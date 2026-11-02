"""Offline evaluation of reviewed bilingual labels against exported predictions.

This tool does not label examples, change thresholds, train or activate models.
JSONL labels: id, text, language, split, reviewed=true, optional emotion / valence+arousal / moodId.
JSONL predictions: id, language, modelVersion, mappingVersion, accepted, emotion, valence, arousal, moodId.
"""
import argparse
from collections import Counter
import hashlib
import json
import math
from pathlib import Path
import statistics
import unicodedata

LABELS = ("Anger", "Disgust", "Enjoyment", "Fear", "Other", "Sadness", "Surprise")


def load_jsonl(path):
    return [json.loads(line) for line in Path(path).read_text(encoding="utf-8-sig").splitlines() if line.strip()]


def coordinate(value):
    return isinstance(value, (int, float)) and not isinstance(value, bool) and math.isfinite(value) and -1 <= value <= 1


def regression(gold, predicted):
    errors = [p - g for g, p in zip(gold, predicted)]
    correlation = None
    if len(gold) > 1 and statistics.pstdev(gold) > 0 and statistics.pstdev(predicted) > 0:
        correlation = statistics.correlation(gold, predicted)
    return {"count": len(errors), "mae": statistics.mean(abs(e) for e in errors),
            "rmse": math.sqrt(statistics.mean(e * e for e in errors)), "pearson": correlation}


