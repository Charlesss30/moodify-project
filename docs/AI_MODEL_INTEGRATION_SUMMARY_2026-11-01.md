# Tổng hợp tích hợp AI Moodify và các bước tiếp theo

Ngày tổng hợp: 01/11/2026

## 1. Mục tiêu của lần cập nhật

Đưa hai model cảm xúc vừa train vào luồng Moodify:

- Tiếng Anh: `roberta-base`, 6 nhãn.
- Tiếng Việt: `vinai/phobert-base-v2`, 6 nhãn.
- Cả hai model dự đoán phân bố xác suất trên nhãn cảm xúc. Valence/Arousal (V/A) được tính từ xác suất và anchors trong `label_config.json`; model không dự đoán V/A bằng regression ở luồng mới.
- Backend chọn tâm trạng Moodify gần tọa độ V/A nhất. Người dùng xem lại và xác nhận tâm trạng trước khi lấy gợi ý.

## 2. Kết quả train đã đọc từ artifacts

| Ngôn ngữ | Version | Tập đánh giá | Accuracy | Macro-F1 | Ghi chú |
|---|---|---|---:|---:|---|
| English | `roberta_base_moodify_6c_20261101_051915` | `dair-ai/emotion` test, n=2.000 | 93,25% | 90,17% | Cùng phân phối với dữ liệu train; chưa phải đánh giá domain Moodify độc lập. |
| Vietnamese | `phobert_moodify_6c_20261101_053022` | Test dịch máy, n=2.000 | 85,70% | 81,08% | Chỉ là kiểm tra tính nhất quán với dữ liệu dịch. |
| Vietnamese | Cùng version trên | UIT-VSMEC 5 lớp, n=432 | 79,40% | 64,18% | Tập tiếng Việt thật; chưa đạt mục tiêu accuracy 85%. |

> Không có human test set và không có confidence threshold được xuất trong hai output. Vì vậy API trả `accepted: null`; không nên tự đặt ngưỡng từ test accuracy. Anchor cho nhãn `Love` cũng cần được kiểm chứng trước khi coi V/A là thang đo đã hiệu chuẩn.

## 3. Những gì đã sửa

### AI service và artifacts

- Cài artifacts cùng cấu hình nhãn, anchors, preprocessing, manifest và báo cáo vào `code-train/train-KLCN/output/`. Thư mục output đang bị Git ignore; weights không được thêm vào source control.
- Cập nhật predictor tiếng Anh để `/predict-va` hỗ trợ classifier sáu lớp và giữ khả năng đọc checkpoint `.pt` regression cũ.
- Cập nhật predictor tiếng Việt để đọc `label_config.json` và `preprocessing.json`, xác thực thứ tự nhãn và SHA-256, rồi dùng pipeline normalize/teencode/emoji/PyVi tương ứng với notebook.
- Cập nhật model discovery, readiness, activation và response để giữ thông tin emotion, confidence, candidates, V/A, language và version.

### Backend, Mobile, Web và Admin

- Backend chuyển tiếp kết quả sáu lớp cho cả tiếng Anh và tiếng Việt; vẫn giữ đường classifier GoEmotions 28 nhãn cũ.
- Upload Admin hỗ trợ bundle classifier mới và bundle VA `.pt` cũ. Với classifier mới, backend xác thực manifest, ngôn ngữ và version trước khi cài candidate.
- Mobile nhận sáu nhãn, hiển thị confidence/candidates và yêu cầu kiểm tra, xác nhận khi chưa có ngưỡng tin cậy.
- Web/Admin cập nhật nội dung và form để phản ánh classifier mới; version mẫu khớp artifacts hiện tại.
- Runner local chọn candidate tiếng Anh mới theo cấu hình; candidate PhoBERT được lưu riêng và cần activate tường minh.

### Mapping và lịch sử

Không đổi collection/migration `aiEmotionMappings` 28 nhãn. Qua đối chiếu code, mapping này thuộc endpoint GoEmotions legacy. Hai model mới đi qua luồng `/valence-arousal`, tính V/A từ anchors rồi tìm mood gần nhất; ép mapping 28 nhãn thành sáu lớp có thể làm hỏng luồng cũ.

## 4. Kiểm tra đã thực hiện

