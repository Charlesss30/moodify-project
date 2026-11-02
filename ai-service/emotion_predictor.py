"""Serving adapter for exported English and Vietnamese six-emotion bundles."""
import math
import re

import torch
from transformers import AutoModelForSequenceClassification, AutoTokenizer

from emotion_bundle import inspect_bundle, fingerprint_bundle, MODEL_TYPE, TAXONOMY


class SixEmotionPredictor:
    def __init__(self, artifact_root, model_version, language):
        bundle = inspect_bundle(artifact_root, language, model_version)
        before = fingerprint_bundle(bundle.root, language, model_version)
        self.language = language
        self.model_version = model_version
        self.model_type = MODEL_TYPE
        self.taxonomy = TAXONOMY
        self.labels = list(bundle.labels)
        self.anchors = bundle.anchors
        self.max_length = bundle.max_length
        self.emoji_dict, self.teencode_dict = bundle.emoji, bundle.teencode
        self.use_word_segmentation = bundle.segmentation
        self.confidence_threshold = bundle.threshold
        self.calibration_status = "uncalibrated" if bundle.threshold is None else "threshold_configured"
        self.device = "cuda" if torch.cuda.is_available() else "cpu"
        self._word_tokenizer = None
        if language == "vi" and bundle.segmentation:
            try:
                from pyvi import ViTokenizer
            except ImportError as error:
                raise RuntimeError("Install pyvi to use this exported Vietnamese classifier.") from error
            self._word_tokenizer = ViTokenizer
        self.tokenizer = AutoTokenizer.from_pretrained(bundle.model_dir, local_files_only=True, trust_remote_code=False)
        self.model = AutoModelForSequenceClassification.from_pretrained(bundle.model_dir, local_files_only=True, trust_remote_code=False)
        actual = [self.model.config.id2label.get(i, self.model.config.id2label.get(str(i))) for i in range(6)]
        if self.model.config.num_labels != 6 or actual != self.labels:
            raise ValueError("Loaded classifier labels do not match the reviewed bundle.")
        self.model.to(self.device).eval()
        self.artifact_fingerprint = fingerprint_bundle(bundle.root, language, model_version)
        if before["bundleSha256"] != self.artifact_fingerprint["bundleSha256"]:
            raise ValueError("Classifier assets changed while loading.")
        self.preprocessing_status = "exported_training_pipeline" if language == "vi" else "saved_tokenizer"

    def preprocess(self, text):
        if not isinstance(text, str) or not text.strip() or len(text) > 4000:
            raise ValueError("Provide 1–4000 characters of text.")
        text = text.strip()
        if self.language == "en":
            return text
        # Preserve the exact dictionary iteration and processing order exported
        # by Khang's Vietnamese training notebook, including segmentation flag.
        if self.emoji_dict and any(ord(c) > 0x2000 for c in text):
            for emoji, description in self.emoji_dict.items():
                text = text.replace(emoji, f" {description} ")
        text = re.sub(r"[^\w\s]", " ", text, flags=re.UNICODE)
        words = [self.teencode_dict.get(word, word) for word in text.lower().split()]
        text = re.sub(r"\s+", " ", " ".join(words)).strip()
        if not text:
            raise ValueError("Text is empty after the exported preprocessing pipeline.")
        if self.use_word_segmentation:
            text = self._word_tokenizer.tokenize(text)
        return text.lower()

    def predict(self, text):
        normalized = self.preprocess(text)
        tokens = self.tokenizer(normalized, return_tensors="pt", max_length=self.max_length,
                                truncation=True, padding="max_length")
        tokens = {key: value.to(self.device) for key, value in tokens.items()}
        with torch.inference_mode():
            probabilities = self.model(**tokens).logits.float().softmax(dim=-1)[0].cpu().tolist()
        if (len(probabilities) != len(self.labels) or any(not math.isfinite(p) or not 0 <= p <= 1 for p in probabilities)
                or abs(sum(probabilities) - 1) > 0.0001):
            raise ValueError("Classifier returned an invalid probability distribution.")
        order = sorted(range(len(probabilities)), key=probabilities.__getitem__, reverse=True)
        confidence = float(probabilities[order[0]])
        return {"model_version": self.model_version, "language": self.language,
                "model_type": self.model_type, "taxonomy": self.taxonomy,
                "score_type": "softmax_not_accuracy", "calibration_status": self.calibration_status,
                "emotion": self.labels[order[0]], "confidence": confidence,
                "accepted": None if self.confidence_threshold is None else confidence >= self.confidence_threshold,
                "emotion_candidates": [{"label": self.labels[i], "score": float(probabilities[i])} for i in order[:3]],
                "probabilities": {label: float(p) for label, p in zip(self.labels, probabilities)},
                "valence": float(sum(p * self.anchors[label][0] for label, p in zip(self.labels, probabilities))),
                "arousal": float(sum(p * self.anchors[label][1] for label, p in zip(self.labels, probabilities))),
                "coordinate_range": [-1.0, 1.0]}


class EnglishEmotionPredictor(SixEmotionPredictor):
    def __init__(self, artifact_root, model_version):
        super().__init__(artifact_root, model_version, "en")


class VietnameseSixEmotionPredictor(SixEmotionPredictor):
    def __init__(self, artifact_root, model_version):
        super().__init__(artifact_root, model_version, "vi")
