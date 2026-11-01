import logging
import os
import re
from contextlib import asynccontextmanager
from pathlib import Path
from threading import BoundedSemaphore

from fastapi import FastAPI, HTTPException
from pydantic import BaseModel, Field


logger = logging.getLogger("moodify.ai")
_predict_lock = BoundedSemaphore(1)
_vi_predict_lock = BoundedSemaphore(1)
_default_model_dir = (
    Path(__file__).resolve().parents[1]
    / "code-train"
    / "train-KLCN"
    / "output"
    / "roberta_large_asl_en_01"
    / "best_model"
)
_default_va_dir = (
    Path(__file__).resolve().parents[1]
    / "code-train"
    / "train-KLCN"
    / "output"
    / "roberta_va_regression"
)
_model_version_pattern = re.compile(r"^[A-Za-z0-9][A-Za-z0-9_-]{2,79}$")


def va_models_root() -> Path:
    return Path(os.environ.get("VA_MODELS_ROOT", str(_default_va_dir.parent))).expanduser().resolve()


def va_candidate(version: str) -> tuple[Path, Path]:
    if not _model_version_pattern.fullmatch(version):
        raise ValueError("Invalid model version.")
    root = va_models_root()
    folder = root / version
    if folder.is_symlink() or not folder.is_dir() or folder.resolve().parent != root:
        raise FileNotFoundError("Model version is not an available local candidate.")
    checkpoint = folder / f"{version}.pt"
    if not checkpoint.is_file() or checkpoint.is_symlink():
        checkpoint = folder / "model.pt"
    tokenizer = folder / "tokenizer"
    tokenizer_file = tokenizer / "tokenizer.json"
    if (not checkpoint.is_file() or checkpoint.is_symlink() or tokenizer.is_symlink()
            or not tokenizer_file.is_file() or tokenizer_file.is_symlink()):
        raise FileNotFoundError("Candidate must contain a PyTorch checkpoint and tokenizer.")
    return checkpoint, tokenizer


def va_candidates() -> list[dict]:
    root = va_models_root()
    candidates = []
    if not root.is_dir():
        return candidates
    for folder in sorted(root.iterdir(), key=lambda item: item.name.casefold()):
        if not _model_version_pattern.fullmatch(folder.name) or folder.is_symlink() or not folder.is_dir():
            continue
        try:
            checkpoint, tokenizer = va_candidate(folder.name)
        except (ValueError, FileNotFoundError):
            continue
        candidates.append({"model_version": folder.name, "checkpoint": checkpoint.name,
                           "tokenizer_available": (tokenizer / "tokenizer.json").is_file()})
    return candidates


def active_va_version() -> str | None:
    state = lifecycle_state("va")
    if state is not None:
        version = state.get("active_version")
        if version is None:
            return None
        va_candidate(version)
        return version
    marker = Path(os.environ.get("VA_ACTIVE_MODEL_FILE", str(va_models_root() / ".active-va-model")))
    try:
        version = marker.read_text(encoding="utf-8").strip()
        va_candidate(version)
        return version
    except (OSError, ValueError, FileNotFoundError):
        return None


def vi_models_root() -> Path:
    return Path(os.environ.get("VI_MODELS_ROOT", str(va_models_root().parent / "vietnamese-emotion"))).expanduser().resolve()


def vi_candidate(version: str) -> Path:
    if not _model_version_pattern.fullmatch(version):
        raise ValueError("Invalid Vietnamese model version.")
    root = vi_models_root()
    folder = root / version
    model_dir = folder / "model"
    if folder.is_symlink() or not folder.is_dir() or folder.resolve().parent != root:
        raise FileNotFoundError("Vietnamese model version is not available.")
    if model_dir.is_symlink() or not (model_dir / "config.json").is_file() or not any(
        (model_dir / item).is_file() for item in ("model.safetensors", "pytorch_model.bin", "model.pt")
    ):
        raise FileNotFoundError("Vietnamese candidate requires model/config artifacts.")
    return model_dir