def score(rows, predictions, split="test", taxonomy=None):
    if taxonomy not in (None, "vsmec-seven-v1", "moodify-six-v1"):
        raise ValueError("Unsupported evaluation taxonomy.")
    labels = ("Sadness", "Enjoyment", "Love", "Anger", "Fear", "Surprise") if taxonomy == "moodify-six-v1" else LABELS
    if split not in ("validation", "test"):
        raise ValueError("Use validation for selection or test for independent reporting.")
    seen_ids, seen_text = set(), set()
    for row in rows:
        if not isinstance(row.get("id"), str) or not row["id"] or row["id"] in seen_ids:
            raise ValueError("Dataset IDs must be nonempty and unique.")
        seen_ids.add(row["id"])
        if row.get("language") not in ("vi", "en") or row.get("split") not in ("train", "validation", "test"):
            raise ValueError("Declare language and split for every row.")
        text = row.get("text")
        if not isinstance(text, str) or not text.strip():
            raise ValueError("Each row requires nonempty text.")
        key = " ".join(unicodedata.normalize("NFKC", text).casefold().split())
        if key in seen_text:
            raise ValueError("Duplicate normalized text, potentially across splits.")
        seen_text.add(key)
    selected = [r for r in rows if r["split"] == split]
    if not selected or any(r.get("reviewed") is not True for r in selected):
        raise ValueError("Every selected row must have independently reviewed labels; templates are not evaluation data.")
    by_id = {}
    for prediction in predictions:
        if prediction.get("id") in by_id:
            raise ValueError("Duplicate prediction ID.")
        by_id[prediction.get("id")] = prediction
    groups = {}
    for row in selected:
        p = by_id.get(row["id"])
        if not p or p.get("language") != row["language"] or type(p.get("accepted")) is not bool:
            raise ValueError("Missing prediction, language mismatch or invalid acceptance.")
        if not isinstance(p.get("modelVersion"), str) or not p["modelVersion"] or not isinstance(p.get("mappingVersion"), str) or not p["mappingVersion"]:
            raise ValueError("Predictions must identify model and mapping versions.")
        key = (row["language"], p["modelVersion"], p["mappingVersion"])
        groups.setdefault(key, []).append((row, p))
    result = []
    for (language, model, mapping), pairs in groups.items():
        report = {"language": language, "modelVersion": model, "mappingVersion": mapping, "samples": len(pairs)}
        labelled = [(r, p) for r, p in pairs if r.get("emotion") is not None]
        if labelled:
            if (language != "vi" and taxonomy != "moodify-six-v1") or any(r["emotion"] not in labels or p.get("emotion") not in labels for r, p in labelled):
                raise ValueError("Emotion labels must match the declared evaluation taxonomy.")
            confusion = Counter((r["emotion"], p["emotion"]) for r, p in labelled)
            f1 = []
            for label in labels:
                tp = confusion[(label, label)]
                fp = sum(v for (g, predicted), v in confusion.items() if g != label and predicted == label)
                fn = sum(v for (g, predicted), v in confusion.items() if g == label and predicted != label)
                f1.append(2 * tp / (2 * tp + fp + fn) if 2 * tp + fp + fn else 0.0)
            accepted = [(r, p) for r, p in labelled if p["accepted"]]
            report["classification"] = {
                "labels": labels, "macroF1": statistics.mean(f1),
                ("macroF1AllSixLabels" if taxonomy == "moodify-six-v1" else "macroF1AllSevenLabels"): statistics.mean(f1),
                "accuracy": sum(r["emotion"] == p["emotion"] for r, p in labelled) / len(labelled),
                "support": {label: sum(r["emotion"] == label for r, _ in labelled) for label in labels},
                "confusionMatrix": [[confusion[(g, p)] for p in labels] for g in labels],
                "coverage": len(accepted) / len(labelled),
                "selectiveAccuracy": sum(r["emotion"] == p["emotion"] for r, p in accepted) / len(accepted) if accepted else None,
            }
        va = [(r, p) for r, p in pairs if r.get("valence") is not None or r.get("arousal") is not None]
        if va:
            if any(not coordinate(x.get(axis)) for r, p in va for x in (r, p) for axis in ("valence", "arousal")):
                raise ValueError("Gold and predicted VA must be finite coordinates in [-1,1].")
            report["va"] = {axis: regression([r[axis] for r, _ in va], [p[axis] for _, p in va]) for axis in ("valence", "arousal")}
            regions = {}
            for r, p in va:
                region = ("positive" if r["valence"] > 0 else "negative" if r["valence"] < 0 else "neutral") + "_" + ("high" if r["arousal"] > 0 else "low" if r["arousal"] < 0 else "neutral")
                regions.setdefault(region, []).append((r, p))
            report["vaByGoldRegion"] = {region: {axis: regression([r[axis] for r, _ in data], [p[axis] for _, p in data]) for axis in ("valence", "arousal")} for region, data in regions.items()}
        mood = [(r, p) for r, p in pairs if r.get("moodId") is not None]
        if mood:
            if any(type(r["moodId"]) is not int or r["moodId"] <= 0 for r, _ in mood):
                raise ValueError("Gold mood IDs must be positive integers.")
            report["mood"] = {"count": len(mood), "exactMatchIncludingAbstentions": sum(r["moodId"] == p.get("moodId") for r, p in mood) / len(mood)}
        if not labelled and not va and not mood:
            raise ValueError("Reviewed rows require at least one applicable gold label.")
        result.append(report)
    return {"split": split, "leakageCheckScope": "Normalized text in provided rows only; training independence must be documented separately.",
            "qualityClaim": "Metrics for supplied human-reviewed labels, not proof of generalization or deployment approval.", "groups": result}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--data", required=True)
    parser.add_argument("--predictions", required=True)
    parser.add_argument("--split", choices=("validation", "test"), default="test")
    parser.add_argument("--taxonomy", choices=("vsmec-seven-v1", "moodify-six-v1"))
    parser.add_argument("--out", required=True)
    args = parser.parse_args()
    report = score(load_jsonl(args.data), load_jsonl(args.predictions), args.split, taxonomy=args.taxonomy)
    report["datasetSha256"] = hashlib.sha256(Path(args.data).read_bytes()).hexdigest()
    report["predictionsSha256"] = hashlib.sha256(Path(args.predictions).read_bytes()).hexdigest()
    output = Path(args.out)
    if output.exists():
        raise ValueError("Choose a new output filename; existing evaluation reports are immutable.")
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print("Evaluation written; no model, threshold or serving state changed.")


if __name__ == "__main__":
    main()
