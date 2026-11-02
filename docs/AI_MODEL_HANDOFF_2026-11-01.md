# Tích hợp hai model cảm xúc mới — 01/11/2026

> Cập nhật sau tích hợp Phuong/Khang ngày 03/11/2026: đây là ghi nhận bàn giao từ nhánh Khang. Hành vi cuối cùng xem [báo cáo tích hợp](KHANG_PHUONG_INTEGRATION_2026-11-03.md). Giữ model cũ, semantic context, receipt, quality gate và rollback; hai model mới là candidate, không tự thay model active. Weights dùng Git LFS; metadata/tokenizer dùng Git thường. Private inference `accepted=null` được chuyển thành `modelAccepted=null` ở API công khai; `accepted` công khai vẫn là bool và luôn cần xác nhận.


## Nội dung yêu cầu trong file bàn giao

- Đọc output Colab và lấy version, metrics, ngưỡng từ artifact; không tự bịa số.
- Đổi hai nhánh song ngữ thành classifier 6 lớp; giữ route/response tương thích backend.
- Đọc labels và V/A anchors từ `label_config.json`, dùng đúng preprocessing tiếng Việt.
- Bảo toàn classifier GoEmotions 28 nhãn cũ, history, fallback và mapping legacy.
- Cập nhật runner/client/docs và ghi tách riêng tập test, license, provenance; không dùng smoke list làm accuracy.

## Đã tìm thấy trong output_2_language

| Model | Artifact version | Tập test | Accuracy | Macro-F1 |
|---|---|---|---:|---:|
| English `roberta-base` | `roberta_base_moodify_6c_20261101_051915` | `dair-ai/emotion` split test, n=2.000; cùng phân phối train | 93,25% | 90,17% |
| Vietnamese `vinai/phobert-base-v2` | `phobert_moodify_6c_20261101_053022` | `test_mt`, n=2.000; dịch máy | 85,70% | 81,08% |
| Vietnamese cùng model | như trên | UIT-VSMEC 5 lớp, n=432 | 79,40% | 64,18% |

Không có human test set và không có confidence threshold trong outputs. Test tiếng Việt thật dưới mục tiêu 85%, nên docs giữ đúng số này; chưa hiệu chỉnh threshold. API `accepted` là null cho tới khi threshold được chọn trên calibration data phù hợp. V/A là xác suất nhân anchors, không phải output regression. Anchor Love=(0.8,0.3) là giả thuyết từ notebook, chưa được kiểm chứng.

## Thay đổi đã thực hiện

- Chép artifact, cấu hình nhãn, preprocessing, log/metrics và manifest vào `code-train/train-KLCN/output/` (đã gitignore); tính/ghi SHA-256 weights. Không đưa dữ liệu huấn luyện thô vào project.
- Viết lại `vietnamese_predictor.py` để tải taxonomy/anchors/length/teencode/emoji từ candidate, kiểm tra nhãn/hashes và dùng đúng pipeline preprocess notebook.
- Mở rộng `va_predictor.py` để `/predict-va` phục vụ classifier English mới + anchors; giữ hỗ trợ candidate `.pt` VA regression cũ. Cập nhật `main.py` discovery/activation cho cả hai package.
- C# English response giờ chuyển tiếp emotion/confidence/accepted/candidates mà vẫn giữ V/A và modelVersion; `accepted` nhánh Việt nullable để chưa hiệu chỉnh ngưỡng không bị coi là false.
- Admin ZIP upload nhận package sáu lớp; backend yêu cầu manifest hợp lệ và version/language trùng form trước khi cài candidate. UI dùng đúng version của hai artifact hiện tại.
- Mobile parser/UI nhận sáu nhãn mới và thông điệp yêu cầu xác nhận khi threshold còn trống. Web hiển thị top emotion candidates cho cả hai language.
- Local runner nạp English candidate mới mặc định nếu không có active marker khác. Vietnamese candidate được lưu như candidate và chỉ nạp sau thao tác Activate; không tự ghi đè marker.
- Cập nhật README, AI inference guide, audit addendum và test cases. Tạo test unit cho preprocessing/schema/anchor V/A.

## Phần handoff không áp dụng nguyên văn sau khi đối chiếu code

`aiEmotionMappings` và `MoodAnalysisMapping.cs` hiện bắt buộc khai báo đúng 28 nhãn GoEmotions; `MoodAnalysisService` dùng mapping đó cho endpoint classifier legacy. Hai model mới đi qua `valence-arousal`, trả V/A từ anchors rồi chọn mood nearest-neighbor; chúng không đọc mapping 28 nhãn này. Do đó không thay mapping collection hoặc migration DB thành sáu nhãn. `Love` được xử lý qua anchor V/A; lịch sử cũ không bị xóa.

## Kiểm tra đã chạy và giới hạn

- Python bytecode compile: pass.
- `node --check backend/wwwroot/app.js`: pass.
- Kiểm tra SHA-256 manifest so với copied weights: pass khi tạo candidate.
- `dotnet build tests/Moodify.Integration.csproj`: không chạy đến compile do restore bị chặn tại NuGet (`NU1301`, không resolve `api.nuget.org`).
- AI Python tests chưa chạy vì environment không cài `torch`/`transformers`/`fastapi`; chỉ các test cũ không phụ thuộc model đã chạy được. Không có inference thật/torch model load trong lượt này.
- `flutter test --no-pub --reporter expanded test/bilingual_mood_flow_test.dart` và `dart format` không trả output sau 45 giây; đã dừng. `mobile-app/.dart_tool/package_config.json` chưa có nên Flutter tests chưa được xác minh.
- Không chạy training, không sửa MongoDB, không activate PhoBERT và không ghi đè user marker.

## Việc tiếp theo

1. Tạo calibration set riêng để chọn EN/VI confidence threshold; không dùng test split đã báo ở trên.
2. Nâng VI real-text performance bằng pipeline/rubric/data review theo lựa chọn của người dùng; không tự sửa PER_CLASS_TARGET hoặc đổi model base.
3. Kiểm tra quyền dataset, PhoBERT/RoBERTa, dịch máy và artifact trước khi phân phối.
4. Cài đúng Python env, chạy service readiness/inference thật, hoàn tất build/test backend và Flutter; sau đó người vận hành có thể Activate model Việt từ AIEngineer workspace.
