# AI Engineer workspace — 08/10/2026

> Cập nhật hiện hành 27/10/2026: xem [AI_SYSTEM_UNIFICATION_RESULTS.md](AI_SYSTEM_UNIFICATION_RESULTS.md). Nội dung bên dưới là snapshot triển khai trước đó; Inference lab/overview và hợp đồng song ngữ đã được cập nhật.

## Chức năng đã triển khai

AIEngineer đăng nhập vào cùng website và mặc định mở AI overview. Có 9 mục: AI overview, Model registry, Emotion mapping, Evaluation, Datasets, Training jobs, Feedback analytics, Inference lab, Audit log. Admin cũng truy cập được các mục này; User bị API từ chối. Không cấp AIEngineer quyền CRUD tài khoản/phim/nhạc.

- Overview: runtime, thiết bị, model active, số dataset/job/kết quả và thống kê phản hồi. Trạng thái phản ánh API; không coi runtime sẵn sàng là model active.
- Registry: giữ validate/activate/deactivate/rollback với revision và audit hiện có.
- Mapping: duyệt emotion → mood, kiểm tra độ phủ nội dung. Không đổi VA catalog trực tiếp.
- Evaluation: benchmark model đang cấu hình và kết quả training do kỹ sư nhập. So sánh chỉ có ý nghĩa khi cùng dataset/protocol/test split. Kết quả nhập không chứng minh chất lượng độc lập hoặc artifact đã được server xác minh.
- Datasets: đăng ký snapshot bất biến; tên/nguồn, các mẫu text/labels/split, checksum và người tạo. Loại bỏ bằng cách từ chối text trùng kể cả xuyên split; chỉ chấp nhận 28 nhãn canonical và đủ train/validation/test. Tối đa 60.000 mẫu, upload 32 MB, BSON dưới 15 MB. Export có rows và metadata; import yêu cầu mảng rows.
- Training jobs: lưu cấu hình, dataset hash, notebook template hash, revision và lịch sử chuyển trạng thái trong MongoDB. Download bundle/Colab notebook; đánh dấu running, failed, cancelled; import workspace_report.json. Có kiểm tra provenance, số mẫu test, khoảng metric, hash và chống cập nhật revision cũ.
- Feedback analytics: phân bố sao, số/điểm trung bình theo nội dung; export ratings với mã người dùng ngẫu nhiên mới cho mỗi lần xuất. Không lộ email/tên/account ID/comment. Giới hạn 20.000 rating mới nhất, ghi rõ trên UI. Đây là dữ liệu recommender, không phải nhãn cảm xúc NLP.
- Inference lab/Audit: dùng API kiểm thử thật và audit registry hiện có.

## Đã đọc và tích hợp notebook của người dùng

Nguồn: `code-train/train-KLCN/RoBERTa_large.ipynb`. Giữ nguyên file gốc; bản template nằm tại `backend/Training/RoBERTa_large.template.ipynb`.

Notebook có 12 code cell: cài dependency; cấu hình/Drive; sinh module; nạp dữ liệu; khởi tạo model; train; calibration; test; export; đồ thị; kiểm thử inference; đóng gói.

Quy trình gốc được giữ:
- FacebookAI/roberta-large, tiếng Anh, 28 nhãn GoEmotions, multi-label.
- ASL gamma_neg=2, gamma_pos=0, clip=0.05; trọng số dương từ TRAIN, giới hạn 1–4.
- Mặc định 6 epoch, batch 2, gradient accumulation 16, learning rate 1e-5, seed 42, max length 128.
- Loại exact-text leakage; validation chia selection/calibration theo nhóm text, mỗi phần ít nhất 200 mẫu. Dashboard chặn job khi validation dưới 400.
- Selection chọn checkpoint; calibration tune threshold global/per-label và selective precision/coverage. Không dùng test để chọn lại siêu tham số.
- Metric: micro/macro F1 và các metric gốc; benchmark được notebook đánh dấu previously_seen_benchmark. Không gọi đây là bằng chứng dữ liệu Moodify độc lập.

