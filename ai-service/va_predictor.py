"""English VA inference adapter with backward-compatible legacy regression support."""
import hashlib
import json
import math
import os
from pathlib import Path

import torch
from torch import nn
from transformers import AutoModelForSequenceClassification, AutoTokenizer, RobertaConfig, RobertaModel, RobertaTokenizerFast


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _threshold(value: object) -> float | None:
    if value is None or value == "":
        return None
    try:
        result = float(value)
    except (TypeError, ValueError) as error:
        raise ValueError("EN_CONFIDENCE_THRESHOLD must be between 0 and 1.") from error
    if not math.isfinite(result) or not 0 <= result <= 1:
        raise ValueError("EN_CONFIDENCE_THRESHOLD must be between 0 and 1.")
    return result


class RobertaForVARegression(nn.Module):
    def __init__(self):
        super().__init__()
        config = RobertaConfig(max_position_embeddings=514, type_vocab_size=1)
        self.roberta = RobertaModel(config)
        self.dropout = nn.Dropout(0.3)
        self.regressor = nn.Linear(self.roberta.config.hidden_size, 2)

    def forward(self, input_ids, attention_mask):
        outputs = self.roberta(input_ids=input_ids, attention_mask=attention_mask)
        pooled = outputs.last_hidden_state[:, 0, :]
        return self.regressor(self.dropout(pooled))