- `node --check` cho JavaScript Web và Admin: đạt.
- `python -m py_compile` cho các file Python chính và test AI mới: đạt. Đây chỉ là kiểm tra cú pháp, không phải inference.
- SHA-256 của hai file weights khớp với manifest:
  - English: `b28f6902afe7522b42a6ed1c1608a3337a1d085c6905ace96b8d2bf8019070b8`
  - Vietnamese: `9902ea9983e3df7939861454ddc9f27963eafbb45ec1582bd7d8b02be99b3790`
- `dotnet build` chưa tới bước compile vì NuGet restore lỗi `NU1301`: môi trường không phân giải được `api.nuget.org`.
- Flutter test và `dart format` không trả output trong 45 giây; đã dừng. `mobile-app/.dart_tool/package_config.json` chưa có nên test Mobile chưa được xác minh.
- Chưa chạy inference thật vì Python environment hiện thiếu framework/dependencies cần thiết. Chưa activate model Việt trong runtime.

## 5. Bạn nên làm gì tiếp theo

### Bước 1 — Chuẩn bị môi trường dev

1. Mở project và dùng đúng Python virtualenv của AI service.
2. Cài dependencies theo file requirements/README của `ai-service`, gồm framework model, tokenizer và dependencies tiền xử lý tiếng Việt (`pyvi`, `sentencepiece`).
3. Chạy `flutter pub get` trong `mobile-app` để tạo package config.
4. Khi có mạng hoặc NuGet cache phù hợp, chạy restore/build backend.

### Bước 2 — Chạy kiểm tra tích hợp

1. Chạy test AI service và unit tests trong `ai-service`.
2. Chạy Flutter test, tối thiểu `test/bilingual_mood_flow_test.dart`.
3. Build backend và kiểm tra các route readiness:
   - English: `/health/va/ready`
   - Vietnamese: `/health/vi/ready`
4. Gửi câu tiếng Anh/Việt đại diện qua endpoint phân tích; kiểm tra nhãn, top candidates, V/A nằm trong `[-1, 1]`, đúng model version và đúng language.
5. Kiểm tra luồng khi `accepted` là null: UI phải yêu cầu người dùng xác nhận trước khi lấy gợi ý.

### Bước 3 — Đánh giá chất lượng trước khi dùng rộng

1. Tạo tập validation/calibration riêng cho từng ngôn ngữ; không dùng lại test split đã báo để chọn threshold.
2. Thu thập thêm câu tiếng Việt tự nhiên trong domain Moodify, có consent và loại bỏ thông tin định danh. Gán nhãn bằng hướng dẫn rõ ràng, lưu bất đồng và tách train/dev/test theo người viết hoặc phiên thu thập nếu có thể.
3. Xem confusion matrix, per-class precision/recall/F1, calibration và các câu bị nhầm. Tập VSMEC hiện tại cho thấy Vietnamese model chưa đạt mục tiêu 85% accuracy.
4. Kiểm tra license/điều khoản của datasets, model base và bản dịch máy trước khi phân phối.
5. Chỉ sau khi chọn threshold từ calibration set và đánh giá chấp nhận được, mới cấu hình `EN_CONFIDENCE_THRESHOLD` / `VI_CONFIDENCE_THRESHOLD`.

### Bước 4 — Đưa model vào runtime

1. Nếu upload từ Admin, ZIP cần chứa cấu trúc bundle được mô tả trên trang upload; version nhập vào phải khớp `model_version` trong `manifest.json`.
2. Candidate tiếng Anh hiện được runner chọn mặc định nếu không có active marker khác.
3. Activate candidate PhoBERT từ AIEngineer → Vietnamese PhoBERT; xác nhận readiness và một số câu kiểm tra trước khi cho user sử dụng.
4. Giữ lại phiên bản trước để rollback. Không xóa candidate cũ cho tới khi bản mới qua smoke test và kiểm tra trên staging.

## 6. Tài liệu liên quan

- [AI handoff và chi tiết code](AI_MODEL_HANDOFF_2026-11-01.md)
- [Audit dữ liệu train và kết quả](AI_TRAINING_AUDIT.md)
- [Hướng dẫn luồng AI và chọn dataset](../moodify-ai-flow-and-dataset-guide.md)
- [Test cases model](AI_MODEL_TEST_CASES.md)
