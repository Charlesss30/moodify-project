"""Explicit local/Colab fine-tuning of an existing six-class package; never deploys."""
import hashlib
import json
import math
from pathlib import Path
import random


def train(rows, cfg, base, out, version, device):
    import torch
    from emotion_bundle import read_json
    from emotion_predictor import EnglishEmotionPredictor, VietnameseSixEmotionPredictor
    from evaluate_bilingual import score

    if device == "cuda" and not torch.cuda.is_available():
        raise ValueError("CUDA was explicitly requested but is unavailable.")
    language = "vi" if cfg["taskType"] == "emotion6_vi" else "en"
    manifest = read_json(base / "manifest.json")
    predictor = (VietnameseSixEmotionPredictor if language == "vi" else EnglishEmotionPredictor)(base, manifest["model_version"])
    labels = predictor.labels
    torch.manual_seed(cfg["seed"])
    random.seed(cfg["seed"])
    ids = {split: [i for i, r in enumerate(rows) if r["split"] == split] for split in ("train", "validation", "test")}
    if not all(ids.values()) or any(r.get("emotion") not in labels for r in rows):
        raise ValueError("Six-class training requires reviewed labels and all immutable splits.")
    texts = [predictor.preprocess(r["text"]) for r in rows]
    encoded = predictor.tokenizer(texts, padding="max_length", truncation=True,
                                  max_length=predictor.max_length, return_tensors="pt")
    targets = torch.tensor([labels.index(r["emotion"]) for r in rows], dtype=torch.long)
    model = predictor.model.to(device)
    optimizer = torch.optim.AdamW(model.parameters(), lr=2e-5)
    loss_function = torch.nn.CrossEntropyLoss()
    batch, accumulation = cfg["batchSize"], cfg["gradientAccumulation"]
    if not 1 <= cfg["epochs"] <= 10 or batch < 1 or accumulation < 1:
        raise ValueError("Use bounded explicit NLP training settings.")
    best_loss, best_weights = math.inf, None

    def forward(indices):
        return model(**{k: v[indices].to(device) for k, v in encoded.items()}).logits

    for epoch in range(cfg["epochs"]):
        order = ids["train"][:]
        random.shuffle(order)
        model.train()
        optimizer.zero_grad()
        for step, offset in enumerate(range(0, len(order), batch)):
            selected = order[offset:offset + batch]
            loss = loss_function(forward(selected), targets[selected].to(device)) / accumulation
            loss.backward()
            if (step + 1) % accumulation == 0 or offset + batch >= len(order):
                torch.nn.utils.clip_grad_norm_(model.parameters(), 1.0)
                optimizer.step()
                optimizer.zero_grad()
        model.eval()
        losses = []
        with torch.inference_mode():
            for offset in range(0, len(ids["validation"]), batch):
                selected = ids["validation"][offset:offset + batch]
                losses.append(float(loss_function(forward(selected), targets[selected].to(device))) * len(selected))
        validation_loss = sum(losses) / len(ids["validation"])
        if validation_loss < best_loss:
            best_loss = validation_loss
            best_weights = {k: v.detach().cpu().clone() for k, v in model.state_dict().items()}
        print(json.dumps({"epoch": epoch + 1, "validationLoss": validation_loss}), flush=True)
    model.load_state_dict(best_weights)
    model.eval()
    predictions = []
    with torch.inference_mode():
        for i in ids["test"]:
            probabilities = forward([i]).float().softmax(-1)[0].cpu().tolist()
            predictions.append({"id": rows[i]["id"], "language": language, "modelVersion": version,
                                "mappingVersion": "exported-six-class-anchors", "accepted": False,
                                "emotion": labels[max(range(len(labels)), key=probabilities.__getitem__)]})
    gold = [{**r, "language": language} for r in rows]
    evaluation = score(gold, predictions, taxonomy="moodify-six-v1")["groups"][0]["classification"]
    (out / "evaluation.json").write_text(json.dumps(evaluation, indent=2), encoding="utf-8")
    (out / "test-predictions.jsonl").write_text("\n".join(json.dumps(r, ensure_ascii=False) for r in predictions), encoding="utf-8")
    model_dir = out / ("model" if language == "vi" else "best_model")
    model.save_pretrained(model_dir, safe_serialization=True)
    predictor.tokenizer.save_pretrained(model_dir)
    from emotion_bundle import ASSETS
    base_model = base / ("model" if language == "vi" else "best_model")
    for name in ASSETS - {"config.json"}:
        if not (model_dir / name).exists() and (base_model / name).is_file():
            (model_dir / name).write_bytes((base_model / name).read_bytes())
    (out / "label_config.json").write_text(json.dumps(read_json(base / "label_config.json"), ensure_ascii=False, indent=2), encoding="utf-8")
    if language == "vi":
        (out / "preprocessing.json").write_bytes((base / "preprocessing.json").read_bytes())
    weights = model_dir / "model.safetensors"
    with weights.open("rb") as stream:
        digest = hashlib.file_digest(stream, "sha256").hexdigest()
    exported = {"model_version": version, "language": language, "task": "6-class emotion classification; V/A derived from anchors",
                "base_model": manifest.get("base_model"), "weights_file": weights.relative_to(out).as_posix(),
                "weights_sha256": digest, "confidence_threshold": None,
                "confidence_threshold_status": "New weights require separate reviewed calibration; accepted remains null.",
                "training_seed": cfg["seed"], "base_version": manifest["model_version"]}
    (out / "manifest.json").write_text(json.dumps(exported, ensure_ascii=False, indent=2), encoding="utf-8")
    return weights, {"macroF1": evaluation["macroF1"], "accuracy": evaluation["accuracy"], "coverage": 0.0}
