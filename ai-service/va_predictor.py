"""Inference adapter for the EmoBank RoBERTa valence/arousal checkpoint."""
from pathlib import Path
import math

import torch
from torch import nn
from transformers import RobertaConfig, RobertaModel, RobertaTokenizerFast


class RobertaForVARegression(nn.Module):
    def __init__(self):
        super().__init__()
        # The checkpoint contains the complete roberta-base weights. The
        # default RobertaConfig matches roberta-base without downloading files.
        config = RobertaConfig(max_position_embeddings=514, type_vocab_size=1)
        self.roberta = RobertaModel(config)
        self.dropout = nn.Dropout(0.3)
        self.regressor = nn.Linear(self.roberta.config.hidden_size, 2)

    def forward(self, input_ids, attention_mask):
        outputs = self.roberta(input_ids=input_ids, attention_mask=attention_mask)
        pooled = outputs.last_hidden_state[:, 0, :]
        return self.regressor(self.dropout(pooled))


class ValenceArousalPredictor:
    def __init__(self, checkpoint_path: str | Path, tokenizer_dir: str | Path, model_version: str = "roberta_va_regression"):
        checkpoint = Path(checkpoint_path).expanduser().resolve()
        tokenizer_path = Path(tokenizer_dir).expanduser().resolve()
        if not checkpoint.is_file():
            raise FileNotFoundError("Valence/arousal checkpoint is unavailable.")
        if not (tokenizer_path / "tokenizer.json").is_file():
            raise FileNotFoundError("Saved RoBERTa tokenizer is unavailable.")

        self.device = "cuda" if torch.cuda.is_available() else "cpu"
        self.model_version = model_version
        self.tokenizer = RobertaTokenizerFast.from_pretrained(
            tokenizer_path, local_files_only=True
        )
        self.model = RobertaForVARegression()
        weights = torch.load(checkpoint, map_location="cpu", weights_only=True)
        if not isinstance(weights, dict):
            raise ValueError("Checkpoint must contain a PyTorch state dictionary.")
        self.model.load_state_dict(weights, strict=True)
        self.model.to(self.device).eval()

    def predict(self, text: str) -> dict:
        if not isinstance(text, str) or not text.strip() or len(text) > 4000:
            raise ValueError("Provide 1â€“4000 characters of text.")
        tokens = self.tokenizer(
            text.strip(),
            return_tensors="pt",
            truncation=True,
            padding="max_length",
            max_length=128,
        )
        input_ids = tokens["input_ids"].to(self.device)
        attention_mask = tokens["attention_mask"].to(self.device)
        with torch.inference_mode():
            output = self.model(input_ids, attention_mask)[0].float().clamp(-1.0, 1.0).cpu()
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
