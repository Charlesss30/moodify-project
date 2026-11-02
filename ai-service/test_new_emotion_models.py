import unittest
from types import SimpleNamespace

from emotion_predictor import VietnameseSixEmotionPredictor as VietnameseEmotionPredictor
from emotion_predictor import EnglishEmotionPredictor as ValenceArousalPredictor


LABELS = ["Sadness", "Enjoyment", "Love", "Anger", "Fear", "Surprise"]
ANCHORS = {
    "Sadness": [-0.8, -0.6], "Enjoyment": [0.8, 0.7], "Love": [0.8, 0.3],
    "Anger": [-0.8, 0.8], "Fear": [-0.7, 0.9], "Surprise": [0.5, 0.9],
}


class FakeInput:
    def to(self, _device):
        return self


class FakeTokenizer:
    def __init__(self):
        self.seen = None

    def __call__(self, text, **kwargs):
        self.seen = (text, kwargs)
        return {"input_ids": FakeInput(), "attention_mask": FakeInput()}


class FakeLogits:
    def __init__(self, values):
        self.values = values

    def float(self):
        return self

    def softmax(self, dim=-1):
        return self

    def __getitem__(self, _index):
        return self

    def cpu(self):
        return self

    def tolist(self):
        return self.values


class EmotionModelContractTests(unittest.TestCase):
    def make_predictor(self, predictor_type, language):
        predictor = object.__new__(predictor_type)
        predictor.language = language
        predictor.model_type = "emotion_classification_va_adapter"
        predictor.taxonomy = "moodify-six-v1"
        predictor.calibration_status = "uncalibrated"
        predictor.use_word_segmentation = language == "vi"
        predictor.model_version = "moodify_test_6c"
        predictor.device = "cpu"
        predictor.labels = LABELS
        predictor.anchors = ANCHORS
        predictor.max_length = 96 if language == "vi" else 64
        predictor.confidence_threshold = None
        predictor.tokenizer = FakeTokenizer()
        predictor.model = lambda **_: SimpleNamespace(logits=FakeLogits([0.02, 0.03, 0.80, 0.04, 0.05, 0.06]))
        if language == "vi":
            predictor.emoji_dict = {"😂": "cười"}
            predictor.teencode_dict = {"ko": "không"}
            predictor._word_tokenizer = SimpleNamespace(tokenize=lambda value: value.replace(" vui ", " vui_seg "))
        else:
            predictor.is_classifier = True
        return predictor

    def test_vietnamese_preprocessing_matches_training_order(self):
        predictor = self.make_predictor(VietnameseEmotionPredictor, "vi")
        result = predictor.preprocess("KO vui 😂!")
        self.assertEqual(result, "không vui_seg cười")
        self.assertEqual(predictor.preprocess("TÔI vẫn đúng"), "tôi vẫn đúng")
        with self.assertRaises(ValueError):
            predictor.preprocess("  ")

    def test_both_six_class_routes_return_emotion_and_anchor_va(self):
        for predictor_type, language in ((VietnameseEmotionPredictor, "vi"), (ValenceArousalPredictor, "en")):
            with self.subTest(language=language):
                predictor = self.make_predictor(predictor_type, language)
                result = predictor.predict("A sample sentence")
                self.assertEqual(result["language"], language)
                self.assertEqual(result["emotion"], "Love")
                self.assertAlmostEqual(result["confidence"], 0.8)
                self.assertIsNone(result["accepted"])
                self.assertEqual(len(result["emotion_candidates"]), 3)
                expected_v = sum(p * ANCHORS[label][0] for p, label in zip(
                    [0.02, 0.03, 0.80, 0.04, 0.05, 0.06], LABELS))
                expected_a = sum(p * ANCHORS[label][1] for p, label in zip(
                    [0.02, 0.03, 0.80, 0.04, 0.05, 0.06], LABELS))
                self.assertAlmostEqual(result["valence"], expected_v)
                self.assertAlmostEqual(result["arousal"], expected_a)
                self.assertEqual(predictor.tokenizer.seen[1]["max_length"], predictor.max_length)

    def test_classifier_input_validation(self):
        predictor = self.make_predictor(ValenceArousalPredictor, "en")
        for text in ("  ", "x" * 4001):
            with self.subTest(length=len(text)), self.assertRaises(ValueError):
                predictor.predict(text)


if __name__ == "__main__":
    unittest.main()
