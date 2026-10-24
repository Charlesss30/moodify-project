"""Local PhoBERT VSMEC classifier adapter for Vietnamese mood analysis."""
import json
import math
import re
from pathlib import Path

import torch
from transformers import AutoConfig, AutoModelForSequenceClassification, AutoTokenizer


class VietnameseEmotionPredictor:
    LABELS = ["Anger", "Disgust", "Enjoyment", "Fear", "Other", "Sadness", "Surprise"]
    VA_ANCHORS = [(-0.8, 0.8), (-0.6, 0.4), (0.8, 0.7), (-0.7, 0.9), (0.0, 0.0), (-0.8, -0.6), (0.5, 0.9)]

    def __init__(self, model_dir: str | Path, model_version: str):
        root = Path(model_dir).expanduser().resolve()
        if not (root / "config.json").is_file() or not any((root / name).is_file() for name in ("model.safetensors", "pytorch_model.bin", "model.pt")):
            raise FileNotFoundError("PhoBERT requires config.json and model weights.")
        self.model_version = model_version
        self.device = "cuda" if torch.cuda.is_available() else "cpu"
        self.tokenizer = AutoTokenizer.from_pretrained(root, local_files_only=True, trust_remote_code=False)
        if (root / "model.pt").is_file() and not (root / "model.safetensors").is_file() and not (root / "pytorch_model.bin").is_file():
            config = AutoConfig.from_pretrained(root, local_files_only=True, trust_remote_code=False)
            self.model = AutoModelForSequenceClassification.from_config(config, trust_remote_code=False)
            weights = torch.load(root / "model.pt", map_location="cpu", weights_only=True)
            if not isinstance(weights, dict):
                raise ValueError("PhoBERT checkpoint must be a state dictionary.")
            self.model.load_state_dict(weights, strict=True)
        else:
            self.model = AutoModelForSequenceClassification.from_pretrained(root, local_files_only=True, trust_remote_code=False)
        config = self.model.config
        if config.num_labels != len(self.LABELS) or config.problem_type not in (None, "single_label_classification"):
            raise ValueError("Vietnamese candidate must be a seven-class single-label classifier.")
        actual = [config.id2label.get(index, config.id2label.get(str(index))) for index in range(config.num_labels)]
        if actual != self.LABELS:
            raise ValueError("Vietnamese model labels do not match the expected VSMEC order.")
        self.max_length = min(int(config.max_position_embeddings) - 2, 128)
        self.emoji_dict = {"😂": "cười", "😍": "yêu thích", "😭": "khóc buồn", "😱": "sợ hãi"}
        self.teencode_dict = {}
        self.confidence_threshold = 0.0
        preprocessing = root / "preprocessing.json"
        if preprocessing.is_file():
            payload = json.loads(preprocessing.read_text(encoding="utf-8"))
            self.emoji_dict = payload.get("emoji_dict", self.emoji_dict)
            self.teencode_dict = payload.get("teencode_dict", {})
        threshold_file = root / "confidence_threshold.json"
        if threshold_file.is_file():
            threshold_payload = json.loads(threshold_file.read_text(encoding="utf-8"))
            threshold = threshold_payload.get("confidence_threshold")
            if isinstance(threshold, (int, float)) and math.isfinite(threshold) and 0 <= threshold <= 1:
                self.confidence_threshold = float(threshold)
        try:
            from pyvi import ViTokenizer
        except ImportError as error:
            raise RuntimeError("Install pyvi to serve the Vietnamese PhoBERT model.") from error
        self._word_tokenizer = ViTokenizer
        self.model.to(self.device).eval()

    def predict(self, text: str) -> dict:
        if not isinstance(text, str) or not text.strip() or len(text) > 4000:
            raise ValueError("Provide 1–4000 characters of Vietnamese text.")
        text = text.strip()
        for emoji, description in sorted(self.emoji_dict.items(), key=lambda item: len(item[0]), reverse=True):
            text = text.replace(emoji, f" {description} ")
        text = re.sub(r"[^\w\s]", " ", text, flags=re.UNICODE).lower()
        words = [self.teencode_dict.get(word, word) for word in text.split()]
        text = re.sub(r"\s+", " ", " ".join(words)).strip()
        if not text:
            raise ValueError("Text is empty after Vietnamese text normalization.")
        text = self._word_tokenizer.tokenize(text).lower()
        encoded = self.tokenizer(text, return_tensors="pt", max_length=self.max_length, padding="max_length", truncation=True)
        encoded = {key: value.to(self.device) for key, value in encoded.items()}
        with torch.inference_mode():
            probabilities = self.model(**encoded).logits.float().softmax(dim=-1)[0].cpu().tolist()
        if len(probabilities) != len(self.LABELS) or any(not math.isfinite(float(value)) for value in probabilities):
            raise ValueError("Vietnamese model returned invalid class probabilities.")
        order = sorted(range(len(probabilities)), key=probabilities.__getitem__, reverse=True)
        best = order[0]
        confidence = float(probabilities[best])
        emotion_candidates = [
            {"label": self.LABELS[index], "score": float(probabilities[index])}
            for index in order[:3]
        ]
        return {
            "model_version": self.model_version,
            "language": "vi",
            "emotion": self.LABELS[best],
            "confidence": confidence,
            "emotion_candidates": emotion_candidates,
            "accepted": confidence >= self.confidence_threshold,
            "valence": float(sum(float(probabilities[index]) * self.VA_ANCHORS[index][0] for index in range(len(self.LABELS)))),
            "arousal": float(sum(float(probabilities[index]) * self.VA_ANCHORS[index][1] for index in range(len(self.LABELS)))),
            "coordinate_range": [-1.0, 1.0],
        }
