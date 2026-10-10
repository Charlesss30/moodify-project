# AI–database alignment results

> Tài liệu lưu bằng chứng theo thời điểm, không mô tả trạng thái runtime hiện tại. Cách chạy mới nhất ở [README](../README.md); chức năng và giới hạn tổng hợp ở [báo cáo tuần 7–8](BAO_CAO_TUAN_7_8.md).

Updated: 2026-10-07. The initial live inventory was read-only. All destructive/write-capable automated integration tests used disposable MongoDB databases prefixed `moodify_test_`, dropped by the test runner in its `finally` cleanup. A separately authorized controlled local acceptance subsequently wrote model-registry/audit state to the primary database; the user confirmed the added history entries were expected. No content/mood/account data was reseeded, deleted, or edited, and no training was run.

## Result summary

| Requirement | Status | Evidence |
|---|---|---|
| Resolve approved experimental emotion mapping using live mood records | **Done / tested** | Read-only inventory resolved exact unique names and IDs: Happy=1, Sad=2, Excited=4, Romantic=6. Backend integration confirms a mapped label returns the fixture database's mood ID. |
| Keep unmapped labels and Focused/Nostalgic manual | **Done / tested** | 22 of 28 labels remain unmapped, including `neutral`, `relief`, `curiosity`, and `remorse`; Focused and Nostalgic have no assigned emotion labels. |
| Separate display top-5 from threshold-qualified decisions | **Done / tested** | Predictor reads per-label thresholds and selective threshold from the model config; response returns `candidates` and `decisionCandidates`. Low-selective, opposing mapped, and unmapped-leading fake-contract scenarios pass. |
| Make manual and AI-confirmed same-mood recommendations equivalent | **Done / tested on fixtures and actual model** | Backend integration compares item order/IDs and midpoint valence/arousal for identical mood and request parameters; the real-model run also matched on a disposable copy of live content. |
| Preserve analysis/mood provenance and prevent duplicate history | **Done / tested on fixtures** | Analysis owner, expiry, model/mapping versions and mood-configuration hash are checked; retry returns the saved response; unique `analysisId` history index and test assertions prevent duplicate writes. |
| Persist and administer model lifecycle | **Done / live validate, activate, deactivate verified** | Registry validation performs a live inference smoke request, records audit, supports CAS activate/deactivate/rollback, and rejects stale revisions, unauthorized roles, unavailable or malformed smoke responses. Admin validation ran a real five-candidate GPU smoke; activation set primary registry revision 1 and subsequent deactivation set revision 2 with `activeVersion=none`. All actions appear as successful audit rows. |
| Real-model gateway/recommendation/history | **Done / tested on an isolated snapshot** | Actual CUDA inference passed through authenticated .NET, which recommended against copied live moods/content, matched manual ranking/VA for the same mood, and wrote provenance history in a disposable DB. A separate run stopped Python while retaining Active registry state; analysis returned unavailable and manual recommendation/history still worked. |
| Flutter Web / primary-database app acceptance | **Passed for English AI → mood confirmation/recommendations/history** | Flutter analyze and 22 tests pass; release Web build succeeds. The controlled release Web session uses the loopback gateway and the configured `localhost` CORS origin. The app is showing its History screen. A read-only primary-DB query verified the two newest AI recommendation records: Happy and Sad each have `source=ai`, `analysisSource=ai`, `decisionSource=ai`, and 12 stored recommendations. The query intentionally omitted raw input text. |
| Flutter Android emulator acceptance | **Passed for real AI and service-off manual fallback** | A Pixel 5 API 37 emulator was deployed with `API_BASE_URL=http://10.0.2.2:5180`; the user confirmed the disabled banner disappeared and live inference/recommendations worked. Python service logs include successful real `/predict` calls. Read-only primary DB records 47–49 have AI source/analysis/decision provenance and 12 stored recommendations each. With the Python service stopped while the same Android app still targeted 5180, the user confirmed the explicit unavailable fallback, manual mood selection and recommendations; primary DB record 50 is manual with 12 recommendations. The service was restored afterward. Raw text was not exposed in the read-only checks. |

## Live MongoDB read-only snapshot

The initial pre-start check queried `mood_recommendation` through the existing application models and did not initialize or mutate the primary database.

- Initial pre-start inventory: 55 Movies, 56 Songs, 7 moods, 3 accounts, 37 mood-history records, 3 ratings.
- Post-session read-only recount: 55 Movies, 56 Songs, 7 moods, 3 accounts, 50 mood-history records, 5 ratings; `softDeleted=0`. Controlled Admin validation/activation/deactivation intentionally added registry/audit state.
- Latest content-free history summary: records 47–49 are AI-sourced with AI analysis/decision provenance and 12 recommendation snapshots; record 50 is manual with 12 recommendations and was created during the service-off Android fallback run. ID 46 was manual with six recommendations. Raw user input was not displayed by the verifier.
- Mood bounds are valid and within [-1,1]. Content VA coordinates are within [-1,1]; movie genre links and music records passed existing integrity checks.
- Review status: Movies 0 reviewed / 55 pending; Songs 4 reviewed / 52 pending. The integration did not promote any content. Pending records remain eligible in the current experimental recommender.
- Exact live mood IDs, current bounds and mapping resolution:

