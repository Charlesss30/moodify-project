"""Local PhoBERT VSMEC classifier adapter for Vietnamese mood analysis."""
import json
import math
import hashlib
from vietnamese_preprocessing import normalize, validate
from artifact_fingerprint import fingerprint_files
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
        before = fingerprint_files("vi", root, model_version)
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
        self.selective_enabled = True
        preprocessing = root / "preprocessing.json"
        if preprocessing.is_file():
            payload = validate(json.loads(preprocessing.read_text(encoding="utf-8-sig")))
            self.emoji_dict = payload.get("emoji_dict", self.emoji_dict)
            self.teencode_dict = payload.get("teencode_dict", {})
        self.preprocessing_status = "bundled_training_parity_not_verified" if preprocessing.is_file() else "legacy_fallback_missing_training_export"
        material = preprocessing.read_bytes() if preprocessing.is_file() else json.dumps(
            {"emoji_dict": self.emoji_dict, "teencode_dict": self.teencode_dict}, sort_keys=True, ensure_ascii=False).encode()
        self.preprocessing_hash = hashlib.sha256(material).hexdigest()
        threshold_file = root / "confidence_threshold.json"
        if threshold_file.is_file():
            threshold_payload = json.loads(threshold_file.read_text(encoding="utf-8"))
            threshold = threshold_payload.get("confidence_threshold")
            if type(threshold) not in (int, float) or not math.isfinite(threshold) or not 0 <= threshold <= 1:
                raise ValueError("Invalid confidence threshold; refusing an unsafe fallback.")
            self.confidence_threshold = float(threshold)
            enabled = threshold_payload.get("enabled", True)
            if type(enabled) is not bool:
                raise ValueError("Selective acceptance must be boolean.")
            self.selective_enabled = enabled
        try:
            from pyvi import ViTokenizer
        except ImportError as error:
            raise RuntimeError("Install pyvi to serve the Vietnamese PhoBERT model.") from error
        self._word_tokenizer = ViTokenizer
        self.model.to(self.device).eval()
        self.artifact_fingerprint = fingerprint_files("vi", root, model_version)
        if before["bundleSha256"] != self.artifact_fingerprint["bundleSha256"]:
            raise ValueError("Model files changed during loading; refusing a mixed artifact.")

    def predict(self, text: str) -> dict:
        if not isinstance(text, str) or not text.strip() or len(text) > 4000:
            raise ValueError("Provide 1–4000 characters of Vietnamese text.")
        text = normalize(text, self.emoji_dict, self.teencode_dict, self._word_tokenizer)
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
            "accepted": self.selective_enabled and confidence >= self.confidence_threshold,
            "valence": float(sum(float(probabilities[index]) * self.VA_ANCHORS[index][0] for index in range(len(self.LABELS)))),
            "arousal": float(sum(float(probabilities[index]) * self.VA_ANCHORS[index][1] for index in range(len(self.LABELS)))),
            "coordinate_range": [-1.0, 1.0],
        }
