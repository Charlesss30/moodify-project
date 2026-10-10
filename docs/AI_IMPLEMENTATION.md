# Moodify AI integration — implementation and acceptance status

> Tài liệu lưu bằng chứng theo thời điểm, không mô tả trạng thái runtime hiện tại. Cách chạy mới nhất ở [README](../README.md); chức năng và giới hạn tổng hợp ở [báo cáo tuần 7–8](BAO_CAO_TUAN_7_8.md).

Updated: 2026-10-07. This status distinguishes real model evidence, fixture-backed integration tests, and work still blocked. Full acceptance is **not** claimed.

## Outcome by phase

| Phase | Status | Implementation and evidence |
|---|---|---|
| **P0 — artifact/runtime** | **Implemented / real service tested** | Existing RoBERTa weights remain at `code-train/train-KLCN/output/roberta_large_asl_en_01/best_model`; no retraining or model copy. `ai-service/` validates artifact/config, label order and output contract, runs English-only/local-files-only inference, reports readiness/device/hash and uses a single inference slot. Real CPU/GPU service smoke and 30-input latency runs are recorded below. |
| **P1 — service/gateway/provenance** | **Implemented / real gateway and primary app history verified** | Authenticated .NET gateway, bounded input, timeout/error handling and manual fallback. Analysis tokens are short-lived/owner-bound, hold model/mapping/mood-config provenance without retaining input text. History stores provenance and idempotent replay response. In addition to isolated real-inference tests, the controlled primary-database app run produced AI-sourced history records with analysis and decision provenance and persisted recommendations. |
| **P2 — mapping/database alignment** | **Implemented / primary DB checked read-only** | User-confirmed experimental mapping is versioned: joy/amusement→Happy, sadness/grief→Sad, excitement→Excited, love→Romantic. Backend resolves unique mood names to current DB IDs; unmapped labels fail closed. `decisionCandidates` are separate from display `candidates` and use artifact thresholds/selective policy. Live mapping and content coverage are in `AI_DATABASE_ALIGNMENT_RESULTS.md`. |
| **P3 — Flutter** | **Web and Android real AI flows verified** | English AI choice remains opt-in, requires user confirmation, supports manual selection/fallback and keeps the existing Vietnamese/rule-based flow. The user completed English mood analysis and recommendation actions in both Web and the Pixel 5 API 37 emulator. Android was rebuilt/launched with `--dart-define=API_BASE_URL=http://10.0.2.2:5180` rather than its default port 5170. The user confirmed the disabled banner disappeared and recommendations appeared; read-only primary DB records 47 and 48 have AI analysis/decision provenance and 12 recommendations each. The inference service log recorded successful real `/predict` requests. |
| **P4 — Admin registry** | **Implemented / live lifecycle completed and deactivated** | Persistent model metadata/state/audit collections, server allowlist, runtime/config hash validation plus live inference smoke, CAS activation/deactivation/rollback, and Admin/AIEngineer authorization. The Admin UI validated `roberta_large_asl_en_01` with a real five-candidate smoke and activated the primary registry at revision 1, then deactivated it at revision 2. The audit records these successful actions; activeVersion is now none. Registry contains one candidate, so rollback to a distinct artifact version is not demonstrated. |
| **P5 — acceptance** | **Web and Android AI + service-off fallback flows passed** | Build/unit/UI checks, 291 isolated backend assertions, database inventory, real CUDA inference through the .NET gateway, recommendation/history parity against copied live content, stopped-service fallback, primary-registry validation/activation/deactivation, and user-facing Web/Android AI-to-history flows pass. On Android, with the correct 5180 gateway and inference service stopped, the user confirmed manual fallback and recommendations; read-only primary DB history ID 50 records a manual mood and 12 recommendations. The experiment has now been shut down and GPU memory released. |

## Real model evidence (service-local only)