def vi_candidates() -> list[dict]:
    root = vi_models_root()
    if not root.is_dir():
        return []
    items = []
    for folder in sorted(root.iterdir(), key=lambda item: item.name.casefold()):
        if not _model_version_pattern.fullmatch(folder.name) or folder.is_symlink() or not folder.is_dir():
            continue
        try:
            model_dir = vi_candidate(folder.name)
        except (ValueError, FileNotFoundError):
            continue
        weight = next((name for name in ("model.safetensors", "pytorch_model.bin", "model.pt") if (model_dir / name).is_file()), "")
        items.append({"model_version": folder.name, "checkpoint": weight, "tokenizer_available": (model_dir / "tokenizer_config.json").is_file()})
    return items


def active_vi_version() -> str | None:
    state = lifecycle_state("vi")
    if state is not None:
        version = state.get("active_version")
        if version is None:
            return None
        vi_candidate(version)
        return version
    marker = Path(os.environ.get("VI_ACTIVE_MODEL_FILE", str(vi_models_root() / ".active-model")))
    try:
        version = marker.read_text(encoding="utf-8").strip()
        vi_candidate(version)
        return version
    except (OSError, ValueError, FileNotFoundError):
        return None


@asynccontextmanager
async def lifespan(app: FastAPI):
    app.state.predictor = None
    app.state.load_error = None
    app.state.va_predictor = None
    app.state.va_load_error = None
    app.state.va_active_version = None
    app.state.vi_predictor = None
    app.state.vi_active_version = None
    app.state.vi_load_error = None
    if os.environ.get("AI_MODEL_ENABLED", "").lower() == "true":
        try:
            from predictor import EmotionPredictor

            model_dir = Path(os.environ.get("MODEL_DIR", str(_default_model_dir)))
            expected_hash = os.environ.get("MODEL_SHA256", "").strip()
            if not expected_hash:
                raise ValueError("MODEL_SHA256 must be configured before loading the model.")
            app.state.predictor = EmotionPredictor(model_dir, expected_hash)
        except Exception as error:
            app.state.load_error = type(error).__name__
            logger.error("Model is not ready (%s).", type(error).__name__)
    if os.environ.get("VA_MODEL_ENABLED", "").lower() == "true" and not disabled_by_operator("va"):
        try:
            from va_predictor import ValenceArousalPredictor

            version = active_va_version() or Path(os.environ.get("VA_MODEL_DIR", str(_default_va_dir))).name
            checkpoint, tokenizer_dir = va_candidate(version)
            app.state.va_predictor = verify_loaded_identity("va", ValenceArousalPredictor(checkpoint, tokenizer_dir, version))
            app.state.va_active_version = version
        except Exception as error:
            app.state.va_load_error = type(error).__name__
            logger.error("Valence/arousal model is not ready (%s).", type(error).__name__)
    if os.environ.get("VI_MODEL_ENABLED", "true").lower() == "true":
        version = active_vi_version()
        if version:
            try:
                from vietnamese_predictor import VietnameseEmotionPredictor

                app.state.vi_predictor = verify_loaded_identity("vi", VietnameseEmotionPredictor(vi_candidate(version), version))
                app.state.vi_active_version = version
            except Exception as error:
                app.state.vi_load_error = type(error).__name__
                logger.exception("Vietnamese emotion model is not ready (%s).", type(error).__name__)
    yield


app = FastAPI(title="Moodify English Emotion Inference", lifespan=lifespan)


class PredictionRequest(BaseModel):
    text: str = Field(min_length=1, max_length=4000)
    language: str = "en"


class ActivateVARequest(BaseModel):
    version: str = Field(pattern=r"^[A-Za-z0-9][A-Za-z0-9_-]{2,79}$")
    expected_revision: int | None = Field(default=None, ge=0)
    expected_fingerprint: str | None = Field(default=None, pattern=r"^[a-fA-F0-9]{64}$")


@app.get("/health/live")
def live():
    return {"status": "live", "process_id": os.getpid()}


@app.get("/health/ready")
def ready():
    predictor = app.state.predictor
    if predictor is None:
        raise HTTPException(status_code=503, detail="Inference model is unavailable.")
    return {
        "status": "ready",
        "model_version": predictor.config["run_id"],
        "artifact_hash": predictor.artifact_hash,
        "config_hash": predictor.config_hash,
        "language": "en",
        "score_type": "uncalibrated_sigmoid_not_accuracy",
        "inference_device": predictor.device,
        "device_name": predictor.device_name,
    }