Notebook tải từ job giữ nguyên loss/train/evaluation, thêm cầu nối:
1. Bundle base64 chứa data/config, xác minh SHA-256; tạo CSV cho notebook.
2. Thư mục Drive riêng `train-KLCN/workspace-jobs/<jobId>`; không ghi đè output model gốc.
3. Áp dụng hyperparameter của job trước khi tạo log/output.
4. Xóa output/execution count cũ trong bản tải xuống.
5. Cell cuối xác minh config/label, hash weights và xuất `workspace_report.json`.
6. Nếu selective policy bị tắt với sentinel threshold >1, chỉ chuẩn hóa threshold về 1 để hợp đồng runtime [0,1] hợp lệ; vẫn giữ enabled=false. Không tự bật acceptance policy.

## Cách sử dụng

1. Khởi động lại backend để nạp controller mới, đăng nhập tài khoản AIEngineer.
2. Datasets: upload JSON array `{text, labels: ["joy"], split: "train"}` (tương tự validation/test). Mẫu tải về chỉ minh họa format, không đủ chất lượng/số mẫu cho training.
3. Training jobs: chọn dataset, tên phiên bản mới và cấu hình; Prepare job.
4. Tải Colab notebook, mở trên Colab GPU, chạy đúng thứ tự cell. Cần tài khoản/Drive và tài nguyên Colab của người dùng. Không đưa secret API của ứng dụng vào notebook.
5. Có thể đánh dấu running để theo dõi thủ công. Sau khi train/evaluate/export, tải report cuối và Import result tại đúng job.
6. Xem Evaluation. Đưa artifact mới lên môi trường inference và cấu hình allowlist/model hash bằng quy trình triển khai hiện có, sau đó Validate và Activate. Dashboard không tự đổi đường dẫn model/weights của runtime đang chạy.

## Phạm vi và giới hạn cần hiểu đúng

- Training thực thi ở Colab, không có worker tự chạy GPU từ nút bấm website; trạng thái running do kỹ sư đánh dấu. Cancel record không tắt Colab; phải dừng runtime bên Colab.
- Kết quả nhập có trạng thái `reported`, không phải artifact đã được xác minh. Trọng số không upload vào MongoDB. Model serving vẫn độc lập với job.
- Chưa có A/B traffic routing, tự động triển khai artifact mới, streaming log từ Colab hoặc đồng bộ tiến độ Colab.
- Dashboard giới hạn 200 dataset và 200 job mới nhất trong danh sách; các bảng có tìm kiếm/phân trang 12 dòng.
- Không thu thập raw mood text từ người dùng để train. Dữ liệu gán nhãn NLP cần được chuẩn bị/duyệt riêng.
- Không chạy lại training RoBERTa trong lượt triển khai này; không tuyên bố metric mới hay model mới đã đạt chất lượng.
- Chưa tự restart stack đang phục vụ; code được build/test ở thư mục riêng với MongoDB tạm.

## API và lưu trữ

`/api/AiWorkspace`: GET/POST datasets, GET datasets/{id}, GET/POST jobs, GET jobs/{id}/bundle, GET jobs/{id}/notebook, POST jobs/{id}/state, POST jobs/{id}/report, GET feedback, POST feedback/export.

Collections mới `aiDatasets`, `aiTrainingJobs`; chỉ được tạo khi có ghi thực tế. Dataset bất biến; events nằm cùng document job và cập nhật qua CAS. Các collection nội dung, tài khoản và model active không bị thay đổi bởi các thao tác này.

