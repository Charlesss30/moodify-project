# Tích hợp nhánh Phuong và Khang — 03/11/2026

## Phạm vi và kết quả

Tích hợp `origin/Khang` (`37c73f7`) vào nền `dev/Phuong` (`034465b`). Hai nhánh có chung nền `6fc37c9`; Khang bắt đầu từ phiên bản cũ hơn các cập nhật mobile và kiểm soát AI của Phuong. Merge có 33 file xung đột. Bản tích hợp giữ các chức năng mới trên dev, rồi đưa bộ model, notebook và metadata của Khang vào bằng adapter riêng.

Có nhánh dự phòng `backup/dev-before-khang-20261102`. Không restore MongoDB, không thay tài khoản, không đổi model active hoặc cấp approval trên database chính. Chưa push remote.

## Những phần được giữ

- Giao diện mobile hiện tại, nút Khám phá ở giữa, tài khoản, thống kê, lịch sử, thư viện, Spotify/YouTube, đánh giá, splash/icon và thông báo hệ thống Android.
- Mobile chống gửi trùng và bỏ kết quả cũ khi người dùng sửa mô tả/chuyển tác vụ.
- Backend tự chọn Việt/Anh, xử lý ngữ cảnh cảm xúc hiện tại, phủ định, cảm xúc người khác, câu hỏi/mong muốn và mô tả dài.
- Server cấp `analysisId` gắn người dùng, hết hạn sau 5 phút. Xác nhận dùng kết quả lưu trên server; không nhận V/A hay phiên bản model do client tự khai.
- Phân biệt V/A từ model và V/A mục tiêu dùng để gợi ý; lưu đúng provenance vào lịch sử. Người dùng vẫn được chọn mood thủ công.
- Dashboard phân quyền, policy, quality approval, lifecycle activate/deactivate/rollback, audit, các công cụ dataset/evaluation/training và recommender/MF.
- Runner xác minh PID, thời điểm tạo tiến trình và worker giữ cổng trước khi dừng; không dừng tiến trình khác chỉ vì dùng lại PID.

## Những phần nhận từ Khang và cách tích hợp

| Thành phần | Xử lý cuối cùng |
|---|---|
| Notebook EN/VI | Giữ hai notebook, manifest, log và báo cáo train đi kèm. Không chạy lại full training. |
| RoBERTa-base 6 lớp EN | Nạp qua `EnglishEmotionPredictor`, vẫn dùng `/predict-va` để tương thích gateway. |
| PhoBERT-base-v2 6 lớp VI | Nạp qua `VietnameseSixEmotionPredictor`, dùng `/predict-vi`. |
| Predictor cũ | Giữ nguyên file của model hồi quy EN và VSMEC 7 lớp VI, tránh làm mất identity/approval của model đang dùng. |
| Tiền xử lý VI | Đọc đúng export emoji/teencode và cờ phân đoạn từ, giữ thứ tự xử lý của notebook Khang. |
| V/A | Tính bằng tổng xác suất × anchor trong `label_config.json`. Đây là V/A từ adapter, không phải mô hình hồi quy đã học trực tiếp V/A. |
| Taxonomy | Tách `moodify-six-v1` khỏi `vsmec-seven-v1` và GoEmotions 28 nhãn. Không gộp nhãn hoặc tính nhầm mẫu số macro-F1. |
| Confidence | Không lấy accuracy trong báo cáo train để làm threshold. Không tự điền ngưỡng cho candidate chưa hiệu chỉnh. |
| Fingerprint | Bao gồm weights, tokenizer/config, nhãn/anchor, manifest, preprocessing, confidence và mã adapter. Đổi asset phục vụ sẽ cần đánh giá lại. |
| Upload | ZIP/RAR nhận đúng cấu trúc 6 lớp, giữ metadata và cấu hình confidence; lưu candidate chờ quality, không tự activate. Phiên bản phải khớp manifest. |
| Dataset/training | Thêm schema `emotion6_en` / `emotion6_vi`, tạo bundle để fine-tune/evaluate bên ngoài. Job không tự khởi chạy huấn luyện. |
| Đánh giá/approval | Classifier EN dùng macro-F1/coverage/selective accuracy; model EN hồi quy vẫn dùng MAE/RMSE. Approval phải khớp task, taxonomy và toàn bộ bundle. |
| File nặng | Hai weights dùng Git LFS. Metadata/tokenizer dùng Git thường. Bỏ bản `output.rar` trùng trong cây merge; giữ nguyên lịch sử Git của Khang. |

