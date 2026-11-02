"""Validated six-emotion packages; legacy predictor fingerprints stay unchanged."""
from dataclasses import dataclass
import hashlib
import json
import math
import os
from pathlib import Path
import re

LABELS = ("Sadness", "Enjoyment", "Love", "Anger", "Fear", "Surprise")
TAXONOMY = "moodify-six-v1"
MODEL_TYPE = "emotion_classification_va_adapter"
ASSETS = {"config.json", "tokenizer.json", "tokenizer_config.json", "special_tokens_map.json",
          "added_tokens.json", "vocab.json", "vocab.txt", "merges.txt", "bpe.codes", "sentencepiece.bpe.model"}


def sha256(path):
    with Path(path).open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def read_json(path):
    value = json.loads(Path(path).read_text(encoding="utf-8-sig"))
    if not isinstance(value, dict):
        raise ValueError("Model metadata must be a JSON object.")
    return value


def threshold_value(value):
    if value is None or value == "":
        return None
    if isinstance(value, bool):
        raise ValueError("A confidence threshold must be numeric.")
    number = float(value)
    if not math.isfinite(number) or not 0 <= number <= 1:
        raise ValueError("A confidence threshold must be finite and in [0, 1].")
    return number


@dataclass(frozen=True)
class EmotionBundle:
    root: Path
    model_dir: Path
    weights: Path
    version: str
    language: str
    labels: tuple
    anchors: dict
    max_length: int
    segmentation: bool
    emoji: dict
    teencode: dict
    threshold: float | None
    weight_hash: str | None


def inspect_bundle(root, language, version, *, verify_weights=True):
    raw_root = Path(root).expanduser()
    root = raw_root.resolve()
    if raw_root.is_symlink() or language not in ("en", "vi") or not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9_-]{2,79}", version):
        raise ValueError("Invalid classifier candidate identity.")
    model_dir = root / ("best_model" if language == "en" else "model")
    if model_dir.is_symlink() or not model_dir.is_dir():
        raise ValueError("Classifier model folder is missing or linked.")
    config = read_json(model_dir / "config.json")
    labels_config = read_json(root / "label_config.json")
    manifest = read_json(root / "manifest.json")
    labels = labels_config.get("labels")
    if not isinstance(labels, list) or len(labels) != 6 or set(labels) != set(LABELS):
        raise ValueError("A six-emotion bundle must declare its exact six-label taxonomy.")
    actual = [config.get("id2label", {}).get(str(i)) for i in range(6)]
    exported = [labels_config.get("id2label", {}).get(str(i)) for i in range(6)]
    if actual != labels or exported != labels or config.get("num_labels", 6) != 6:
        raise ValueError("Classifier label order differs from the exported configuration.")
    if config.get("problem_type") not in (None, "single_label_classification"):
        raise ValueError("Only single-label emotion classification is supported by this adapter.")
    anchors = labels_config.get("va_anchors")
    if not isinstance(anchors, dict) or set(anchors) != set(labels):
        raise ValueError("Every emotion must have exactly one exported V/A anchor.")
    for pair in anchors.values():
        if not isinstance(pair, list) or len(pair) != 2 or any(type(x) not in (int, float) or not math.isfinite(x) or not -1 <= x <= 1 for x in pair):
            raise ValueError("V/A anchors must be finite pairs in [-1, 1].")
    length = labels_config.get("max_len")
    if type(length) is not int or not 2 <= length <= int(config.get("max_position_embeddings", 514)) - 2:
        raise ValueError("Exported token length is incompatible with this model.")
    weights = next((model_dir / name for name in ("model.safetensors", "pytorch_model.bin") if (model_dir / name).is_file()), None)
    if weights is None or weights.is_symlink():
        raise ValueError("Classifier weights are missing or linked.")
    if (manifest.get("language") != language or manifest.get("model_version") != version
            or manifest.get("weights_file") != weights.relative_to(root).as_posix()
            or not re.fullmatch(r"[a-fA-F0-9]{64}", str(manifest.get("weights_sha256", "")))):
        raise ValueError("Classifier manifest language, version or weights path does not match.")
    required = ("tokenizer.json", "tokenizer_config.json") if language == "en" else ("vocab.txt", "bpe.codes", "tokenizer_config.json")
    for name in required:
        asset = model_dir / name
        if asset.is_symlink() or not asset.is_file():
            raise ValueError("Saved classifier tokenizer is incomplete.")
    for file in [root / "label_config.json", root / "manifest.json", *model_dir.iterdir()]:
        if file.is_symlink() or not file.resolve().is_relative_to(root):
            raise ValueError("Model packages cannot contain linked runtime assets.")
    emoji, teencode = {}, {}
    segmentation = labels_config.get("use_word_segmentation", language == "vi")
    if type(segmentation) is not bool:
        raise ValueError("Word segmentation must be an exported boolean setting.")
    if language == "vi":
        preprocessing = root / "preprocessing.json"
        if preprocessing.is_symlink():
            raise ValueError("Preprocessing must be stored inside the classifier bundle.")
        payload = read_json(preprocessing)
        emoji, teencode = payload.get("emoji_dict"), payload.get("teencode_dict")
        if any(not isinstance(d, dict) or any(not isinstance(k, str) or not k or not isinstance(v, str) for k, v in d.items()) for d in (emoji, teencode)):
            raise ValueError("Use the exported emoji and teencode dictionaries.")
    threshold = None
    threshold_file = root / "confidence_threshold.json"
    if threshold_file.is_file():
        if threshold_file.is_symlink():
            raise ValueError("Confidence configuration must be local to the bundle.")
        threshold = threshold_value(read_json(threshold_file).get("confidence_threshold"))
    override = threshold_value(os.environ.get(language.upper() + "_CONFIDENCE_THRESHOLD"))
    if override is not None:
        threshold = max(threshold, override) if threshold is not None else override
    weight_hash = sha256(weights) if verify_weights else None
    if verify_weights and weight_hash != manifest["weights_sha256"].lower():
        raise ValueError("Weights do not match manifest SHA-256; download Git LFS assets if this is a fresh clone.")
    return EmotionBundle(root, model_dir, weights, version, language, tuple(labels), anchors, length,
                         segmentation, emoji, teencode, threshold, weight_hash)


