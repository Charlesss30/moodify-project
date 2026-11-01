"""Execute a reviewed typed training bundle locally/Colab; never activates serving."""
import argparse
import hashlib
import json
from pathlib import Path
import random


def digest(text):
    return hashlib.sha256(text.encode("utf-8")).hexdigest()


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--bundle", required=True)
    p.add_argument("--base-dir")
    p.add_argument("--out", required=True)
    p.add_argument("--device", choices=("cpu", "cuda"), default="cpu")
    args = p.parse_args()
    bundle = json.loads(Path(args.bundle).read_text(encoding="utf-8-sig"))
    for field, checksum in (("datasetJson", "datasetHash"), ("configJson", "configHash")):
        if digest(bundle[field]).lower() != bundle[checksum].lower():
            raise ValueError("Bundle integrity failed: " + field)
    rows = json.loads(bundle["datasetJson"])
    cfg = json.loads(bundle["configJson"])
    if any(r.get("reviewed") is not True for r in rows):
        raise ValueError("Every supplied row must be reviewed; the runner does not generate gold labels.")
    out = Path(args.out)
    out.mkdir(parents=True, exist_ok=False)
    if cfg.get("purpose") == "evaluation":
        from evaluate_candidate import evaluate_existing
        report = evaluate_existing(bundle, rows, cfg, Path(args.base_dir or ""), out, args.device)
        (out / "report.json").write_text(json.dumps(report,ensure_ascii=False,allow_nan=False,indent=2),encoding="utf-8")
        print("Existing artifact evaluated; no weights, thresholds or serving state changed.")
        return
    if cfg["taskType"] == "recommender":
        from matrix_factorization import train
        data = [{k: r[k] for k in ("id", "userId", "contentId", "stars", "timestamp")} for r in rows]
        validation_start = min(r["timestamp"] for r in rows if r["split"] == "validation")
        test_start = min(r["timestamp"] for r in rows if r["split"] == "test")
        model = train(data, validation_start, test_start, bundle["modelVersion"], factors=cfg["factors"],
                      epochs=cfg["epochs"], seed=cfg["seed"])
        model["datasetHash"] = bundle["datasetHash"]
        artifact = out / "matrix-factorization.json"
        artifact.write_text(json.dumps(model, ensure_ascii=False, allow_nan=False), encoding="utf-8")
        metrics = {"rmse": model["report"]["test"]["rmse"], "ndcg": model["report"]["ranking"]["ndcg"] or 0.0}
    else:
        artifact, metrics = train_nlp(rows, cfg, Path(args.base_dir or ""), out, bundle["modelVersion"], args.device)
    report = {key: bundle[key] for key in ("jobId", "modelVersion", "datasetHash", "configHash")}
    with artifact.open("rb") as stream:
        report["artifactHash"] = hashlib.file_digest(stream, "sha256").hexdigest()
    report["testSamples"] = sum(r["split"] == "test" for r in rows)
    report["artifactFiles"] = []
    for path in sorted(out.rglob("*")):
        if path.is_file():
            with path.open("rb") as stream:
                report["artifactFiles"].append({"path": path.relative_to(out).as_posix(),
                    "sha256": hashlib.file_digest(stream, "sha256").hexdigest()})
    report["metrics"] = metrics
    report["status"] = "trained_candidate_not_deployed"
    (out / "report.json").write_text(json.dumps(report, ensure_ascii=False, allow_nan=False, indent=2), encoding="utf-8")
    print("Candidate and report written. Review independent metrics before any model upload/activation.")


