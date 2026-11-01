import math
import unittest
from evaluate_bilingual import score


def data():
    return [
        {"id": "a", "text": "First reviewed description", "language": "vi", "split": "test", "reviewed": True,
         "emotion": "Anger", "valence": 0.0, "arousal": 0.0},
        {"id": "b", "text": "Second reviewed description", "language": "vi", "split": "test", "reviewed": True,
         "emotion": "Enjoyment", "valence": 1.0, "arousal": -1.0},
    ]


def predictions():
    return [
        {"id": "a", "language": "vi", "modelVersion": "fixture", "mappingVersion": "policy-v1", "accepted": True,
         "emotion": "Anger", "valence": 0.2, "arousal": 0.4},
        {"id": "b", "language": "vi", "modelVersion": "fixture", "mappingVersion": "policy-v1", "accepted": False,
         "emotion": "Anger", "valence": 0.6, "arousal": -0.6},
    ]


class EvaluationTests(unittest.TestCase):
    def test_known_confusion_and_selective_metrics(self):
        report = score(data(), predictions())["groups"][0]
        self.assertAlmostEqual(report["classification"]["macroF1AllSevenLabels"], (2 / 3) / 7)
        self.assertEqual(report["classification"]["confusionMatrix"][0][0], 1)
        self.assertEqual(report["classification"]["coverage"], 0.5)
        self.assertEqual(report["classification"]["selectiveAccuracy"], 1)
        self.assertAlmostEqual(report["va"]["valence"]["mae"], 0.3)
        self.assertAlmostEqual(report["va"]["valence"]["rmse"], math.sqrt(0.1))
        self.assertAlmostEqual(report["va"]["arousal"]["mae"], 0.4)

    def test_no_accepted_predictions_has_no_selective_accuracy(self):
        p = predictions()
        p[0]["accepted"] = False
        self.assertIsNone(score(data(), p)["groups"][0]["classification"]["selectiveAccuracy"])

    def test_rejects_unreviewed_template(self):
        rows = data()
        rows[0]["reviewed"] = False
        with self.assertRaisesRegex(ValueError, "reviewed"):
            score(rows, predictions())

    def test_detects_cross_split_duplicate(self):
        rows = data()
        rows.append({**rows[0], "id": "train-a", "text": " FIRST  reviewed description ", "split": "train"})
        with self.assertRaisesRegex(ValueError, "Duplicate"):
            score(rows, predictions())

    def test_invalid_va_and_bool_are_rejected(self):
        for value in (float("nan"), float("inf"), True, 1.1):
            p = predictions()
            p[0]["valence"] = value
            with self.assertRaisesRegex(ValueError, "finite"):
                score(data(), p)

    def test_cannot_pool_separate_model_versions(self):
        p = predictions()
        p[1]["modelVersion"] = "other-version"
        self.assertEqual(len(score(data(), p)["groups"]), 2)

    def test_missing_predictions_and_wrong_languages_fail(self):
        with self.assertRaises(ValueError):
            score(data(), predictions()[:1])
        p = predictions()
        p[0]["language"] = "en"
        with self.assertRaises(ValueError):
            score(data(), p)


if __name__ == "__main__":
    unittest.main()
