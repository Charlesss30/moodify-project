"""Local, artifact-configured Vietnamese emotion classifier."""
import hashlib
import json
import math
import os
import re
from pathlib import Path

import torch
from transformers import AutoModelForSequenceClassification, AutoTokenizer


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _optional_threshold(value: object, name: str) -> float | None:
    if value is None or value == "":
        return None
    try:
        threshold = float(value)
    except (TypeError, ValueError) as error:
        raise ValueError(f"{name} must be a number between 0 and 1.") from error
    if not math.isfinite(threshold) or not 0 <= threshold <= 1:
        raise ValueError(f"{name} must be a number between 0 and 1.")
    return threshold


class VietnameseEmotionPredictor:
    def __init__(self, model_dir: str | Path, model_version: str):
        self.model_dir = Path(model_dir).expanduser().resolve()
        self.artifact_dir = self.model_dir.parent
        if not (self.model_dir / "config.json").is_file() or not any(
            (self.model_dir / name).is_file()
            for name in ("model.safetensors", "pytorch_model.bin", "model.pt")
        ):
            raise FileNotFoundError("Vietnamese model requires config.json and model weights.")
        self.model_version = model_version
        self.device = "cuda" if torch.cuda.is_available() else "cpu"
        self.config_payload = json.loads((self.artifact_dir / "label_config.json").read_text(encoding="utf-8-sig"))
        self.labels = self.config_payload.get("labels")
        self.id2label = self.config_payload.get("id2label")
        self.anchors = self.config_payload.get("va_anchors")
        if (not isinstance(self.labels, list) or len(self.labels) != 6
                or len(set(self.labels)) != 6 or not all(isinstance(x, str) and x for x in self.labels)
                or not isinstance(self.anchors, dict)
                or set(self.anchors) != set(self.labels)):
            raise ValueError("Vietnamese label_config.json must define six labels and matching VA anchors.")
        model_config = json.loads((self.model_dir / "config.json").read_text(encoding="utf-8-sig"))
        configured_labels = model_config.get("id2label", {})
        actual_labels = [configured_labels.get(str(i), configured_labels.get(i)) for i in range(6)]
        if actual_labels != self.labels or model_config.get("num_labels", 6) != 6:
            raise ValueError("Vietnamese model label order does not match label_config.json.")
        if model_config.get("problem_type") not in (None, "single_label_classification"):
            raise ValueError("Vietnamese candidate must be a single-label classifier.")
        self.max_length = int(self.config_payload.get("max_len", 96))
        if self.max_length < 2 or self.max_length > int(model_config.get("max_position_embeddings", 514)) - 2:
            raise ValueError("Vietnamese max_len is incompatible with the model position embeddings.")
        manifest_path = self.artifact_dir / "manifest.json"
        if not manifest_path.is_file():
            raise FileNotFoundError("Vietnamese candidate requires manifest.json with its artifact SHA-256.")
        manifest = json.loads(manifest_path.read_text(encoding="utf-8-sig"))
        weights = next((self.model_dir / name for name in ("model.safetensors", "pytorch_model.bin", "model.pt") if (self.model_dir / name).is_file()), None)
        if (weights is None or manifest.get("language") != "vi"
                or manifest.get("model_version") != model_version
                or manifest.get("weights_sha256") != _sha256(weights)):
            raise ValueError("Vietnamese model weights or version do not match the artifact manifest.")
        preprocessing_path = self.artifact_dir / "preprocessing.json"
        if not preprocessing_path.is_file():
            raise FileNotFoundError("Vietnamese candidate requires preprocessing.json.")
        preprocessing = json.loads(preprocessing_path.read_text(encoding="utf-8-sig"))
        self.emoji_dict = preprocessing.get("emoji_dict", {})
        self.teencode_dict = preprocessing.get("teencode_dict", {})
        if not isinstance(self.emoji_dict, dict) or not isinstance(self.teencode_dict, dict):
            raise ValueError("Vietnamese preprocessing dictionaries must be JSON objects.")
        self.confidence_threshold = _optional_threshold(
            os.environ.get("VI_CONFIDENCE_THRESHOLD"), "VI_CONFIDENCE_THRESHOLD"
        )
        self.tokenizer = AutoTokenizer.from_pretrained(self.model_dir, local_files_only=True, trust_remote_code=False)
        self.model = AutoModelForSequenceClassification.from_pretrained(
            self.model_dir, local_files_only=True, trust_remote_code=False
        )
        if self.model.config.num_labels != len(self.labels):
            raise ValueError("Loaded Vietnamese model must contain exactly six classes.")
        loaded_labels = [self.model.config.id2label.get(i, self.model.config.id2label.get(str(i))) for i in range(6)]
        if loaded_labels != self.labels:
            raise ValueError("Loaded Vietnamese class order does not match label_config.json.")
        self.max_length = min(self.max_length, int(self.model.config.max_position_embeddings) - 2)
        try:
            from pyvi import ViTokenizer
        except ImportError as error:
            raise RuntimeError("Install pyvi to serve the Vietnamese PhoBERT model.") from error
        self._word_tokenizer = ViTokenizer
        self.model.to(self.device).eval()

    def preprocess(self, text: str) -> str:
        if not isinstance(text, str) or not text.strip() or len(text) > 4000:
            raise ValueError("Provide 1–4000 characters of Vietnamese text.")
        text = text.strip()
        if self.emoji_dict and any(ord(char) > 0x2000 for char in text):
            for emoji, description in sorted(self.emoji_dict.items(), key=lambda item: len(item[0]), reverse=True):
                if emoji in text:
                    text = text.replace(emoji, f" {description} ")
        text = re.sub(r"[^\w\s]", " ", text, flags=re.UNICODE)
        words = [self.teencode_dict.get(word, word) for word in text.lower().split()]
        text = re.sub(r"\s+", " ", " ".join(words)).strip()
        if not text:
            raise ValueError("Text is empty after Vietnamese text normalization.")
        return self._word_tokenizer.tokenize(text).lower()

    def predict(self, text: str) -> dict:
        normalized = self.preprocess(text)
        encoded = self.tokenizer(
            normalized, return_tensors="pt", max_length=self.max_length,
            padding="max_length", truncation=True,
        )
        encoded = {key: value.to(self.device) for key, value in encoded.items()}
        with torch.inference_mode():
            probabilities = self.model(**encoded).logits.float().softmax(dim=-1)[0].cpu().tolist()
        if len(probabilities) != len(self.labels) or any(not math.isfinite(float(value)) for value in probabilities):
            raise ValueError("Vietnamese model returned invalid class probabilities.")
        order = sorted(range(len(probabilities)), key=probabilities.__getitem__, reverse=True)
        best = order[0]
        confidence = float(probabilities[best])
        anchors = [self.anchors[label] for label in self.labels]
        if any(not isinstance(pair, list) or len(pair) != 2 or any(
            not isinstance(x, (int, float)) or not math.isfinite(x) or not -1 <= x <= 1 for x in pair
        ) for pair in anchors):
            raise ValueError("Vietnamese valence/arousal anchors must be finite pairs in [-1, 1].")
        return {
            "model_version": self.model_version,
            "language": "vi",
            "emotion": self.labels[best],
            "confidence": confidence,
            "accepted": None if self.confidence_threshold is None else confidence >= self.confidence_threshold,
            "emotion_candidates": [
                {"label": self.labels[index], "score": float(probabilities[index])}
                for index in order[:3]
            ],
            "probabilities": {self.labels[index]: float(probabilities[index]) for index in range(len(self.labels))},
            "valence": float(sum(float(probabilities[i]) * float(anchors[i][0]) for i in range(len(self.labels)))),
            "arousal": float(sum(float(probabilities[i]) * float(anchors[i][1]) for i in range(len(self.labels)))),
            "coordinate_range": [-1.0, 1.0],
        }