def train_nlp(rows, cfg, base, out, version, device):
    import math
    import torch
    from transformers import AutoModelForSequenceClassification, AutoTokenizer
    from evaluate_bilingual import score
    if not base.is_dir() or str(base) == ".":
        raise ValueError("Provide the explicit local base model folder; no model is downloaded automatically.")
    torch.manual_seed(cfg["seed"])
    random.seed(cfg["seed"])
    if device == "cuda" and not torch.cuda.is_available():
        raise ValueError("CUDA was explicitly requested but unavailable.")
    classification = cfg["taskType"] == "vsmec7"
    labels = ["Anger", "Disgust", "Enjoyment", "Fear", "Other", "Sadness", "Surprise"]
    max_length = 128
    if classification:
        from pyvi import ViTokenizer
        from vietnamese_preprocessing import DEFAULT_EMOJI, VERSION, normalize, validate
        preprocessing_file = base / "preprocessing.json"
        preprocessing = json.loads(preprocessing_file.read_text(encoding="utf-8")) if preprocessing_file.is_file() else {
            "emoji_dict": DEFAULT_EMOJI, "teencode_dict": {}}
        validate(preprocessing)
        preprocessing["normalization_version"] = VERSION
        texts = [normalize(r["text"], preprocessing.get("emoji_dict", DEFAULT_EMOJI), preprocessing.get("teencode_dict", {}), ViTokenizer) for r in rows]
        tokenizer = AutoTokenizer.from_pretrained(base, local_files_only=True, trust_remote_code=False)
        model = AutoModelForSequenceClassification.from_pretrained(base, local_files_only=True, trust_remote_code=False)
        actual = [model.config.id2label.get(i) for i in range(model.config.num_labels)]
        if actual != labels:
            raise ValueError("Base model is not the supported seven-label VSMEC classifier.")
        max_length = min(128, model.config.max_position_embeddings - 2)
        targets = torch.tensor([labels.index(r["emotion"]) for r in rows], dtype=torch.long)
    else:
        from va_predictor import RobertaForVARegression
        from transformers import RobertaTokenizerFast
        checkpoints = list(base.glob("*.pt"))
        if len(checkpoints) != 1:
            raise ValueError("VA base must contain one checkpoint and its tokenizer directory.")
        tokenizer = RobertaTokenizerFast.from_pretrained(base / "tokenizer", local_files_only=True)
        model = RobertaForVARegression()
        model.load_state_dict(torch.load(checkpoints[0], map_location="cpu", weights_only=True), strict=True)
        texts = [r["text"] for r in rows]
        targets = torch.tensor([[r["valence"], r["arousal"]] for r in rows], dtype=torch.float32)
    encoded = tokenizer(texts, padding="max_length", truncation=True, max_length=max_length, return_tensors="pt")
    train_ids = [i for i, r in enumerate(rows) if r["split"] == "train"]
    validation_ids = [i for i, r in enumerate(rows) if r["split"] == "validation"]
    test_ids = [i for i, r in enumerate(rows) if r["split"] == "test"]
    if not train_ids or not validation_ids or not test_ids:
        raise ValueError("Every immutable split must be represented.")
    model.to(device)
    optimizer = torch.optim.AdamW(model.parameters(), lr=2e-5)
    loss_function = torch.nn.CrossEntropyLoss() if classification else torch.nn.MSELoss()
    batch_size = cfg["batchSize"]
    accumulation = cfg["gradientAccumulation"]
    best_loss, best_weights = math.inf, None

    def forward(ids):
        batch = {key: value[ids].to(device) for key, value in encoded.items()}
        if classification:
            return model(**batch).logits
        return model(batch["input_ids"], batch["attention_mask"])

    for epoch in range(cfg["epochs"]):
        order = train_ids[:]
        random.shuffle(order)
        model.train()
        optimizer.zero_grad()
        for step, offset in enumerate(range(0, len(order), batch_size)):
            ids = order[offset:offset + batch_size]
            loss = loss_function(forward(ids), targets[ids].to(device)) / accumulation
            loss.backward()
            if (step + 1) % accumulation == 0 or offset + batch_size >= len(order):
                torch.nn.utils.clip_grad_norm_(model.parameters(), 1.0)
                optimizer.step()
                optimizer.zero_grad()
        model.eval()
        losses = []
        with torch.inference_mode():
            for offset in range(0, len(validation_ids), batch_size):
                ids = validation_ids[offset:offset + batch_size]
                losses.append(float(loss_function(forward(ids), targets[ids].to(device))) * len(ids))
        validation_loss = sum(losses) / len(validation_ids)
        if validation_loss < best_loss:
            best_loss = validation_loss
            best_weights = {key: tensor.detach().cpu().clone() for key, tensor in model.state_dict().items()}
        print(json.dumps({"epoch": epoch + 1, "validationLoss": validation_loss}), flush=True)
    model.load_state_dict(best_weights)
    model.eval()
    if classification:
        with torch.inference_mode():
            values = [forward([i]).float().softmax(-1)[0].cpu().tolist() for i in validation_ids]
        candidates = sorted({max(v) for v in values})
        eligible = []
        for threshold in candidates:
            accepted = [(r, v) for r, v in zip(validation_ids, values) if max(v) >= threshold]
            accuracy = sum(labels[v.index(max(v))] == rows[r]["emotion"] for r, v in accepted) / len(accepted)
            if len(accepted) >= 20 and accuracy >= .8:
                eligible.append((len(accepted), threshold))
        enabled = bool(eligible)
        threshold = max(eligible)[1] if enabled else 1.0
        predictions = []
        with torch.inference_mode():
            for i in test_ids:
                probabilities = forward([i]).float().softmax(-1)[0].cpu().tolist()
                predictions.append({"id": rows[i]["id"], "language": "vi", "modelVersion": version, "mappingVersion": "vsmec-anchors-v1-nearest-center-v1",
                    "emotion": labels[probabilities.index(max(probabilities))], "accepted": enabled and max(probabilities) >= threshold})
        gold = [{**r, "language": "vi"} for r in rows]
        report = score(gold, predictions)["groups"][0]["classification"]
        metrics = {"macroF1": report["macroF1AllSevenLabels"], "coverage": report["coverage"]}
        (out / "evaluation.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
        (out / "test-predictions.jsonl").write_text("\n".join(json.dumps(r) for r in predictions), encoding="utf-8")
        target = out / "model"
        model.save_pretrained(target, safe_serialization=True)
        tokenizer.save_pretrained(target)
        (target / "preprocessing.json").write_text(json.dumps(preprocessing, ensure_ascii=False), encoding="utf-8")
        (target / "confidence_threshold.json").write_text(json.dumps({"confidence_threshold": threshold, "enabled": enabled,
            "method": "validation_only_target_accuracy_0.8_min_20_accepted", "quality": "requires_independent_review"}), encoding="utf-8")
        return target / "model.safetensors", metrics
    predictions = []
    with torch.inference_mode():
        for i in test_ids:
            va = forward([i])[0].float().clamp(-1, 1).cpu().tolist()
            predictions.append({"id": rows[i]["id"], "language": "en", "modelVersion": version, "mappingVersion": "regression-nearest-center-v1",
                                "accepted": True, "valence": va[0], "arousal": va[1]})
    gold = [{**r, "language": "en"} for r in rows]
    va_metrics = score(gold, predictions)["groups"][0]["va"]
    (out / "evaluation.json").write_text(json.dumps(va_metrics, indent=2), encoding="utf-8")
    (out / "test-predictions.jsonl").write_text("\n".join(json.dumps(r) for r in predictions), encoding="utf-8")
    metrics = {axis + suffix: va_metrics[axis][metric] for axis in ("valence", "arousal") for suffix, metric in (("Mae", "mae"), ("Rmse", "rmse"))}
    tokenizer.save_pretrained(out / "tokenizer")
    artifact = out / (version + ".pt")
    torch.save(model.state_dict(), artifact)
    return artifact, metrics


if __name__ == "__main__":
    main()