@app.post("/predict")
def predict(request: PredictionRequest):
    predictor = app.state.predictor
    if predictor is None:
        raise HTTPException(status_code=503, detail="Inference model is unavailable.")
    if request.language != "en":
        raise HTTPException(status_code=422, detail="This model supports English only.")
    if not request.text.strip():
        raise HTTPException(status_code=422, detail="Text must not be blank.")
    if not _predict_lock.acquire(blocking=False):
        raise HTTPException(status_code=429, detail="Inference capacity is busy.")
    try:
        try:
            return predictor.predict(request.text)
        except ValueError as error:
            raise HTTPException(status_code=422, detail=str(error)) from error
    finally:
        _predict_lock.release()


@app.get("/health/va/ready")
def va_ready():
    predictor = app.state.va_predictor
    if predictor is None:
        raise HTTPException(status_code=503, detail="Valence/arousal model is unavailable.")
    return {
        "status": "ready",
        "model_version": predictor.model_version,
        "language": "en",
        "inference_device": predictor.device,
    }


@app.get("/models/va")
def list_va_models():
    return {"items": va_candidates(), "active_version": app.state.va_active_version}


@app.post("/models/va/activate")
def activate_va_model(request: ActivateVARequest):
    return model_lifecycle("va", "activate", request.version, request.expected_revision, request.expected_fingerprint)


@app.post("/predict-va")
def predict_va(request: PredictionRequest):
    predictor = app.state.va_predictor
    if predictor is None:
        raise HTTPException(status_code=503, detail="Valence/arousal model is unavailable.")
    if request.language != "en":
        raise HTTPException(status_code=422, detail="This model supports English only.")
    if not request.text.strip():
        raise HTTPException(status_code=422, detail="Text must not be blank.")
    if not _predict_lock.acquire(blocking=False):
        raise HTTPException(status_code=429, detail="Inference capacity is busy.")
    try:
        try:
            return predictor.predict(request.text)
        except ValueError as error:
            raise HTTPException(status_code=422, detail=str(error)) from error
    finally:
        _predict_lock.release()


@app.get("/health/vi/ready")
def vi_ready():
    predictor = app.state.vi_predictor
    if predictor is None:
        raise HTTPException(status_code=503, detail="Vietnamese emotion model is unavailable.")
    return {"status": "ready", "model_version": predictor.model_version, "language": "vi", "inference_device": predictor.device,
            "preprocessing_status": getattr(predictor, "preprocessing_status", "unknown"),
            "preprocessing_sha256": getattr(predictor, "preprocessing_hash", None)}


@app.get("/models/vi")
def list_vi_models():
    return {"items": vi_candidates(), "active_version": app.state.vi_active_version}


@app.post("/models/vi/activate")
def activate_vi_model(request: ActivateVARequest):
    return model_lifecycle("vi", "activate", request.version, request.expected_revision, request.expected_fingerprint)


@app.post("/predict-vi")
def predict_vi(request: PredictionRequest):
    predictor = app.state.vi_predictor
    if predictor is None:
        raise HTTPException(status_code=503, detail="Vietnamese emotion model is unavailable.")
    if request.language != "vi":
        raise HTTPException(status_code=422, detail="This endpoint accepts Vietnamese text only.")
    if not _vi_predict_lock.acquire(blocking=False):
        raise HTTPException(status_code=429, detail="Vietnamese inference capacity is busy.")
    try:
        try:
            return predictor.predict(request.text)
        except ValueError as error:
            raise HTTPException(status_code=422, detail=str(error)) from error
    finally:
        _vi_predict_lock.release()


def lifecycle_path(kind):
    marker = Path(os.environ.get("VA_ACTIVE_MODEL_FILE", str(va_models_root() / ".active-va-model"))) if kind == "va" else Path(os.environ.get("VI_ACTIVE_MODEL_FILE", str(vi_models_root() / ".active-model")))
    return marker, marker.with_name(marker.name + ".lifecycle.json")