Sáu nhãn đúng thứ tự: `Sadness`, `Enjoyment`, `Love`, `Anger`, `Fear`, `Surprise`. EN dùng tối đa 64 token, VI 96 token theo export. Từ input của app tối đa 500 ký tự, backend chọn phần mô tả cảm xúc phù hợp trước khi gọi model; giới hạn token vẫn có thể cắt bớt văn bản.

## Hợp đồng public/private

Private inference có thể trả `accepted: null` khi candidate chưa có confidence threshold. Public API giữ `accepted` là boolean để tương thích mobile, đồng thời bổ sung:

- `modelType`: loại mô hình thực tế, không gắn nhãn hồi quy cho classifier EN.
- `taxonomy`: tập nhãn đang phục vụ.
- `modelAccepted`: quyết định confidence riêng của model; có thể `null`.
- `calibrationStatus`: `uncalibrated` hoặc trạng thái cấu hình đã có threshold.

Public `accepted=true` nghĩa là đề xuất qua kiểm tra ngữ cảnh có thể được người dùng xác nhận. Nó không có nghĩa đã đo được độ chính xác hoặc đã hiệu chỉnh confidence. Kết quả luôn cần xác nhận; thiếu receipt hoặc sai taxonomy/label thì mobile từ chối response. Candidate bị model từ chối không được cấp receipt. Model đổi phiên bản hoặc mood/policy đổi thì phải phân tích lại.

Không biến `Love` thành Lãng mạn chỉ nhờ tọa độ dương; không biến giận/sợ thành Vui/Hào hứng chỉ vì gần một mood trong catalog. Khi danh mục hiện có không biểu diễn được cảm xúc hoặc ngữ cảnh/model mâu thuẫn, trả chưa xác định và cho chọn thủ công.

## Artifact và tải sau khi pull

```powershell
git lfs install
git lfs pull
```

| Ngôn ngữ | Thư mục candidate | SHA-256 weights |
|---|---|---|
| EN | `code-train/train-KLCN/output/roberta_base_moodify_6c_20261101_051915` | `b28f6902afe7522b42a6ed1c1608a3337a1d085c6905ace96b8d2bf8019070b8` |
| VI | `code-train/train-KLCN/output/vietnamese-emotion/phobert_moodify_6c_20261101_053022` | `9902ea9983e3df7939861454ddc9f27963eafbb45ec1582bd7d8b02be99b3790` |

EN cần `best_model/` và root `label_config.json`, `manifest.json`. VI cần `model/` và root `label_config.json`, `preprocessing.json`, `manifest.json`. Nếu có threshold đã được hiệu chỉnh, đặt `confidence_threshold.json` ở root và đánh giá lại fingerprint. Không nén thêm output cũ, checkpoint thừa hoặc cả dataset. Phiên bản nhập trên dashboard phải đúng `model_version` trong manifest.

Các artifact cũ và dữ liệu train khác vẫn cấp riêng/ignore. `.gitignore` chỉ mở hai bundle mới; database backup, private reviews, khóa Firebase/SMTP và các cache vẫn giữ nguyên quy tắc hiện tại.

## Vận hành và chuyển model

```powershell
.\Start.ps1
.\scripts\moodify-local.ps1 -Action Status
.\Stop.ps1
```

Nếu không dùng classifier GoEmotions 28 nhãn riêng, có thể giảm phần bộ nhớ đó bằng:

```powershell
.\Start.ps1 -WithoutLegacyClassifier
```

Cờ này giữ hai pipeline EN/VI. Mặc định runner giữ hành vi cũ. `Status` báo riêng legacy, English và Vietnamese; một runtime bị disable không đồng nghĩa hai runtime còn lại đều hỏng.

Runner đọc lifecycle state trước marker cũ. State đã deactivate được tôn trọng; không tự hồi sinh model bằng fallback. Nếu chỉ clone hai bundle mới, service có thể mở catalog khi chưa active. Runtime này chưa có nghĩa app đã được phép phân tích bằng candidate mới.

Để dùng model mới lâu dài:

1. AIEngineer xem English models hoặc Vietnamese PhoBERT, xác nhận candidate được liệt kê đúng loại.
2. Đăng ký dataset độc lập đã review, chọn đúng `emotion6_en` hoặc `emotion6_vi`. Không dùng output model làm nhãn chuẩn.
3. Tạo quality policy cho đúng task trước khi đánh giá. Tách validation/calibration và test; không chọn threshold dựa trên test cuối.
4. Tạo job evaluation, tải bundle, chạy `ai-service/evaluate_candidate.py` theo hướng dẫn trong bundle; nhập report đầy đủ và review artifact identity.
5. Approve rồi Activate. Có thể deactivate/rollback qua lifecycle; phiên bản quay lại cũng phải thỏa kiểm tra identity/approval tương ứng.

