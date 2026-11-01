import copy
import unittest
from evaluate_hybrid import evaluate


def fixture():
    rows = []
    for split, day in (("validation", "11"), ("test", "21")):
        rows.append({"id": split, "split": split, "reviewed": True, "profileThrough": "2024-01-01T00:00:00Z",
                     "requestedAt": f"2024-01-{day}T00:00:00Z",
                     "items": [{"id": "a", "mood": 1., "content": 0., "collaborative": None, "disliked": False,
                                "distanceSquared": 0., "genres": ["drama"], "futureStars": 1, "labelAt": f"2024-01-{day}T01:00:00Z"},
                               {"id": "b", "mood": .2, "content": 1., "collaborative": None, "disliked": False,
                                "distanceSquared": 2., "genres": ["comedy"], "futureStars": 5, "labelAt": f"2024-01-{day}T01:00:00Z"}]})
    return {"schemaVersion": 1, "sourceEvidence": "Synthetic fixture only, no production quality claim.", "contexts": rows}


class HybridEvaluationTests(unittest.TestCase):
    def test_select_on_validation_and_report_actual_baseline_difference(self):
        result = evaluate(fixture(), 1)
        self.assertEqual(result["validation"]["selectedOnValidation"]["ndcgAtK"], 1)
        self.assertEqual(result["validation"]["currentHybrid"]["ndcgAtK"], 0)
        self.assertEqual(result["test"]["selectedOnValidation"]["collaborativeColdContexts"], 1)

    def test_test_labels_cannot_choose_weights(self):
        data = fixture()
        before = evaluate(data, 1)
        for item in data["contexts"][1]["items"]:
            item["futureStars"] = 6-item["futureStars"]
        self.assertEqual(before["selectedWeights"], evaluate(data, 1)["selectedWeights"])

    def test_future_profile_or_validation_outcomes_rejected(self):
        data = fixture()
        data["contexts"][0]["profileThrough"] = "2025-01-01T00:00:00Z"
        with self.assertRaises(ValueError):
            evaluate(data)
        data = fixture()
        data["contexts"][0]["items"][0]["labelAt"] = "2025-01-01T00:00:00Z"
        with self.assertRaises(ValueError):
            evaluate(data)


if __name__ == "__main__":
    unittest.main()