def lifecycle_state(kind):
    import json
    _, state_path = lifecycle_path(kind)
    try:
        state = json.loads(state_path.read_text(encoding="utf-8"))
        if not isinstance(state, dict) or type(state.get("revision")) is not int:
            raise ValueError("Invalid lifecycle state.")
        return state
    except FileNotFoundError:
        return None
    except (OSError, ValueError, TypeError):
        logger.error("Model lifecycle state is invalid for %s.", kind)
        return {"revision": 0, "active_version": None, "previous_version": None, "error": "invalid_state"}


def disabled_by_operator(kind):
    state = lifecycle_state(kind)
    return state is not None and state.get("active_version") is None


def persist_lifecycle(kind, version, previous, bundle=None, previous_bundle=None):
    import json
    marker, state_path = lifecycle_path(kind)
    state = lifecycle_state(kind) or {"revision": 0}
    payload = {"active_version": version, "previous_version": previous, "revision": state["revision"] + 1,
               "active_bundle_sha256": bundle, "previous_bundle_sha256": previous_bundle}
    state_path.parent.mkdir(parents=True, exist_ok=True)
    temporary = state_path.with_name(state_path.name + ".tmp")
    temporary.write_text(json.dumps(payload), encoding="utf-8")
    temporary.replace(state_path)
    # New code reads the atomic state first. Legacy marker is a compatibility mirror only.
    try:
        temporary = marker.with_name(marker.name + ".tmp")
        temporary.write_text((version or "none") + "\n", encoding="utf-8")
        temporary.replace(marker)
    except OSError:
        logger.warning("Lifecycle state committed; legacy marker mirror could not be refreshed.")
    return payload


def load_candidate(kind, version):
    if kind == "va":
        from va_predictor import ValenceArousalPredictor
        checkpoint, tokenizer = va_candidate(version)
        candidate = ValenceArousalPredictor(checkpoint, tokenizer, version)
        smoke = candidate.predict("I feel calm today.")
    else:
        from vietnamese_predictor import VietnameseEmotionPredictor
        candidate = VietnameseEmotionPredictor(vi_candidate(version), version)
        smoke = candidate.predict("Tôi đang cảm thấy vui.")
    import math
    if smoke.get("model_version") != version or smoke.get("language") != ("en" if kind == "va" else "vi") or any(
        type(smoke.get(key)) not in (int, float) or not math.isfinite(smoke[key]) or not -1 <= smoke[key] <= 1 for key in ("valence", "arousal")
    ):
        raise ValueError("Readiness output is outside the model contract.")
    return candidate


def model_lifecycle(kind, action, version=None, expected_revision=None, expected_fingerprint=None):
    lock = _predict_lock if kind == "va" else _vi_predict_lock
    if not lock.acquire(blocking=False):
        raise HTTPException(status_code=429, detail="Inference is busy; retry model control.")
    prefix = "va" if kind == "va" else "vi"
    try:
        state = lifecycle_state(kind) or {"revision": 0, "previous_version": None}
        if expected_revision is not None and state["revision"] != expected_revision:
            raise HTTPException(status_code=409, detail="Model lifecycle changed. Refresh before retrying.")
        if state.get("error"):
            raise HTTPException(status_code=409, detail="Stored lifecycle state needs operator repair; it will not be overwritten.")
        current = getattr(app.state, prefix + "_active_version", None)
        predictor = getattr(app.state, prefix + "_predictor", None)
        if action == "deactivate":
            payload = persist_lifecycle(kind, None, current or state.get("previous_version"), None,
                getattr(predictor,"artifact_fingerprint",{}).get("bundleSha256") or state.get("previous_bundle_sha256"))
            setattr(app.state, prefix + "_predictor", None)
            setattr(app.state, prefix + "_active_version", None)
            return {"status": "inactive", **payload}
        if action == "rollback":
            version = state.get("previous_version")
            expected_fingerprint = expected_fingerprint or state.get("previous_bundle_sha256")
            if not version:
                raise HTTPException(status_code=409, detail="No previous model artifact to restore.")
        if action == "activate" and current == version and predictor is not None:
            if expected_fingerprint and getattr(predictor,"artifact_fingerprint",{}).get("bundleSha256") != expected_fingerprint:
                raise HTTPException(status_code=409, detail="Loaded model identity differs from approved artifact; use a new immutable version.")
            return {"status": "ready", "active_version": version, "device": predictor.device, "revision": state["revision"]}
        try:
            candidate = load_candidate(kind, version)
            if expected_fingerprint and getattr(candidate,"artifact_fingerprint",{}).get("bundleSha256") != expected_fingerprint:
                raise HTTPException(status_code=409, detail="Artifact identity changed after approval; current model remains unchanged.")
            if action == "validate":
                device = candidate.device
                del candidate
                return {"status": "validated_readiness_only", "version": version, "device": device,
                        "revision": state["revision"], "quality_validated": False}
            payload = persist_lifecycle(kind, version, current or state.get("previous_version"),
                getattr(candidate,"artifact_fingerprint",{}).get("bundleSha256"),
                getattr(predictor,"artifact_fingerprint",{}).get("bundleSha256") or state.get("previous_bundle_sha256"))
            setattr(app.state, prefix + "_predictor", candidate)
            setattr(app.state, prefix + "_active_version", version)
            setattr(app.state, prefix + "_load_error", None)
            return {"status": "ready", "device": candidate.device, **payload}
        except HTTPException:
            raise
        except Exception as error:
            logger.exception("Candidate lifecycle failed for %s.", kind)
            raise HTTPException(status_code=422, detail="Candidate failed loading/readiness; current model remains unchanged.") from error
    finally:
        lock.release()


