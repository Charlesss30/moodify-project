import json
from pathlib import Path
import tempfile
import unittest
from artifact_fingerprint import fingerprint_files


class ArtifactFingerprintTests(unittest.TestCase):
    def test_weight_tokenizer_and_preprocessing_changes_invalidate_identity(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory)
            (root/"model.safetensors").write_bytes(b"fixture-tensors-not-loaded")
            (root/"config.json").write_text("{}")
            (root/"vocab.txt").write_text("fixture")
            baseline=fingerprint_files("vi",root,"fixture_v1")
            self.assertEqual(baseline,fingerprint_files("vi",root,"fixture_v1"))
            for name in ("preprocessing.json","vocab.txt","model.safetensors"):
                (root/name).write_text('{"changed":true}')
                updated=fingerprint_files("vi",root,"fixture_v1")
                self.assertNotEqual(baseline["bundleSha256"],updated["bundleSha256"])
                baseline=updated

    def test_va_ignores_optimizer_but_binds_tokenizer(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);(root/"tokenizer").mkdir()
            (root/"fixture_va.pt").write_bytes(b"weight")
            (root/"tokenizer/tokenizer.json").write_text("{}")
            before=fingerprint_files("va",root,"fixture_va")
            (root/"optimizer.pt").write_bytes(b"not-a-serving-weight")
            self.assertEqual(before,fingerprint_files("va",root,"fixture_va"))
            (root/"tokenizer/tokenizer.json").write_text('{"changed":true}')
            self.assertNotEqual(before["bundleSha256"],fingerprint_files("va",root,"fixture_va")["bundleSha256"])


if __name__=="__main__":
    unittest.main()