| ID | Mood | Valence bounds | Arousal bounds | Mapping |
|---:|---|---:|---:|---|
| 1 | Happy | [0.5, 1] | [0.2, 1] | `joy`, `amusement` |
| 2 | Sad | [-1, -0.2] | [-1, 0.2] | `sadness`, `grief` |
| 3 | Relaxed | [0.1, 0.8] | [-1, 0.1] | Unmapped |
| 4 | Excited | [0.4, 1] | [0.5, 1] | `excitement` |
| 5 | Focused | [0, 0.6] | [0.2, 0.8] | Manual |
| 6 | Romantic | [0.3, 1] | [-0.2, 0.5] | `love` |
| 7 | Nostalgic | [-0.3, 0.5] | [-0.5, 0.3] | Manual |

The approved mapping is an experimental, user-confirmed name mapping, resolved uniquely at request time; it is not a learned VA model. A missing/duplicate mood name remains unmapped rather than receiving a guessed ID.

### Local content coverage

Distances are squared Euclidean distance from each live mood midpoint to current local content. They are inventory statistics, **not** relevance-quality scores or acceptance thresholds.

| Mood | Movies: total / reviewed / pending; nearest local item | Songs: total / reviewed / pending; nearest local item |
|---|---|---|
| Happy | 55 / 0 / 55; `MOV001` (0.0025, pending) | 56 / 4 / 52; `SPOTIFY_2rSrLECfNaKYJ96UJ4ddDt` (0.0250, reviewed) |
| Sad | 55 / 0 / 55; `OMDB_tt0111161` (0.4100, pending) | 56 / 4 / 52; `SPOTIFY_1KZG1H1OCugGqIRmk1wF9m` (0.1300, pending) |
| Relaxed | 55 / 0 / 55; `9cf291ba-985d-49de-8377-de5cf9370d24` (0.3650, pending) | 56 / 4 / 52; `SPOTIFY_0faFpGqSb9MFriH1XqGKbO` (0.0000, reviewed) |
| Excited | 55 / 0 / 55; `MOV003` (0.0250, pending) | 56 / 4 / 52; `SPOTIFY_2rSrLECfNaKYJ96UJ4ddDt` (0.0000, reviewed) |
| Focused | 55 / 0 / 55; `OMDB_tt0359950` (0.0178, pending) | 56 / 4 / 52; `SPOTIFY_3ouNEk0tv5TTi8VWMe1xbX` (0.0400, pending) |
| Romantic | 55 / 0 / 55; `OMDB_tt0211915` (0.0100, pending) | 56 / 4 / 52; `SPOTIFY_1qbmS6ep2hbBRaEZFpn7BX` (0.1250, pending) |
| Nostalgic | 55 / 0 / 55; `9cf291ba-985d-49de-8377-de5cf9370d24` (0.0500, pending) | 56 / 4 / 52; `SPOTIFY_5GpfyJrKHJI36jDKtQiyGN` (0.0400, pending) |

No content annotations were altered. Review/pending state does not imply the coordinates are correct/incorrect. Mood overlap and close midpoints can naturally produce shared recommendations.

## Verification run

- `dotnet build backend\mood_recommendation.csproj --no-restore`: pass, 0 warnings / 0 errors.
- `dotnet build tests\Moodify.Integration.csproj --no-restore`: pass, 0 warnings / 0 errors.
- `dotnet run --project tests\Moodify.Integration.csproj --no-build`: pass, **291 HTTP/business assertions** against a temporary MongoDB database. Covers mapping lookup, threshold/display separation, identical manual/AI-confirmed ranking and VA, duplicate replay (including after backend restart), mood-bound invalidation, errors/timeouts/fallback, role checks, live-smoke validation gate, audit, deactivate, rollback and registry persistence after restart.
- Real-model gateway acceptance: pass with the actual CUDA service (`NVIDIA GeForce RTX 3050 Laptop GPU`). Authenticated Admin validation ran a real smoke inference; an authenticated user request returned `matched`, five display candidates, top label `joy`; manual and AI-confirmed recommendation IDs/order and VA matched against a disposable database copied from a read-only primary-data snapshot; history provenance was verified.
- Stopped-service acceptance: pass after stopping the Python process. With a previously active registry entry in the temporary database, runtime status was `Unavailable`, analysis returned explicit `unavailable`, and manual recommendation/history still succeeded.
- Python contract tests: 8 pass. These do not load the full model.
- `flutter analyze`: pass, no issues. `flutter test`: 22 pass.
- `flutter build web --release`: pass. This is a build check, not a real browser inference flow.
- Admin `npm run build` and `npm run lint`: pass.
- Primary MongoDB read-only inventory: pass; pre-start and post-session counts are recorded above. Admin validation/activation intentionally wrote registry/audit state to the primary database. The user confirmed the earlier three added history records were expected; the newest read-only summary verified AI provenance for two additional records. No content, mood, or account edits, reseed, or deletion was performed by this acceptance run.