## Kết quả kiểm tra lượt triển khai
- .NET build riêng: 0 warning, 0 error.
- 428 HTTP/business assertions đạt trên MongoDB tạm, gồm quyền AIEngineer/User, dataset trùng, giới hạn hyperparameter, notebook giữ ASL, CAS job, provenance report và trạng thái reported.
- Browser smoke đạt: đăng nhập AIEngineer vào Overview, không có tab Users/Movies/Music, 9 mục AI, đăng ký dataset fixture, tạo job, download notebook 13 cell, running/cancel record, mapping và feedback. Luồng Admin cũ cũng đạt trong cùng smoke.
- Frontend lint/build đạt.
- Tất cả cell của notebook sinh ra parse được; đã thực thi riêng cell cầu nối trong thư mục tạm để kiểm tra checksum, CSV split, đường dẫn job và cấu hình ASL. Không train GPU/Colab trong kiểm thử.
- Script kiểm tra cầu nối: `tests/check_workspace_notebook.py`, dùng notebook fixture được integration test ghi tại `.local-build/ai-workspace/generated-notebook.test.ipynb` khi đặt MOODIFY_TEST_ROOT.

## Đăng nhập chung và bản chạy local
Đăng nhập tại localhost:5170 với Admin/AIEngineer chuyển sang cùng hostname ở cổng 5174. Backend cấp cookie HttpOnly chuyển phiên dùng một lần; Admin web đổi cookie thành JWT và vào đúng dashboard, không nhập lại mật khẩu. Quyền vẫn kiểm tra ở server; AIEngineer không được quyền Users.

Lỗi yêu cầu đăng nhập lần hai được xác định do bản Release đang chạy chưa chứa `CanAccessWorkspace` (bản sửa trước chỉ build ở thư mục kiểm thử). Cần Stop rồi Start để runner build/nạp Release mới; chỉ refresh frontend hoặc Start khi stack đang chạy không cập nhật backend.

## Phân quyền cập nhật 09/10/2026
Quyền hiện tại thay cho phần mô tả Admin truy cập toàn bộ workspace ở trên: các API AiWorkspace và Models test/mapping/coverage dành riêng AIEngineer. Admin giữ registry/lifecycle, audit và báo cáo evaluation đọc. Xem WEEK_7_8_ROLE_REVIEW.md. Bản này đạt 454 assertion và browser smoke trên DB tạm.


## Cập nhật thống nhất song ngữ và recommender — 27/10/2026

Đọc mục này trước các hướng dẫn cũ theo ngày. Giữ sidebar theo role hiện có; các công cụ mới nằm trong tab tương ứng, không thêm một dãy tab mới.

| Tab | Thao tác mới | Ý nghĩa |
| --- | --- | --- |
| AI overview | Bilingual pipeline + Inference observations | Đúng model VI/EN, policy, latency/accepted theo ngày; không coi acceptance là accuracy |
| GoEmotions mapping | Bilingual VA policies ở phần riêng | Tạo candidate theo stable mood IDs, gate VI bổ sung, Approve bằng revision, Restore baseline |
| Datasets | Datasets, training and evaluation by model type | JSON review riêng VSMEC7 / VA regression / explicit-rating MF |
| Training jobs | Prepare a typed training job | Tải bundle chứa dataset/config/hash; chạy explicit offline, không giả trạng thái running là đã train |
| Evaluation | Submit report for review | Kiểm tra job/dataset/config/model/test count và metric theo task; không tự Activate |
| English VA / Vietnamese PhoBERT | Model lifecycle controls | Validate readiness, Activate từ bảng version, Deactivate, Rollback; CAS/restart |
| Vietnamese PhoBERT | Preprocessing status + SHA-256 | Cảnh báo fallback thiếu export; bundled vẫn cần kiểm chứng parity train |
| Feedback analytics / Training | Matrix Factorization registry | Import candidate, gate heldout, Activate/rollback/deactivate MF; cold-start fallback |
| Feedback analytics | Recommendation experiments | Contextual bandit/pilot, nhóm account ổn định, propensity và feedback aggregate; mặc định tắt |

### A. Policy đúng pipeline

1. Trong **GoEmotions mapping**, phần 28 nhãn phía trên chỉ điều khiển classifier GoEmotions cũ.
2. Mở **Create a reviewed policy candidate** trong Bilingual VA policies.
3. Chọn VI/EN và các mood ID còn tồn tại. VI minimum confidence chỉ siết thêm, không hạ threshold của model; EN phải 0 vì regression không có confidence phân loại.
4. Ghi lý do/bằng chứng review → **Save candidate** → **Approve**.
5. Revision cũ bị từ chối khi người khác vừa đổi. Refresh rồi rà soát; không submit mù để đè thay đổi.
6. Policy mới làm receipt cũ hết hợp lệ. App cần phân tích lại. **Restore baseline** cũng tăng version để receipt cũ không tự sống lại.

