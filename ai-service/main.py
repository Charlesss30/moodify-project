import logging
import os
from contextlib import asynccontextmanager
from pathlib import Path
from threading import BoundedSemaphore

from fastapi import FastAPI, HTTPException
from pydantic import BaseModel, Field


logger = logging.getLogger("moodify.ai")
_predict_lock = BoundedSemaphore(1)
_default_model_dir = (
    Path(__file__).resolve().parents[1]
    / "code-train"
    / "train-KLCN"
    / "output"
    / "roberta_large_asl_en_01"
    / "best_model"
)


@asynccontextmanager
async def lifespan(app: FastAPI):
    app.state.predictor = None
    app.state.load_error = None
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
    yield


app = FastAPI(title="Moodify English Emotion Inference", lifespan=lifespan)


class PredictionRequest(BaseModel):
    text: str = Field(min_length=1, max_length=4000)
    language: str = "en"


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