def fingerprint_bundle(root, language, version):
    bundle = inspect_bundle(root, language, version)
    files = [f for f in bundle.model_dir.iterdir() if f.is_file() and f.name in ASSETS]
    files += [bundle.weights, bundle.root / "label_config.json", bundle.root / "manifest.json"]
    files += [bundle.root / name for name in ("preprocessing.json", "confidence_threshold.json") if (bundle.root / name).is_file()]
    manifest = []
    for path in sorted(files):
        if path.is_symlink() or not path.resolve().is_relative_to(bundle.root):
            raise ValueError("Fingerprint cannot follow linked model files.")
        manifest.append({"path": path.relative_to(bundle.root).as_posix(),
                         "sha256": bundle.weight_hash if path == bundle.weights else sha256(path)})
    source_root = Path(__file__).parent
    code = {name: hashlib.sha256((source_root / name).read_text(encoding="utf-8-sig").encode()).hexdigest()
            for name in ("emotion_bundle.py", "emotion_predictor.py")}
    material = {"schemaVersion": 2, "kind": "va" if language == "en" else "vi", "version": version,
                "files": manifest, "runtimeCode": code, "taxonomy": TAXONOMY,
                "confidenceThreshold": bundle.threshold}
    return {**material, "weightSha256": bundle.weight_hash,
            "bundleSha256": hashlib.sha256(json.dumps(material, sort_keys=True, separators=(",", ":")).encode()).hexdigest()}


def describe_bundle(root, language, version):
    bundle = inspect_bundle(root, language, version, verify_weights=False)
    return {"model_type": MODEL_TYPE, "taxonomy": TAXONOMY, "labels": list(bundle.labels),
            "calibration_status": "uncalibrated" if bundle.threshold is None else "threshold_configured"}