Neo VSMEC, tọa độ nội dung và vùng mood là các dữ liệu khác nhau. Không đổi policy để che giới hạn PhoBERT không biểu diễn được vùng tích cực/ít năng lượng.

### B. Dataset và training thực sự

JSON rows phải có ID duy nhất, `split=train|validation|test`, `reviewed=true` sau khi người phụ trách review. Tên nguồn và cách gán nhãn/bất đồng/split là bắt buộc.

- VSMEC7: `text/emotion`, `language=vi`; 7 nhãn đúng thứ tự artifact. Không đưa sao/tài khoản vào nhãn NLP.
- VA regression: `text/valence/arousal`, `language=en`, tọa độ hữu hạn [-1,1].
- MF: `userId/contentId/stars/timestamp`, `language=none`; split thời gian toàn cục, sao 1–5 thật.

Sau khi Register dataset → **Training jobs → Prepare a typed training job** → chọn dataset, version mới, base version local cho NLP, epochs. Tải bundle và chạy từ root project:

```powershell
.\ai-service\.venv\Scripts\python.exe ai-service\train_experiment.py `
  --bundle .local-build\typed-training-bundle.json `
  --base-dir '<thu-muc-model-base-local>' `
  --out .local-build\training-output-new
```

MF không cần `--base-dir`. Mặc định CPU; chỉ thêm `--device cuda` khi người vận hành chủ động chọn. Chạy Colab dùng cùng source/requirements, bundle và base artifact riêng trên Drive/máy train; không đưa credentials hoặc weights lên Git.

Runner kiểm tra hash, chạy optimizer thật, chọn checkpoint bằng validation, xuất model/tokenizer/preprocessing/threshold, dự đoán test và `report.json` kèm manifest. Nếu VI validation không có subset đủ bằng chứng, selective acceptance bị tắt cho candidate thay vì tạo threshold dễ dãi. Không chép threshold của candidate vào bản active.

Nhập `report.json` ở Training/Evaluation. Trạng thái `report_submitted_not_deployed` chỉ ghi nhận report; metric chưa được coi là đánh giá độc lập do server tự xác minh. Nhãn/source/permission phải được review. Sau nghiệm thu, đóng gói model mới rồi dùng workflow model thích hợp.

### C. MF và retrain

1. Chạy MF từ typed bundle hoặc `matrix_factorization.py` theo [protocol](AI_EVALUATION_PROTOCOL.md).
2. Import JSON trong **Matrix Factorization registry**, khai báo nguồn/review.
3. Server tính lại heldout RMSE, kiểm tra factors/IDs/thời gian và gate tối thiểu 20 warm validation + 20 warm test pair, không tệ hơn mean baseline.
4. Chưa đạt thì giữ candidate; không Activate. Trước Activate, mọi user/item latent phải map vào tài khoản/catalog hiện tại.
5. Hybrid dùng MF khi có score phù hợp; user/item mới hoặc chưa active quay về neighbor/content/mood. Version MF lưu trong recommendation/history.
6. **Use neighbor baseline** tắt MF, **Rollback MF** quay lại version trước bằng CAS.

Hồ sơ phản hồi được đọc lại ở request kế tiếp; model MF immutable được cache 5 phút theo version. Chuyển active version không tiếp tục dùng cache version cũ. Retrain hiện do kỹ sư chủ động tạo job từ snapshot mới; chưa có scheduler auto-retrain/auto-promote. Với snapshot DB hiện chỉ 7 rating, chưa có cơ sở bật MF production hoặc khẳng định tốt hơn baseline.

### D. Pilot phản hồi

Mặc định off. Chỉ mở sau khi có offline review, mục tiêu đo, rollback và phạm vi GVHD chấp thuận.

Context là mood ID, action là arm trọng số Hybrid, reward từ sao mới nhất; khi chưa có sao chỉ có tín hiệu mở link nhỏ. Chọn **Enable reviewed pilot**, traffic tối đa 50%, epsilon và bằng chứng review → Save. Phần còn lại là nhóm control ổn định theo account/experiment version.

Generated không phải seen/listened/completed. Xem cả số feedback và mean reward, không dùng số generated để tuyên bố người dùng hài lòng. Tắt bằng bỏ chọn Enable rồi Save; lịch sử experiment cũ vẫn được giữ trong aggregate/audit. Đây là contextual bandit, không phải RL nhiều bước.

### E. Giới hạn hiện tại

- [Preprocessing gốc thiếu](PHOBERT_PREPROCESSING_AUDIT.md); JSON từ hai CSV mới chưa được deploy.
- Quality gate NLP đã nối VI/VA Activate/rollback; còn thiếu gold/song ngữ mới đã review để tạo approval thực tế. Xem mục F.
- Chưa có dữ liệu/A-B đủ lớn để kết luận MF/Hybrid/bandit cải thiện trải nghiệm.
- Android emulator và Flutter Web đã chạy E2E VI/EN với model thật trên DB tạm; chưa đo profile/release/điện thoại thật.
- Tất cả thao tác thử ghi/activate/train trong audit dùng fixture, DB/marker tạm; không bật MF/pilot hoặc thay weights chính.

### F. Upload candidate → đánh giá độc lập → approval → Activate

Đây là workflow hiện tại, thay cho hướng dẫn upload-kích-hoạt ngay ở các mục cũ.

1. Mở **Vietnamese PhoBERT** hoặc **English VA** → Upload candidate. Chỉ lưu version mới, không tự Activate; version cũ không bị ghi đè.
2. Trong **Model promotion · reviewed quality evidence**, mở **Fix quality criteria...**, nhập tiêu chí/baseline/lý do rồi lưu policy bất biến **trước** tạo evaluation job. Số mẫu tối thiểu mặc định 50 validation và 50 test, có thể chọn theo protocol nhưng không thấp hơn 20 mỗi split. Ngưỡng form là ví dụ cần review.
3. Chuẩn bị/review nhãn bằng [công cụ và hướng dẫn](HUONG_DAN_GAN_NHAN_AI.md). Trong Datasets chọn đúng task, `purpose=evaluation`, xác nhận độc lập. Tập này không chứa train; cung cấp emotion cho VI, VA cho EN; VI có thể thêm gold VA/mood riêng.
4. Trong Training jobs chọn dataset evaluation, policy đã lưu và **modelVersion đúng version candidate đã upload**. Tải bundle JSON.
5. Chạy evaluator từ root với đúng source hiện tại và artifact candidate tải/cấp riêng:

```powershell
.\ai-service\.venv\Scripts\python.exe ai-service\train_experiment.py `
  --bundle .local-build\evaluation-bundle.json `
  --base-dir '<thu-muc-candidate-local>' `
  --out .local-build\evaluation-candidate
```

