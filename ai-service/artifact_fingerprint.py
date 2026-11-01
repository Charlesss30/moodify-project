"""Fingerprint exactly the stored files used by a bilingual serving adapter."""
import hashlib
import json
from pathlib import Path

_METADATA = {"config.json","tokenizer_config.json","tokenizer.json","special_tokens_map.json","added_tokens.json",
             "vocab.txt","vocab.json","merges.txt","bpe.codes","sentencepiece.bpe.model",
             "preprocessing.json","confidence_threshold.json"}


def sha(path):
    with Path(path).open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def fingerprint_files(kind, root, version, checkpoint=None):
    root = Path(root).resolve()
    if kind not in ("vi", "va"):
        raise ValueError("Unknown model pipeline.")
    if kind == "vi":
        weight = next((root/name for name in ("model.safetensors","pytorch_model.bin","model.pt") if (root/name).is_file()), None)
        files = [p for p in root.iterdir() if p.is_file() and p.name in _METADATA]
        sources = ("vietnamese_predictor.py","vietnamese_preprocessing.py","artifact_fingerprint.py")
    else:
        weight = Path(checkpoint).resolve() if checkpoint else root/(version+".pt")
        files = [p for p in (root/"tokenizer").rglob("*") if p.is_file() and p.name in _METADATA]
        sources = ("va_predictor.py","artifact_fingerprint.py")
    if weight is None or not weight.is_file():
        raise ValueError("Serving weights are missing.")
    files.append(weight)
    manifest = []
    for path in sorted(files):
        if path.is_symlink() or not path.resolve().is_relative_to(root):
            raise ValueError("Fingerprint cannot follow model file links.")
        manifest.append({"path":path.relative_to(root).as_posix(),"sha256":sha(path)})
    source_root = Path(__file__).parent
    code = {name:hashlib.sha256((source_root/name).read_text(encoding="utf-8-sig").encode("utf-8")).hexdigest()
            for name in sources}
    material = {"schemaVersion":1,"kind":kind,"version":version,"files":manifest,"runtimeCode":code}
    return {**material,"weightSha256":sha(weight),
            "bundleSha256":hashlib.sha256(json.dumps(material,sort_keys=True,separators=(",",":")).encode()).hexdigest()}