class LifecycleControl(BaseModel):
    expected_revision: int = Field(ge=0)
    expected_fingerprint: str | None = Field(default=None, pattern=r"^[a-fA-F0-9]{64}$")


@app.get("/models/{kind}/lifecycle")
def get_lifecycle(kind: str):
    if kind not in ("va", "vi"):
        raise HTTPException(status_code=404, detail="Unknown model pipeline.")
    return lifecycle_state(kind) or {"active_version": getattr(app.state, kind + "_active_version", None),
                                    "previous_version": None, "revision": 0}


@app.post("/models/{kind}/validate")
def validate_candidate(kind: str, request: ActivateVARequest):
    if kind not in ("va", "vi"):
        raise HTTPException(status_code=404, detail="Unknown model pipeline.")
    return model_lifecycle(kind, "validate", request.version)


@app.post("/models/{kind}/deactivate")
def deactivate_candidate(kind: str, request: LifecycleControl):
    if kind not in ("va", "vi"):
        raise HTTPException(status_code=404, detail="Unknown model pipeline.")
    return model_lifecycle(kind, "deactivate", expected_revision=request.expected_revision)


@app.post("/models/{kind}/rollback")
def rollback_candidate(kind: str, request: LifecycleControl):
    if kind not in ("va", "vi"):
        raise HTTPException(status_code=404, detail="Unknown model pipeline.")
    return model_lifecycle(kind, "rollback", expected_revision=request.expected_revision, expected_fingerprint=request.expected_fingerprint)


@app.get("/models/{kind}/fingerprint/{version}")
def model_fingerprint(kind: str, version: str):
    from artifact_fingerprint import fingerprint_files
    if kind not in ("va", "vi"):
        raise HTTPException(status_code=404, detail="Unknown model pipeline.")
    try:
        if kind == "vi":
            fingerprint = fingerprint_files(kind, vi_candidate(version), version)
        else:
            checkpoint, _ = va_candidate(version)
            fingerprint = fingerprint_files(kind, checkpoint.parent, version, checkpoint)
    except (ValueError, OSError) as error:
        raise HTTPException(status_code=404, detail="Stored model fingerprint is unavailable.") from error
    predictor = getattr(app.state, kind + "_predictor", None)
    loaded = getattr(predictor, "artifact_fingerprint", None)
    active = predictor is not None and getattr(predictor, "model_version", None) == version
    return {**fingerprint, "active": active,
            "activeBundleSha256": loaded.get("bundleSha256") if active and loaded else None}


def verify_loaded_identity(kind, predictor):
    state = lifecycle_state(kind) or {}
    expected = state.get("active_bundle_sha256")
    if expected and getattr(predictor,"artifact_fingerprint",{}).get("bundleSha256") != expected:
        raise ValueError("Stored active artifact changed since activation; independent review is required.")
    return predictor