Dataset purpose quyết định chạy evaluation-only; tên lệnh được giữ chung với workflow training. VI có thể trỏ folder bundle hoặc folder chứa `config.json`/weights; VA cần một `.pt` và `tokenizer/`. CPU mặc định, `--device cuda` phải do người vận hành chọn. Output không thay weights/threshold và không Activate. Hash code adapter phải khớp source serving; thay source cần đánh giá lại.

6. Nhập `report.json` ở Evaluation; xem `validation-evaluation.json`, `test-evaluation.json`, hai file dự đoán, baseline và giới hạn.
7. Quay về tab model → **Review and approve an evaluated candidate**. Chọn job, xác nhận review độc lập, ghi người review/nơi lưu evidence/bất đồng/quyết định. Server kiểm tra số mẫu/metrics/policy/fingerprint.
8. Sau khi approval pass mới **Activate** từ bảng version. Rollback cũng kiểm tra approval hoặc baseline đã ghi nhận. Runtime không sẵn sàng/fingerprint thay đổi thì dừng, giữ active cũ.

Server kiểm tra báo cáo do kỹ sư nộp; không tự chứng minh nhãn đúng/độc lập hoặc tính lại toàn bộ metric. Không đưa fixture kiểm thử vào approval chính. Model đang phục vụ từ trước vẫn có thể giữ làm baseline vận hành, hiển thị `existing_serving_baseline_not_quality_validated`; không là chất lượng độc lập đã đạt.