Artifact SHA-256: `EA865317D9EB7CF1B39008062276BAE441801A7D719D33177A3DDD1851AF7B09`. It is a 28-label English multi-label classifier; sigmoid scores are not calibrated per-sentence accuracy, valence, or arousal. The model does not predict VA or recommend content.

Previously observed on 2026-10-06, via a temporary service bound to loopback:

| Runtime | Warm-up | 30 sequential requests | Peak process RSS | Additional observation |
|---|---:|---:|---:|---|
| CPU, Torch 2.11.0+cpu | 91.4 ms | p50 70.7 ms; p95 82.0 ms; max 105.3 ms | 1,623.6 MiB | All 30 returned 28 finite scores; whitespace and overlong input were rejected. |
| RTX 3050 Laptop GPU, CUDA 12.8 / Torch 2.11.0+cu128 | 267.6 ms first warm-up | p50 17.9 ms; p95 22.7 ms; mean 18.3 ms; max 23.3 ms | 2,215.5 MiB | About 1,818 MiB VRAM free after batch; all 30 met the output contract. |

The small local GPU batch was approximately 4.0× faster at p50 and 3.6× at p95 than the separately run CPU batch. Inference auto-selects CUDA when available; readiness exposes `inference_device` and `device_name`. This evidence supports GPU for this workload on the tested host, but does not prove quality, concurrent throughput, end-to-end latency, or suitability on other machines. The temporary service was stopped after that benchmark; no 30-prompt benchmark was rerun through the .NET gateway.

## Real model through gateway and fallback

On 2026-10-06, the checksum-verified CUDA service reported `NVIDIA GeForce RTX 3050 Laptop GPU`. The dedicated `--real-inference` runner read moods/contents from the primary database and copied only moods, genres, movies, music and active content records into a disposable `moodify_real_ai_*` MongoDB. It created temporary test accounts and ran the backend with `AI__Enabled=true`; the checked-in config was not changed.

- Authenticated Admin validation issued a real smoke inference, then activated the registry only in the disposable database.
- Authenticated user analysis of “I feel joyful and amused today.” returned status `matched`, five display candidates and `joy` as its leading label.
- Selecting the live-copy Happy mood (ID 1 in this snapshot) produced the same item IDs/order and midpoint VA for manual and AI-confirmed requests. History contained AI-analysis provenance; the raw input was not stored.
- Deactivation kept manual recommendations available. A separate `--real-inference-off` run started with a previously active registry record but the Python service stopped; the backend reported runtime unavailable, returned explicit analysis fallback, and successfully wrote a manual history entry in that disposable database.
- Both test modes drop their temporary Mongo database and stop their backend process. They did not write to the primary database. This verifies the real model/gateway/recommender contract against a snapshot; the separate primary-database Web and Android acceptance runs are recorded below.

## Current validation results

- Backend build: pass, zero warnings/errors.
- Integration test project build: pass, zero warnings/errors.
- Isolated Mongo backend integration: pass, **291 HTTP/business assertions**. This includes registry restart persistence and replay behavior after restart. The fake inference endpoint uses the real config thresholds/hashes but synthetic response scores; it is not an AI accuracy test.
- Real-model isolated gateway run: pass, CUDA model → authenticated gateway → recommendation/history against copied live-content snapshot; manual and AI-confirmed same-mood output matched.
- Real service-off isolated fallback: pass, active registry + stopped inference service reported unavailable while manual recommendation/history remained functional.
- Python contract tests: 8 pass; the unit suite does not load the full model.
- Flutter: `flutter analyze` clean; 22 tests pass.
- Flutter Web release build: pass; interactive Web AI flow and history are also verified below.
- Admin: `npm run build` and `npm run lint` pass.
- Before local startup: read-only check found 55 Movies, 56 Songs, 7 moods, 3 accounts, 37 history rows and 3 ratings. After the controlled Web/Android sessions, a read-only inventory found 50 history rows, 5 ratings and `softDeleted=0`. Recent AI records 47–49 carry `source=ai`, `analysisSource=ai`, `decisionSource=ai` and 12 recommendations each; ID 50 is a manual-fallback record with 12 recommendations. ID 46 is also manual with six recommendations. The content-free verifier did not print user input. Admin validation/activation intentionally wrote registry/audit state. See [AI_DATABASE_ALIGNMENT_RESULTS.md](./AI_DATABASE_ALIGNMENT_RESULTS.md).