The 291-assertion integration server intentionally uses a contract-shaped fake inference endpoint. Its outputs test gateway behavior only; they are not model predictions or NLP-quality evidence. The separate real-model run above used actual inference; it copied database content into a temporary database and did not write to the primary database.

## Remaining acceptance blockers

1. Distinct-version registry rollback and independent NLP/content-label quality remain unproven.
2. The controlled experiment is shut down: registry revision 2 has no active model, ports 5173/5174/5180/18001 are released, and the existing backend on 5170 was preserved. The app can be restarted using the documented API override and explicit model activation.
3. The registry has one allowlisted artifact. Deactivate/rollback back to that same validated model is tested; rollback to a distinct artifact version is not demonstrated.
4. Independent Moodify-domain accuracy and content-label suitability are unproven. Existing GoEmotions benchmark figures are not per-request confidence or database recommendation quality.

## Local daily operations

`scripts/moodify-local.ps1` provides `Start`, `Status`, and `Stop` actions. It uses standard local ports (backend 5170, inference 8001, Admin 5174), starts managed processes hidden, records PID/command identity and logs under `%LOCALAPPDATA%\Moodify`, and refuses to stop a process whose identity changed. Uvicorn's Windows supervisor and listening worker are treated as one verified parent-child service tree; shutdown targets the worker and supervisor, not unrelated Python processes. `Start` checks Mongo reachability and runs the read-only inventory before starting the backend; it refuses to start if `softDeleted` is not confirmed zero because the existing backend initializer cleans soft-deleted content. AI mode requires 2500 MiB free system RAM and, where `nvidia-smi` is available, 2500 MiB free VRAM; it also verifies artifact hash and runtime readiness. It refuses occupied unowned ports, is idempotent for a healthy stack in the same mode, and does not start automatically with Windows.

- Tested PowerShell parse/Status with all experimental ports closed: pass.
- Tested `Start -WithoutAI` against the primary DB after read-only preflight: API/Admin became ready; a repeated Start did not create duplicate processes; Status reported inference unavailable and backend available; Stop released 5170/5174 and left registry unchanged.
- AI-enabled runner retest: primary DB read-only inventory passed (`softDeleted=0`); checksum-verified `roberta_large_asl_en_01` became Ready on the RTX 3050 CUDA device; API/Admin readiness, `Status`, and repeated `Start` passed without duplicate processes. The repeat confirmed the recorded Uvicorn parent and listening worker.
- Stop retest started from a state file that predated worker tracking: the runner verified the worker PID, command line, and parent relationship, stopped the worker and supervisor, then confirmed ports 5170/5174/8001 closed. GPU memory returned to 184 MiB used / 3779 MiB free. A second Stop/Start cycle with `WorkerPid` persisted also passed; the final AI-enabled stack is healthy.
- Integration project build passed with zero warnings/errors; the isolated backend suite passed 291 HTTP/business assertions. Real CUDA inference through the authenticated gateway passed against a disposable Mongo content snapshot, including same-mood recommendation parity and the disabled-registry manual fallback. Python contract tests passed: 8 tests via `ai-service\.venv\Scripts\python.exe -m unittest discover -s ai-service -p 'test_*.py' -v`. The repository-root `.venv` does not contain pytest; the service suite uses `unittest`.
- A read-only live DB check reported `activeVersion=none`, revision 2, and `softDeleted=0`; startup does not activate the registry. Admin must still validate/activate with an authorized account before client AI analysis is available.
- Current device recheck is blocked: `flutter devices` listed Windows and Edge only, with no Android emulator/device. This does not invalidate the earlier Pixel 5 API 37 acceptance evidence above; Android could not be retested in this session.

For exact commands, Web/Admin/Android API URLs, LAN-phone caveats, restart steps, and safe activation/deactivation guidance, see [AI_IMPLEMENTATION.md](./AI_IMPLEMENTATION.md) under “Local daily operations”. `backend/appsettings.json` remains disabled. The runner does not read or store credentials; the Mongo account collection contains password hashes, not recoverable plaintext, and Admin actions require private sign-in.

Independent NLP/domain quality, content VA suitability, and rollback to a distinct model artifact remain unproven. Never claim those from build or integration tests alone.

## Follow-up resolution — 2026-10-07

F1 (replay ignores changed request parameters) and F2 (rename breaks mapping) were reproduced then fixed. Stable persisted mood bindings, role-gated mapping approval/CAS/audit, metrics and content coverage UI are implemented. Regression suites: 320 backend assertions, 23 Flutter tests and Admin browser mapping approval passed; real CUDA gateway/recommendation/history and offline-service fallback also passed on temporary databases. See [consolidated regression notes](AI_IMPLEMENTATION.md#consolidated-regression-notes-historical-evidence-07102026). Earlier inactive registry/shutdown snapshots remain historical; restart backend to load this source revision. No main-database content edits or activation changes were made during these fixes.