class ValenceArousalPredictor:
    """Loads either a six-class model with VA anchors or the previous .pt regressor."""
    def __init__(self, checkpoint_path: str | Path, tokenizer_dir: str | Path, model_version: str = "roberta_va_regression"):
        candidate = Path(checkpoint_path).expanduser().resolve()
        artifact_dir = Path(tokenizer_dir).expanduser().resolve()
        self.device = "cuda" if torch.cuda.is_available() else "cpu"
        self.model_version = model_version
        self.is_classifier = candidate.is_dir()
        self.labels: list[str] = []
        self.anchors: dict[str, list[float]] = {}
        self.confidence_threshold = _threshold(os.environ.get("EN_CONFIDENCE_THRESHOLD"))
        self.max_length = 128

        if self.is_classifier:
            model_dir = candidate
            config_path = artifact_dir / "label_config.json"
            if not (model_dir / "config.json").is_file() or not config_path.is_file():
                raise FileNotFoundError("English emotion candidate requires model config and label_config.json.")
            self.config_payload = json.loads(config_path.read_text(encoding="utf-8-sig"))
            self.labels = self.config_payload.get("labels", [])
            self.anchors = self.config_payload.get("va_anchors", {})
            if len(self.labels) != 6 or len(set(self.labels)) != 6 or set(self.anchors) != set(self.labels):
                raise ValueError("English label_config.json must declare six labels and matching VA anchors.")
            config = json.loads((model_dir / "config.json").read_text(encoding="utf-8-sig"))
            id2label = config.get("id2label", {})
            model_labels = [id2label.get(str(i), id2label.get(i)) for i in range(6)]
            if model_labels != self.labels or config.get("problem_type") not in (None, "single_label_classification"):
                raise ValueError("English model label order or task does not match label_config.json.")
            self.max_length = int(self.config_payload.get("max_len", 64))
            if self.max_length < 2 or self.max_length > int(config.get("max_position_embeddings", 514)) - 2:
                raise ValueError("English max_len is incompatible with model position embeddings.")
            manifest_path = artifact_dir / "manifest.json"
            if not manifest_path.is_file():
                raise FileNotFoundError("English classifier candidate requires manifest.json with its artifact SHA-256.")
            manifest = json.loads(manifest_path.read_text(encoding="utf-8-sig"))
            weights = model_dir / "model.safetensors"
            if (not weights.is_file() or manifest.get("language") != "en"
                    or manifest.get("model_version") != model_version
                    or manifest.get("weights_sha256") != _sha256(weights)):
                raise ValueError("English classifier weights or version do not match artifact manifest.")
            self.tokenizer = AutoTokenizer.from_pretrained(model_dir, local_files_only=True, trust_remote_code=False)
            self.model = AutoModelForSequenceClassification.from_pretrained(
                model_dir, local_files_only=True, trust_remote_code=False
            )
            loaded_labels = [self.model.config.id2label.get(i, self.model.config.id2label.get(str(i))) for i in range(6)]
            if self.model.config.num_labels != 6 or loaded_labels != self.labels:
                raise ValueError("Loaded English class order does not match label_config.json.")
            self.max_length = min(self.max_length, int(self.model.config.max_position_embeddings) - 2)
        else:
            checkpoint = candidate
            tokenizer_path = artifact_dir
            if not checkpoint.is_file():
                raise FileNotFoundError("Valence/arousal checkpoint is unavailable.")
            if not (tokenizer_path / "tokenizer.json").is_file():
                raise FileNotFoundError("Saved RoBERTa tokenizer is unavailable.")
            self.tokenizer = RobertaTokenizerFast.from_pretrained(tokenizer_path, local_files_only=True)
            self.model = RobertaForVARegression()
            weights = torch.load(checkpoint, map_location="cpu", weights_only=True)
            if not isinstance(weights, dict):
                raise ValueError("Checkpoint must contain a PyTorch state dictionary.")
            self.model.load_state_dict(weights, strict=True)
        self.model.to(self.device).eval()

    def predict(self, text: str) -> dict:
        if not isinstance(text, str) or not text.strip() or len(text) > 4000:
            raise ValueError("Provide 1–4000 characters of English text.")
        tokens = self.tokenizer(
            text.strip(), return_tensors="pt", truncation=True,
            padding="max_length", max_length=self.max_length,
        )
        tokens = {key: value.to(self.device) for key, value in tokens.items()}
        with torch.inference_mode():
            if self.is_classifier:
                probabilities = self.model(**tokens).logits.float().softmax(dim=-1)[0].cpu().tolist()
                if len(probabilities) != len(self.labels) or any(not math.isfinite(float(p)) for p in probabilities):
                    raise ValueError("English classifier returned invalid probabilities.")
                order = sorted(range(len(probabilities)), key=probabilities.__getitem__, reverse=True)
                best = order[0]
                confidence = float(probabilities[best])
                anchors = [self.anchors[label] for label in self.labels]
                if any(not isinstance(pair, list) or len(pair) != 2 or any(
                    not isinstance(x, (int, float)) or not math.isfinite(x) or not -1 <= x <= 1 for x in pair
                ) for pair in anchors):
                    raise ValueError("English VA anchors must be finite pairs in [-1, 1].")
                return {
                    "model_version": self.model_version,
                    "language": "en",
                    "emotion": self.labels[best],
                    "confidence": confidence,
                    "accepted": None if self.confidence_threshold is None else confidence >= self.confidence_threshold,
                    "emotion_candidates": [
                        {"label": self.labels[i], "score": float(probabilities[i])} for i in order[:3]
                    ],
                    "probabilities": {self.labels[i]: float(probabilities[i]) for i in range(len(self.labels))},
                    "valence": float(sum(float(probabilities[i]) * float(anchors[i][0]) for i in range(len(self.labels)))),
                    "arousal": float(sum(float(probabilities[i]) * float(anchors[i][1]) for i in range(len(self.labels)))),
                    "coordinate_range": [-1.0, 1.0],
                }
            output = self.model(tokens["input_ids"], tokens["attention_mask"])[0].float().clamp(-1.0, 1.0).cpu()
        valence, arousal = (float(value) for value in output.tolist())
        if not all(math.isfinite(value) for value in (valence, arousal)):
            raise ValueError("Model returned non-finite valence/arousal values.")
        return {
            "model_version": self.model_version,
            "language": "en",
            "valence": valence,
            "arousal": arousal,
            "coordinate_range": [-1.0, 1.0],
        }
