# Moodify AI inference service

This service can load the local emotion-classification model and the separate
EmoBank RoBERTa valence/arousal (VA) checkpoint. It never sends text to a third
party. The VA model accepts English only and is enabled with
`VA_MODEL_ENABLED=true`. Model artifacts remain outside this source folder.

The default `MODEL_DIR` resolves to the local `best_model` folder, which is
excluded from Git and must be supplied separately. Set
`MODEL_DIR` to an absolute path when deploying elsewhere and set
`MODEL_SHA256` to the SHA-256 of `model.safetensors`. The service checks the
checksum, 28-label order, multi-label classifier configuration, and 128-token
limit before reporting readiness. Do not expose it publicly; bind to loopback.

From the `ai-service` directory, create a dedicated virtual environment,
install these requirements, and run one process after verifying sufficient
free memory:

```powershell
py -3.14 -m venv .venv
.\.venv\Scripts\Activate.ps1
pip install -r requirements.txt
pip install -r requirements-gpu.txt
$python = ".\.venv\Scripts\python.exe"
& $python -c "import torch; print(torch.__version__, torch.cuda.is_available(), torch.cuda.get_device_name(0) if torch.cuda.is_available() else 'CPU')"
$env:AI_MODEL_ENABLED = "true"
$env:MODEL_SHA256 = "EA865317D9EB7CF1B39008062276BAE441801A7D719D33177A3DDD1851AF7B09"
& $python -m uvicorn main:app --host 127.0.0.1 --port 8001 --workers 1
```

The CUDA wheel is optional and was verified with Python 3.14, CUDA 12.8, and
an NVIDIA RTX 3050 Laptop GPU (4 GB). It is large (about 2.8 GB). The predictor
automatically selects CUDA when `torch.cuda.is_available()` is true and falls
back to CPU otherwise. Keep the service bound to loopback and confirm enough
free VRAM before enabling it. Do not install the CUDA wheel into the global
Python environment.

`GET /health/live` reports process liveness. `GET /health/ready` reports 503
until the model has loaded and passed artifact checks; when ready it reports
`inference_device` and `device_name` so CUDA use is observable. The predictor
places both the model and tokenized inputs on CUDA when available and otherwise
uses CPU. `POST /predict` accepts
`{"text":"...","language":"en"}`. Whitespace, 1–4000 character bounds, and
128-token bounds are validated; long input is rejected, never silently cut.
Concurrent inference beyond the single active slot returns 429.

## Valence/arousal regression model

The checkpoint is expected at
`code-train/train-KLCN/output/roberta_va_regression/roberta_va_regression.pt`
with its tokenizer at `.../tokenizer/tokenizer.json`. The adapter recreates
`RobertaModel(roberta-base) + CLS + Dropout(0.3) + Linear(768, 2)` from
`emobank.ipynb`, loads the `state_dict` with `weights_only` mode, and uses the
same 128-token input length. Outputs are clamped to the training target range
`[-1, 1]`.

The local runner enables VA automatically when the checkpoint and tokenizer
are present. For a manual service start, set:

```powershell
$env:VA_MODEL_ENABLED = "true"
$env:VA_MODEL_DIR = (Resolve-Path "..\code-train\train-KLCN\output\roberta_va_regression").Path
$env:VA_CHECKPOINT = Join-Path $env:VA_MODEL_DIR "roberta_va_regression.pt"
$env:VA_TOKENIZER_DIR = Join-Path $env:VA_MODEL_DIR "tokenizer"
```

`GET /health/va/ready` checks this model; `POST /predict-va` returns its VA
coordinates. The authenticated backend endpoint
`POST /api/MoodAnalysis/valence-arousal` finds the nearest configured mood.
The web page then sends the predicted coordinates and mood ID to the existing
recommendation endpoint so content ranking uses the model output. This model
has a separate lifecycle from the 28-label model registry. No independent
quality validation is claimed for this checkpoint.

The dependency file retains the exported deployment's supported ranges and
Transformers version; it is not a post-smoke lockfile. Controlled local service
benchmarks on 2026-10-06 used the same 30 sequential English inputs:

- CPU (`torch 2.11.0+cpu`): p50 70.7 ms, p95 82.0 ms, peak server RSS
  1,623.6 MiB.
- RTX 3050 Laptop GPU (`torch 2.11.0+cu128`): p50 17.9 ms, p95 22.7 ms,
  peak server RSS 2,215.5 MiB; about 1.5 GiB of VRAM was occupied by the
  loaded model. GPU warm-up was 267.6 ms.

All 30 responses on each run met the 28 finite-score contract. Whitespace,
unsupported-language, and 129-token requests returned HTTP 422 on the GPU
service. These are small local latency/contract tests, not model-quality
evidence or production-readiness claims. Gateway/app end-to-end and mobile
device performance were not tested. The temporary service was stopped.
