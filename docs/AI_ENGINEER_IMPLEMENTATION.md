# AI Engineer workspace — 08/10/2026

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
