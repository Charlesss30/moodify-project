# Moodify AI inference service

Python/FastAPI serves local model artifacts only. The two current bilingual models are six-class emotion classifiers; the service converts class probabilities to valence/arousal (V/A) using each artifact's `label_config.json` anchors. V/A is therefore anchor-derived, not a learned regression output.

## Current six-class candidates

| Language | Model | Length | Candidate version | Test result recorded at training |
|---|---|---:|---|---|
| English | `roberta-base` | 64 tokens | `roberta_base_moodify_6c_20261101_051915` | `dair-ai/emotion` official test, n=2,000: accuracy 93.25%, macro-F1 90.17% (in-distribution) |
| Vietnamese | `vinai/phobert-base-v2` | 96 tokens | `phobert_moodify_6c_20261101_053022` | translated test n=2,000: accuracy 85.70%, macro-F1 81.08%; UIT-VSMEC 5-class test n=432: accuracy 79.40%, macro-F1 64.18% |

The Vietnamese result on real Vietnamese text is below the 85% target. The translated test is not a measure of real Vietnamese quality. The English test is from the same dataset distribution as training and does not establish Moodify-domain quality. Both outputs were trained with six labels: `Sadness`, `Enjoyment`, `Love`, `Anger`, `Fear`, `Surprise`.

Artifacts live under the gitignored `code-train/train-KLCN/output/` tree:

```text
output/roberta_base_moodify_6c_20261101_051915/
  best_model/       # config, model.safetensors, tokenizer
  label_config.json # ordered labels, V/A anchors and training evaluation
  manifest.json     # base model, dataset notes, SHA-256, split and license status
  test_report.json  # recorded test metrics, not recomputed by the service
  training_metrics.json
  training_log.txt
output/vietnamese-emotion/phobert_moodify_6c_20261101_053022/
  model/            # config, model.safetensors, PhoBERT tokenizer files
  label_config.json
  preprocessing.json # emoji and curated teencode maps from the training notebook
  manifest.json
  test_report.json
  training_metrics.json
  training_log.txt
```

`manifest.json` SHA-256 is checked when the predictor loads. Do not replace weights in place; create a new version directory so the previous candidate remains available. Verify dataset, base-model and translation-model terms before redistribution. Raw user descriptions, credentials, and tokens must not enter logs or training bundles.

## Start

With the project runner, the new English candidate is the default English model. Vietnamese candidates are listed but need an explicit activation; no active marker is overwritten by installing the candidate.

```powershell
./Start.ps1
```

The runner expects the Python environment at `ai-service/.venv` and binds inference to loopback port 8001. It uses `VA_MODEL_VERSION` for the English candidate. Set `VI_ACTIVE_MODEL_FILE` to a marker created by the AIEngineer model activation flow to serve a Vietnamese candidate.

Manual service start from `ai-service/`:

```powershell
$env:VA_MODEL_ENABLED = "true"
$env:VA_MODELS_ROOT = (Resolve-Path "../code-train/train-KLCN/output").Path
$env:VA_MODEL_VERSION = "roberta_base_moodify_6c_20261101_051915"
$env:VA_ACTIVE_MODEL_FILE = Join-Path $env:LOCALAPPDATA "Moodify/va-active-model.txt"
$env:VI_MODEL_ENABLED = "true"
$env:VI_MODELS_ROOT = (Resolve-Path "../code-train/train-KLCN/output/vietnamese-emotion").Path
$env:VI_ACTIVE_MODEL_FILE = Join-Path $env:LOCALAPPDATA "Moodify/vi-active-model.txt"
$python = ".\.venv\Scripts\python.exe"
& $python -m uvicorn main:app --host 127.0.0.1 --port 8001 --workers 1
```

Activate the Vietnamese candidate from the AIEngineer model workspace or, on the loopback-only service, after startup:

```powershell
Invoke-RestMethod -Method Post -Uri http://127.0.0.1:8001/models/vi/activate `
  -ContentType 'application/json' -Body '{"version":"phobert_moodify_6c_20261101_053022"}'
```

The new English classifier is available through the legacy-compatible `POST /predict-va` route. Vietnamese remains `POST /predict-vi`. Both accept `{"text":"...","language":"en|vi"}` and return `model_version`, `language`, `emotion`, `confidence`, `accepted`, top emotion candidates, class probabilities, V/A and coordinate range. V/A stays in [-1,1]. Inputs are limited to 1–4000 characters; English truncates to 64 model tokens and Vietnamese to 96, matching the notebook max lengths.

The notebook outputs do not contain calibrated confidence thresholds. Until calibration is performed, `accepted` is `null`, not a fabricated true/false. Clients must ask the user to review and confirm the suggested mood. Optional `EN_CONFIDENCE_THRESHOLD` and `VI_CONFIDENCE_THRESHOLD` environment variables are accepted only as explicit deployment configuration after calibration; they are intentionally unset by the runner.

Readiness endpoints are `/health/va/ready` and `/health/vi/ready`; candidate lists and activate routes are `/models/va` and `/models/vi`. Model activation loads the candidate, runs a smoke inference, then writes the active marker atomically. The inference model lifecycle is separate from the 28-label legacy classifier (`POST /predict`, `AI_MODEL_ENABLED`, SHA-256 allowlist) and its emotion-mood mapping. That legacy path remains unchanged.

Install runtime requirements in a dedicated virtual environment with `pip install -r requirements.txt`. Vietnamese requires `pyvi`/sentencepiece as declared by the project. CUDA is optional; the predictors use CPU when CUDA is not available. Do not expose port 8001 publicly.

## Evaluation limits

Smoke sentences in the training notebooks are hand-written integration checks, not gold evaluation data. Training reports are read from the supplied output; this repository integration does not rerun training or recompute the test sets. Add a held-out, human-annotated Moodify test set (with agreement reporting) before claiming app-domain accuracy or selecting an `accepted` threshold. The current anchor for `Love` is a design proposal, not a validated psychological measurement.
