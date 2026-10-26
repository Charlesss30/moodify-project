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
    if os.environ.get("VA_MODEL_ENABLED", "").lower() == "true":
        try:
            from va_predictor import ValenceArousalPredictor

            version = active_va_version() or Path(os.environ.get("VA_MODEL_DIR", str(_default_va_dir))).name
            checkpoint, tokenizer_dir = va_candidate(version)
            app.state.va_predictor = ValenceArousalPredictor(checkpoint, tokenizer_dir, version)
            app.state.va_active_version = version
        except Exception as error:
            app.state.va_load_error = type(error).__name__
            logger.error("Valence/arousal model is not ready (%s).", type(error).__name__)
    if os.environ.get("VI_MODEL_ENABLED", "true").lower() == "true":
        version = active_vi_version()
        if version:
            try:
                from vietnamese_predictor import VietnameseEmotionPredictor

                app.state.vi_predictor = VietnameseEmotionPredictor(vi_candidate(version), version)
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


@app.get("/health/live")
def live():
    return {"status": "live"}


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
    # A newly uploaded candidate can bootstrap an empty VA runtime.
    if app.state.va_predictor is not None and request.version == app.state.va_active_version:
        return {"status": "ready", "active_version": request.version,
                "device": app.state.va_predictor.device}
    if not _predict_lock.acquire(blocking=False):
        raise HTTPException(status_code=429, detail="Inference is busy. Retry model activation shortly.")
    try:
        try:
            checkpoint, tokenizer = va_candidate(request.version)
            from va_predictor import ValenceArousalPredictor

            candidate = ValenceArousalPredictor(checkpoint, tokenizer, request.version)
            smoke = candidate.predict("Moodify model readiness check.")
            if smoke.get("model_version") != request.version:
                raise ValueError("Candidate smoke check returned an unexpected model version.")
            marker = Path(os.environ.get("VA_ACTIVE_MODEL_FILE", str(va_models_root() / ".active-va-model")))
            marker.parent.mkdir(parents=True, exist_ok=True)
            temporary = marker.with_name(marker.name + ".tmp")
            temporary.write_text(request.version + "\n", encoding="utf-8")
            temporary.replace(marker)
            app.state.va_predictor = candidate
            app.state.va_active_version = request.version
            app.state.va_load_error = None
            return {"status": "ready", "active_version": request.version, "device": candidate.device}
        except Exception as error:
            logger.exception("VA candidate activation failed for %s.", request.version)
            raise HTTPException(status_code=422, detail="Candidate could not be loaded or failed its readiness check.") from error
    finally:
        _predict_lock.release()


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
    return {"status": "ready", "model_version": predictor.model_version, "language": "vi", "inference_device": predictor.device}


@app.get("/models/vi")
def list_vi_models():
    return {"items": vi_candidates(), "active_version": app.state.vi_active_version}


@app.post("/models/vi/activate")
def activate_vi_model(request: ActivateVARequest):
    if app.state.vi_predictor is not None and request.version == app.state.vi_active_version:
        return {"status": "ready", "active_version": request.version, "device": app.state.vi_predictor.device}
    if not _vi_predict_lock.acquire(blocking=False):
        raise HTTPException(status_code=429, detail="Vietnamese inference is busy. Retry activation shortly.")
    try:
        try:
            from vietnamese_predictor import VietnameseEmotionPredictor

            candidate = VietnameseEmotionPredictor(vi_candidate(request.version), request.version)
            smoke = candidate.predict("Tôi cảm thấy vui vẻ và bình yên.")
            if smoke.get("model_version") != request.version or smoke.get("language") != "vi":
                raise ValueError("Vietnamese model readiness result is invalid.")
            marker = Path(os.environ.get("VI_ACTIVE_MODEL_FILE", str(vi_models_root() / ".active-model")))
            marker.parent.mkdir(parents=True, exist_ok=True)
            temporary = marker.with_name(marker.name + ".tmp")
            temporary.write_text(request.version + "\n", encoding="utf-8")
            temporary.replace(marker)
            app.state.vi_predictor = candidate
            app.state.vi_active_version = request.version
            app.state.vi_load_error = None
            return {"status": "ready", "active_version": request.version, "device": candidate.device}
        except Exception as error:
            logger.exception("Vietnamese candidate activation failed for %s.", request.version)
            raise HTTPException(status_code=422, detail="Vietnamese model could not be loaded or failed readiness check.") from error
    finally:
        _vi_predict_lock.release()


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
