import copy
import json
from pathlib import Path
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch

from matrix_factorization import train, temporal_split, predict
from vietnamese_preprocessing import normalize
import main


class MatrixFactorizationTests(unittest.TestCase):
    def rows(self):
        rows = []
        for split, day in enumerate((1, 11, 21)):
            for user in range(1, 9):
                for item in range(4):
                    stars = 5 if (user % 2 == item % 2) else 1
                    rows.append({"userId": user, "contentId": str(item), "stars": stars,
                                 "timestamp": f"2024-01-{day:02d}T00:00:00Z"})
        return rows

    def test_learns_and_selects_without_future_feedback(self):
        model = train(self.rows(), "2024-01-10T00:00:00Z", "2024-01-20T00:00:00Z",
                      "mf_test", epochs=120, factors=4)
        self.assertLess(model["report"]["test"]["rmse"], model["report"]["test"]["meanBaselineRmse"])
        self.assertGreater(predict(model, 1, "1"), predict(model, 1, "0"))
        changed = self.rows()
        for r in changed:
            if r["timestamp"].startswith("2024-01-21"):
                r["stars"] = 3
        other = train(changed, "2024-01-10T00:00:00Z", "2024-01-20T00:00:00Z", "mf_test", epochs=120, factors=4)
        self.assertEqual(model["users"], other["users"])
        self.assertEqual(model["items"], other["items"])

    def test_seed_determinism_and_cold_start(self):
        args = (self.rows(), "2024-01-10T00:00:00Z", "2024-01-20T00:00:00Z", "mf_test")
        a, b = train(*args, epochs=5), train(*args, epochs=5)
        self.assertEqual(a["users"], b["users"])
        self.assertEqual(predict(a, 9999, "new"), a["globalMean"])

    def test_latest_rating_and_temporal_boundaries(self):
        rows = [{"userId": 1, "contentId": "x", "stars": n, "timestamp": f"2024-01-0{n}T00:00:00Z"} for n in (1, 2, 3)]
        training, _, _ = temporal_split(rows, "2024-01-10T00:00:00Z", "2024-01-20T00:00:00Z")
        self.assertEqual(training[0]["stars"], 3)
        with self.assertRaises(ValueError):
            temporal_split(rows, "2024-01-20T00:00:00Z", "2024-01-10T00:00:00Z")


class PreprocessingTests(unittest.TestCase):
    def test_same_normalizer_is_available_to_training_and_serving(self):
        tokenizer = SimpleNamespace(tokenize=lambda x: x.replace("hạnh phúc", "hạnh_phúc"))
        self.assertEqual(normalize("😭!!! KO Hạnh Phúc", {"😭": "khóc buồn"}, {"ko": "không"}, tokenizer),
                         "khóc buồn không hạnh_phúc")
        with self.assertRaises(ValueError):
            normalize("!!!", {}, {}, tokenizer)


class LifecycleTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.root = Path(self.directory.name)
        self.env = patch.dict("os.environ", {"VA_ACTIVE_MODEL_FILE": str(self.root / "va.txt"),
                                           "VI_ACTIVE_MODEL_FILE": str(self.root / "vi.txt"),
                                           "VA_MODELS_ROOT": str(self.root)})
        self.env.start()
        self.loader = patch.object(main, "load_candidate", side_effect=lambda kind, version: SimpleNamespace(model_version=version, device="cpu"))
        self.loader.start()
        for kind in ("va", "vi"):
            setattr(main.app.state, kind + "_active_version", None)
            setattr(main.app.state, kind + "_predictor", None)
        for version in ("artifact_a", "artifact_b"):
            folder = self.root / version
            (folder / "tokenizer").mkdir(parents=True)
            (folder / (version + ".pt")).write_bytes(version.encode())
            (folder / "tokenizer/tokenizer.json").write_text("{}")

    def tearDown(self):
        self.loader.stop()
        self.env.stop()
        self.directory.cleanup()
        for kind in ("va", "vi"):
            setattr(main.app.state, kind + "_predictor", None)
            setattr(main.app.state, kind + "_active_version", None)

    def test_validate_activate_rollback_and_restart_selection(self):
        main.model_lifecycle("va", "validate", "artifact_a")
        self.assertIsNone(main.app.state.va_predictor)
        main.model_lifecycle("va", "activate", "artifact_a")
        main.model_lifecycle("va", "activate", "artifact_b")
        self.assertEqual(main.lifecycle_state("va")["previous_version"], "artifact_a")
        main.model_lifecycle("va", "rollback", expected_revision=2)
        self.assertEqual(main.active_va_version(), "artifact_a")
        self.assertIsNone(main.app.state.vi_predictor)
        main.model_lifecycle("va", "deactivate", expected_revision=3)
        self.assertTrue(main.disabled_by_operator("va"))
        self.assertIsNone(main.active_va_version())

    def test_revision_and_failed_candidate_preserve_current_model(self):
        main.model_lifecycle("vi", "activate", "artifact_a")
        with self.assertRaises(main.HTTPException) as caught:
            main.model_lifecycle("vi", "deactivate", expected_revision=0)
        self.assertEqual(caught.exception.status_code, 409)
        with patch.object(main, "load_candidate", side_effect=ValueError("Fixture invalid artifact")):
            with self.assertRaises(main.HTTPException) as caught:
                main.model_lifecycle("vi", "activate", "artifact_b")
        self.assertEqual(caught.exception.status_code, 422)
        self.assertEqual(main.app.state.vi_active_version, "artifact_a")
        self.assertEqual(main.lifecycle_state("vi")["active_version"], "artifact_a")

    def test_expected_fingerprint_cannot_switch_or_relabel_loaded_weights(self):
        main.model_lifecycle("va", "activate", "artifact_a")
        for version in ("artifact_b", "artifact_a"):
            with self.assertRaises(main.HTTPException) as caught:
                main.model_lifecycle("va", "activate", version, expected_fingerprint="f"*64)
            self.assertEqual(caught.exception.status_code, 409)
            self.assertEqual(main.lifecycle_state("va")["active_version"], "artifact_a")

    def test_pinned_startup_rejects_changed_artifact(self):
        from types import SimpleNamespace
        main.persist_lifecycle("va", "artifact_a", None, "a"*64)
        candidate = SimpleNamespace(artifact_fingerprint={"bundleSha256":"b"*64})
        with self.assertRaises(ValueError):
            main.verify_loaded_identity("va", candidate)
        candidate.artifact_fingerprint["bundleSha256"] = "a"*64
        self.assertIs(main.verify_loaded_identity("va", candidate), candidate)

    def test_corrupt_state_is_isolated_and_not_overwritten(self):
        path = self.root / "va.txt.lifecycle.json"
        path.write_text("broken")
        self.assertTrue(main.disabled_by_operator("va"))
        with self.assertRaises(main.HTTPException):
            main.model_lifecycle("va", "activate", "artifact_a")
        self.assertEqual(path.read_text(), "broken")


if __name__ == "__main__":
    unittest.main()