## Local configuration, data safety, and blockers

- `backend/appsettings.json` keeps `AI.Enabled=false`; the local runner overrides it only for the child backend process. Before adding the runner, read-only checks found no listener on ports 5170/5173/5174/5180/8001 and `softDeleted=0` in the primary DB.
- The local runner uses the standard ports (API 5170, inference 8001, Admin 5174), checks Mongo and a read-only inventory before backend startup, and refuses startup if soft-deleted content could be removed by the existing backend initializer. Services run hidden with PID/command identity records and per-service logs under `%LOCALAPPDATA%\Moodify`. Uvicorn's supervisor and listening worker are verified as a parent-child pair so status, idempotent start, and stop target the actual listener without killing unrelated Python processes. It does not modify the registry.
- Android emulator calls the host through `10.0.2.2`; its default port 5170 matches the local runner. Flutter Web uses `localhost:5170`; Admin Vite proxies to the same backend. The former temporary ports 5180/18001 are not used by the daily runner.
- User-facing Flutter Web and Android flows completed in the controlled local session. The Pixel emulator was built for gateway 5180; primary-DB records 47–49 confirm AI provenance and stored recommendations, and record 50 confirms the Android outage fallback. The prior Android disabled banner was caused by its default API target of 5170, not by model validation.
- Integration tests that create/delete records still use isolated temporary databases. The controlled primary-database Admin validate/activate/deactivate actions were intentional registry/audit writes; no reseed, content/mood/account edits, content deletion, or training occurred during acceptance.
- Android AI and corrected-gateway service-off manual fallback are both verified. Remaining technical limits are distinct-version registry rollback, independent NLP/domain quality, and content VA suitability.
- Current device discovery lists only Windows and Edge; no Android emulator/device is available for a new mobile retest. See [AI_DATABASE_ALIGNMENT_RESULTS.md](./AI_DATABASE_ALIGNMENT_RESULTS.md) for the runner and test results from this session.
- Actual model prediction through the .NET gateway and real-service-off fallback passed on disposable Mongo populated from a read-only snapshot. Distinct-version registry rollback, NLP/domain quality, and content VA suitability remain unproven.
- NLP quality on Moodify language/domain, correctness of the experimental mapping, content VA label quality, distinct-version registry rollback, and Android performance remain unproven.

## Local daily operations

Prerequisites: MongoDB at `127.0.0.1:27017`, .NET 10 SDK, Node.js dependencies installed under `admin-web`, Flutter installed, and the existing `ai-service/.venv` plus model weights/checksum. The runner does not install software, download weights, configure auto-start, or read account passwords.

From the repository root in PowerShell:

```powershell
# Start checksum-verified inference, the integrated backend, and local Admin Web (hidden processes).
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\moodify-local.ps1 -Action Start

# Check managed PIDs, port/readiness, model device, and backend availability.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\moodify-local.ps1 -Action Status

# Stop only the recorded, identity-verified local processes.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\moodify-local.ps1 -Action Stop

# Optional manual-only mode: backend/Admin run; inference is not started.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\moodify-local.ps1 -Action Start -WithoutAI
```

Start the server stack before a client. Admin opens at `http://127.0.0.1:5174/`; use **Models** to verify runtime/hash/version, then authenticate as Admin/AIEngineer, Validate, and Activate the allowlisted model. Activation is not automatic and is never written directly to Mongo by the runner. To stop, deactivate first in Admin if the registry must remain inactive; `-Action Stop` itself stops processes only and does not change registry/audit state. The checked-in `AI.Enabled=false` remains unchanged.

Run a Flutter Web development client in another terminal:

```powershell
cd .\mobile-app
flutter run -d web-server --web-hostname localhost --web-port 5173 --dart-define=API_BASE_URL=http://localhost:5170
```

For the Android emulator:

```powershell
cd .\mobile-app
flutter run -d emulator-5554 --dart-define=API_BASE_URL=http://10.0.2.2:5170
```

For a physical phone, start the stack with `-ListenAddress 0.0.0.0`, allow the backend port through Windows Firewall only on the trusted private network, and use the PC's private LAN IPv4 in `--dart-define=API_BASE_URL=http://<PC-LAN-IP>:5170`. Do not expose port 8001 or bind the inference service outside loopback. Admin Vite is intentionally loopback-only. No credentials or JWTs are stored by the runner; sign in privately using an authorized account (Mongo stores password hashes, not recoverable plaintext).

The runner records logs/PIDs in `%LOCALAPPDATA%\Moodify`; if readiness fails, inspect the corresponding `.stderr.log` and `.stdout.log`. It rejects an occupied port rather than killing an unowned process, is idempotent for a healthy stack in the same mode, and requires `Stop` before changing AI mode. The Admin model status requires authenticated UI access to confirm registry activeVersion; runtime Ready alone does not mean the model is active.

The integration test executable runs destructive cases only against disposable Mongo databases. Example (run in the background from a separate PowerShell session):

```powershell
Start-Process -FilePath dotnet -ArgumentList 'tests\bin\Debug\net10.0\Moodify.Integration.dll' -WorkingDirectory $PWD -WindowStyle Hidden -RedirectStandardOutput "$env:TEMP\moodify-tests.out.log" -RedirectStandardError "$env:TEMP\moodify-tests.err.log" -PassThru
```

English AI remains experimental and requires mood confirmation. Vietnamese continues through the existing manual/rule-based path. NLP/domain quality, content VA suitability, and distinct-artifact rollback remain unproven.

## Follow-up fixes — 2026-10-07

The independent audit findings F1/F2 are fixed in source and regression-tested. Mapping is now persisted by mood ID with revisioned approval and audit; Models includes offline evaluation metrics and live local content coverage. Backend tests now pass 320 assertions; Flutter passes 23 tests. See the consolidated regression notes below; current startup commands are in the repository README. The earlier revision-2/inactive and shutdown descriptions above are historical, not current runtime guarantees. Existing running backend processes must be restarted to use these changes.

## Consolidated regression notes (historical evidence: 07/10/2026)

The superseded audit/fix logs were removed on 10/10/2026. Their maintenance-relevant details are retained here:

- Recommendation replay fingerprints mood ID, content type, limit and balanced. Reusing an analysis token with changed parameters returns 409; old history without a request hash is not replayed without verification. Flutter clears the previous AI decision when changing content type.
- Mapping binds to stable mood IDs after initial unique-name bootstrap. Renaming a mood preserves its binding; deleting it must not silently bind to a new mood with the same name. Missing targets remain invalid.
- Mapping approval validates all 28 labels and target IDs, uses revision compare-and-swap, archives the previous mapping and invalidates analysis tokens for the old version. Current role permissions are documented in WEEK_7_8_ROLE_REVIEW.md.
- Mapping and audit writes on standalone MongoDB are not a multi-collection transaction. After an interrupted operation, inspect the current revision and audit before retrying.
- Historical regression evidence: 320 backend assertions, 23 Flutter tests, real CUDA changed-filter/renamed-mood checks, gateway/history and unavailable-service fallback on temporary databases. These counts describe that run, not a fresh test on 10/10/2026. Later feature/role checks are recorded in the week 7–8 report.
- Offline model metrics are not per-request accuracy. Content coverage does not prove VA label quality, and pending content must not be automatically marked reviewed merely to improve coverage.
- Independent domain quality, Vietnamese AI, content VA suitability and rollback to a distinct model artifact remain unproven. Keep runtime readiness separate from persisted registry activation.