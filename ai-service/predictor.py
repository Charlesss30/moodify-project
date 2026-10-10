import hashlib
import json
import math
from pathlib import Path


SCORE_TYPE = "uncalibrated_sigmoid_not_accuracy"


def validate_inference_config(config: dict) -> list[str]:
    labels = config.get("labels")
    thresholds = config.get("thresholds")
    policy = config.get("selective_policy")
    if (
        not isinstance(labels, list)
        or len(labels) != 28
        or any(not isinstance(label, str) or not label for label in labels)
        or len(set(labels)) != 28
        or not isinstance(thresholds, list)
        or len(thresholds) != 28
        or any(
            not isinstance(value, (int, float))
            or not math.isfinite(value)
            or value < 0
            or value > 1
            for value in thresholds
        )
        or config.get("max_length") != 128
        or config.get("language") != "en"
        or not isinstance(policy, dict)
        or not isinstance(policy.get("enabled"), bool)
        or not isinstance(policy.get("threshold"), (int, float))
        or not math.isfinite(policy["threshold"])
        or policy["threshold"] < 0
        or policy["threshold"] > 1
    ):
        raise ValueError("Inference configuration does not match the expected model contract.")
    return labels


class EmotionPredictor:
    def __init__(self, model_dir: str | Path, expected_sha256: str):
        root = Path(model_dir).expanduser().resolve()
        model_path = root / "model.safetensors"
        if not model_path.is_file():
            raise FileNotFoundError("Model weights are unavailable.")
        self.artifact_hash = self._sha256(model_path)
        if self.artifact_hash != expected_sha256.lower():
            raise ValueError("Model artifact checksum does not match configuration.")

        self.config = json.loads((root / "inference_config.json").read_text(encoding="utf-8"))
        self.config_hash = self._sha256(root / "inference_config.json")
        labels = validate_inference_config(self.config)

        import torch
        from transformers import AutoModelForSequenceClassification, AutoTokenizer

        self.device = "cuda" if torch.cuda.is_available() else "cpu"
        self.device_name = (
            torch.cuda.get_device_name(0) if self.device == "cuda" else "CPU"
        )
        self.tokenizer = AutoTokenizer.from_pretrained(
            root, local_files_only=True, trust_remote_code=False
        )
        self.model = AutoModelForSequenceClassification.from_pretrained(
            root, local_files_only=True, trust_remote_code=False
        ).to(self.device).eval()
        actual = [self.model.config.id2label[i] for i in range(len(labels))]
        if (
            actual != labels
            or self.model.config.num_labels != len(labels)
            or self.model.config.problem_type != "multi_label_classification"
        ):
            raise ValueError("Loaded model does not match the inference configuration.")
        self._torch = torch

    @staticmethod
    def _sha256(path: Path) -> str:
        digest = hashlib.sha256()
        with path.open("rb") as artifact:
            for chunk in iter(lambda: artifact.read(1024 * 1024), b""):
                digest.update(chunk)
        return digest.hexdigest()

    def predict(self, text: str) -> dict:
        if not isinstance(text, str) or not text.strip() or len(text) > 4000:
            raise ValueError("Provide 1–4000 characters of English text.")
        text = text.strip()
        if self.config.get("language") != "en":
            raise ValueError("This model supports English only.")
        if len(self.tokenizer(text, truncation=False)["input_ids"]) > 128:
            raise ValueError("Input exceeds 128 model tokens; shorten the text.")

        inputs = self.tokenizer(text, return_tensors="pt").to(self.device)
        with self._torch.inference_mode():
            scores = self.model(**inputs).logits.float().sigmoid()[0].cpu().tolist()
        if len(scores) != 28 or any(
            not math.isfinite(score) or score < 0 or score > 1 for score in scores
        ):
            raise ValueError("Model returned scores outside the supported contract.")

        labels = self.config["labels"]
        order = sorted(range(len(scores)), key=lambda index: scores[index], reverse=True)
        top = order[0]
        policy = self.config["selective_policy"]
        decision_indices = [
            index
            for index in order
            if scores[index] >= self.config["thresholds"][index]
        ]
        accepted = (
            policy["enabled"]
            and scores[top] >= policy["threshold"]
            and top in decision_indices
        )
        return {
            "model_version": self.config["run_id"],
            "language": "en",
            "status": "accepted" if accepted else "needs_confirmation",
            "suggested_emotion": labels[top],
            "score": float(scores[top]),
            "score_type": SCORE_TYPE,
            "requires_confirmation": True,
            "decision_eligible": accepted,
            "decision_candidates": [
                {"label": labels[index], "score": float(scores[index])}
                for index in decision_indices
            ],
            "predicted_labels": [
                labels[index]
                for index in decision_indices
            ],
            "candidates": [
                {"label": labels[index], "score": float(scores[index])}
                for index in order[:5]
            ],
            "emotion_scores": dict(zip(labels, scores)),
        }