Hai model mới chưa được tự cấp quality approval hay thay thế model đang active. Không gọi thẳng endpoint nội bộ để lách bước review.

## Kiểm chứng

| Kiểm tra | Kết quả |
|---|---|
| .NET build | Pass, 0 lỗi / 0 cảnh báo |
| Backend integration trên MongoDB tạm | 1.111 assertion pass, gồm upload ZIP, phân quyền, receipt, lifecycle và quality task/taxonomy |
| Python | 43 test pass, gồm metadata/hash/threshold, discovery hai định dạng, train-export-load fixture, lifecycle |
| Flutter analyze | Không có issue |
| Flutter test | 111 test pass |
| Admin Vite build | Pass |
| PowerShell runner | 19 assertion identity/lifecycle/shutdown pass |
| Real-model gateway → recommendation/history | 201 assertion, 26 mô tả, EN/VI CUDA; pass về hợp đồng và dữ liệu luồng |

Smoke inference thật chạy hai weights trên CUDA trong process riêng, backend riêng và database tạm có bản sao catalog. Đã dừng các process smoke và xóa database tạm sau khi chạy. Không đánh giá E2E UI trên Android thật trong lượt merge này.

Bằng chứng đầy đủ của 26 mô tả ở `.local-build/khang-six-real-smoke.json`; log test cũng nằm trong `.local-build/` (không đưa lên Git). Lần đo cục bộ: cold startup khoảng 12,2 giây; 4 yêu cầu burst nhỏ đều HTTP 200, khoảng 16–59 ms. Đây là số đo trên máy phát triển, không phải cam kết hiệu năng thiết bị hoặc kiểm thử tải sản xuất.

Kết quả 26 mô tả: 15 matched, 10 unknown và 1 ngôn ngữ không hỗ trợ. Các unknown có cả cảm xúc ngoài danh mục, mâu thuẫn/ngữ cảnh chưa rõ và lỗi phân loại thực tế; không tính chúng thành độ chính xác. Ví dụ model VI dự đoán một số câu thành Anger/Fear và hệ thống abstain; câu hoài niệm trong smoke được model gợi ý Sad. Vì vậy chưa có bằng chứng hai candidate mới tốt hơn model active hiện tại.

Các test train/export chỉ dùng model ngẫu nhiên cực nhỏ và dữ liệu synthetic để kiểm tra phần mềm. Không dùng kết quả đó làm số liệu chất lượng AI hoặc triển khai vào app.

Lệnh tái hiện chính (trong repo, đã có virtualenv/dependencies):

```powershell
dotnet build tests/Moodify.Integration.csproj
dotnet tests/bin/Debug/net10.0/Moodify.Integration.dll
dotnet tests/bin/Debug/net10.0/Moodify.Integration.dll --six-class-real
.\ai-service\.venv\Scripts\python.exe -m unittest discover -s ai-service -p 'test_*.py' -q
powershell -NoProfile -File tests/LocalRunner.Tests.ps1
npm --prefix admin-web run build
Push-Location mobile-app
flutter analyze --no-pub
flutter test --no-pub
Pop-Location
```

Lệnh `--six-class-real` cần MongoDB local có catalog `mood_recommendation`; chỉ đọc catalog đó và ghi vào database tạm. Các test Python không cần GPU. Test Flutter dùng `PUB_CACHE` hiện có trong `D:\hoc-tap\moodify-pub-cache`.

## Giới hạn cần ghi đúng trong báo cáo khóa luận

- Đây là tích hợp phân loại cảm xúc và gợi ý nội dung, không bổ sung chatbot/LLM hoặc vượt phạm vi đề cương.
- Accuracy/F1 trong bundle là số Khang ghi từ lượt train: EN 93,25% / 90,17%; VI translated 85,70% / 81,08%; VI VSMEC 5 lớp 79,40% / 64,18%. Chưa chạy lại trên tập độc lập Moodify và chưa được dùng làm quality approval.
- Sáu nhãn không có lớp riêng cho Thư giãn, Tập trung, Hoài niệm. Semantic context giữ khả năng chọn mood theo mô tả trực tiếp; không suy ra model được huấn luyện các nhãn ấy.
- Anchor V/A là cấu hình thiết kế. Không trình bày như phép đo tâm lý đã kiểm chứng.
- Cần dữ liệu gán nhãn độc lập và calibration để quyết định có thay model active hay không. Bộ test phần mềm pass không thay được bước này.
