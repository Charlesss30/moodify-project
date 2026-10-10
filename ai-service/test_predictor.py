import contextlib
import math
import unittest
from types import SimpleNamespace

from predictor import EmotionPredictor, SCORE_TYPE, validate_inference_config


LABELS = [f"label_{index}" for index in range(28)]


class FakeTokenizer:
    def __init__(self, token_count):
        self.token_count = token_count

    def __call__(self, text, **kwargs):
        if kwargs.get("truncation") is False:
            return {"input_ids": [1] * self.token_count}
        return FakeInputs()


class FakeInputs(dict):
    def to(self, _device):
        return self


class FakeTensor:
    def __init__(self, values):
        self.values = values

    def float(self):
        return self

    def sigmoid(self):
        return self

    def __getitem__(self, _index):
        return self

    def cpu(self):
        return self

    def tolist(self):
        return self.values


class FakeTorch:
    @staticmethod
    def inference_mode():
        return contextlib.nullcontext()


class PredictorContractTests(unittest.TestCase):
    def test_config_requires_all_thresholds_and_english_language(self):
        config = {
            "labels": LABELS,
            "thresholds": [0.5] * 28,
            "max_length": 128,
            "language": "en",
            "selective_policy": {"enabled": True, "threshold": 0.955},
        }
        self.assertEqual(validate_inference_config(config), LABELS)
        config["thresholds"] = [0.5] * 27
        with self.assertRaises(ValueError):
            validate_inference_config(config)
        config["thresholds"] = [0.5] * 28
        config["language"] = "vi"
        with self.assertRaises(ValueError):
            validate_inference_config(config)

    def predictor(self, token_count=3, scores=None):
        predictor = object.__new__(EmotionPredictor)
        predictor.config = {
            "run_id": "test-model",
            "language": "en",
            "labels": LABELS,
            "thresholds": [0.5] * 28,
            "max_length": 128,
            "selective_policy": {"enabled": True, "threshold": 0.955},
        }
        predictor.device = "cpu"
        predictor.tokenizer = FakeTokenizer(token_count)
        probabilities = scores or [0.1] * 28
        predictor.model = lambda **_: SimpleNamespace(logits=FakeTensor(probabilities))
        predictor._torch = FakeTorch()
        return predictor

    def test_128_tokens_are_allowed_and_keep_label_order(self):
        scores = [0.1] * 28
        scores[4] = 0.99
        result = self.predictor(token_count=128, scores=scores).predict("English text")
        self.assertEqual(len(result["emotion_scores"]), 28)
        self.assertEqual(result["candidates"][0]["label"], LABELS[4])
        self.assertEqual(result["score_type"], SCORE_TYPE)
        self.assertTrue(result["requires_confirmation"])
        self.assertTrue(result["decision_eligible"])
        self.assertEqual(result["decision_candidates"][0]["label"], LABELS[4])

    def test_display_candidates_do_not_imply_decision_eligibility(self):
        scores = [0.1] * 28
        scores[3] = 0.9
        result = self.predictor(scores=scores).predict("English text")
        self.assertEqual(result["candidates"][0]["label"], LABELS[3])
        self.assertEqual(result["decision_candidates"], [{"label": LABELS[3], "score": 0.9}])
        self.assertFalse(result["decision_eligible"])
        self.assertEqual(result["status"], "needs_confirmation")

    def test_129_tokens_are_rejected_without_truncation(self):
        with self.assertRaisesRegex(ValueError, "128 model tokens"):
            self.predictor(token_count=129).predict("English text")

    def test_blank_and_overlong_text_are_rejected(self):
        predictor = self.predictor()
        for text in ("  \t ", "x" * 4001):
            with self.subTest(length=len(text)), self.assertRaises(ValueError):
                predictor.predict(text)

    def test_invalid_scores_are_rejected(self):
        scores = [0.1] * 27 + [math.nan]
        with self.assertRaisesRegex(ValueError, "outside the supported contract"):
            self.predictor(scores=scores).predict("English text")


if __name__ == "__main__":
    unittest.main()