Nếu mood geometry/policy hoặc weights/tokenizer/preprocessing/code adapter đổi, approval cũ có thể hết hợp lệ; tạo evaluation job mới. Sidecar pin identity bảo vệ restart sau Activate. Hai model chính chưa bị thay artifact hoặc dữ liệu.

API mới AIEngineer-only: `GET /api/ModelQuality`, `POST /api/ModelQuality/policies`, `POST /api/ModelQuality/approvals`. Metadata lưu `aiNlpQualityPolicies` / `aiNlpQualityApprovals`; không đưa gold riêng tư vào Git. RL/bandit hiện không bắt buộc, pilot tiếp tục off.


### Mapping context-mapping-v2 — 30/10/2026

API Mobile/Web hiện kết hợp quy tắc ngữ cảnh với model để chọn mood; không dùng nearest-center cho nhãn ngữ nghĩa chi tiết. `mappingMethod` nói rõ rule/model fallback; `mappingVersion` gồm policy/mapper/method. Raw V/A được giữ riêng, target gợi ý được giới hạn trong vùng mood. Không coi thay mapper là model được retrain hoặc đạt quality gate. [Chi tiết và kiểm thử](AI_MOOD_MAPPING_FIX_2026-10-30.md).


### Rà toàn luồng context-mapping-v3 — 30/10/2026

Đã sửa phủ định để sử dụng model evidence phù hợp thay vì chặn toàn bộ; không nhận mood bị phủ định và không tự lấy cảm xúc đối lập. Đã rà 136 câu, 1.000 assertion backend và browser roles/policy. Các ca còn confidence thấp giữ lựa chọn manual; không thay model/threshold/quality approval. [Ma trận và kết quả chi tiết](AI_FLOW_AUDIT_2026-10-30.md).


### Đầu vào context-mapping-v4 — 30/10/2026

Bilingual serving hiện chọn nguyên văn các mệnh đề cảm xúc hiện tại đủ rõ trước một lần inference khi yêu cầu nội dung hoặc bối cảnh khác gây nhiễu. Full-text guards vẫn kiểm tra mọi quyết định. Response có `inputScope`/`analyzedCharacterCount`; receipt chỉ lưu scope, không lưu câu gốc. Validator receipt cũ được mở thêm đúng trường optional. Threshold và chất lượng artifact giữ nguyên.

174 ca model thật có 93/98 mood kỳ vọng được nhận/khớp, 5 ca tự chọn; đây không phải đánh giá gold độc lập. [Thiết kế, kiểm thử và giới hạn](AI_INPUT_CONTEXT_OPTIMIZATION_2026-10-30.md).


### Mobile serving context v5 / routing v2 — 31/10/2026

Serving phân biệt chủ thể/thời điểm và nội dung được đề cập ở mức từng cảm xúc; input vẫn là nguyên văn, full-text guards và ngưỡng model còn nguyên. Câu Anh có café/tên địa danh Việt được route dựa trên bằng chứng từ vựng. Mobile có hủy HTTP, chống phản hồi cũ và phục hồi receipt hết hạn. Không thay workflow training/quality approval/model registry.

256 ví dụ model thật có 141/146 mood kỳ vọng được nhận/khớp và 0/98 guard violation; năm câu còn giữ tự chọn. Đây là hồi quy pipeline, không phải bằng chứng model được huấn luyện tốt hơn. [Kết quả và giới hạn](MOBILE_AI_HARDENING_2026-10-31.md).
