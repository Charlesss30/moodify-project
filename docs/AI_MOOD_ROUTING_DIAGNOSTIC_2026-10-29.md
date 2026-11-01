# Chẩn đoán kết quả tâm trạng Việt/Anh — 29/10/2026

> Cập nhật mới nhất: [Tối ưu đầu vào và context-mapping-v4](AI_INPUT_CONTEXT_OPTIMIZATION_2026-10-30.md). Các số đo bên dưới là snapshot của lượt trước.

> **Bản cập nhật v3 — 30/10/2026:** Đã sửa xử lý phủ định và rà 136 câu/nhóm luồng. [Kết quả mới, phạm vi và giới hạn](AI_FLOW_AUDIT_2026-10-30.md). Các số liệu bên dưới là snapshot trước v3.

> **Cập nhật 30/10/2026:** Luồng chọn mood đã được sửa bằng `context-mapping-v2`. [Chi tiết triển khai và kiểm thử sau sửa](AI_MOOD_MAPPING_FIX_2026-10-30.md). Nội dung bên dưới giữ lại snapshot trước sửa.

## Kết luận

**Có lệch ngữ nghĩa ở cả hai pipeline đang active.** Định tuyến và truyền tọa độ đúng chưa chứng minh nhãn tâm trạng đúng với câu nhập.

Đã chạy **40 câu: 20 tiếng Việt và 20 tiếng Anh**, qua đúng `POST /api/MoodAnalysis/valence-arousal`, `language=auto`, giống endpoint Mobile. Dùng model thật đang phục vụ; backend HTTP/account/receipt/metrics trong DB tạm; moods/policy sao chép chỉ đọc từ DB chính.

Thời điểm kết quả: **29/10/2026 22:13 (UTC+7)**.

| Phần kiểm tra | Kết quả |
| --- | --- |
| Model Việt | `phobert_vsmec_v2`, CUDA |
| Model Anh | `roberta_va_regression_v2`, CUDA |
| Định tuyến đúng model/ngôn ngữ | 40/40 |
| V/A thô và API | Khớp trong sai số 10⁻⁶ |
| Assertion kỹ thuật | 213 pass |
| Cùng câu lặp ba lần mỗi ngôn ngữ | Tọa độ ổn định trong sai số 10⁻⁶ |
| Flutter bilingual/API/filter | 14 tests pass |
| Mood khớp kỳ vọng VI | 10/17 |
| Mood khớp kỳ vọng EN | 4/17 |

**20/34 câu có kỳ vọng mood chưa khớp**, bao gồm câu bị từ chối vì chưa đủ tin cậy. Sáu câu giận/lo sợ/pha trộn xem riêng, không tính vào mẫu số mood. Đây là ví dụ chẩn đoán tự soạn, **không phải gold độc lập hoặc accuracy tổng quát**.

Câu chính xác **`hôm nay tao vui` hiện trả Happy/Vui vẻ**, chưa tái hiện chính câu đó ra Excited/Hào hứng. Tuy nhiên, đã xác minh nhiều lệch khác ở cả hai ngôn ngữ; không kết luận phản ánh của người dùng là sai.

## Ví dụ rõ nhất

| Câu | Đầu ra model | Mood API |
| --- | --- | --- |
| hôm nay tao vui | VI: Enjoyment; V=0,670, A=0,648 | Vui vẻ |
| I am happy today. | EN: V=0,519, A=0,256 | Lãng mạn |
| Tôi cực kỳ hào hứng, tràn đầy năng lượng và mong chờ buổi hòa nhạc tối nay! | VI: Enjoyment; V=0,678, A=0,653 | Vui vẻ |
| I am extremely excited, full of energy and cannot wait for tonight's concert! | EN: V=0,349, A=0,590 | Tập trung |
| Tôi thấy bình yên và thư giãn khi ngồi uống trà. | VI: Enjoyment; V=0,630, A=0,614 | Vui vẻ |
| I feel peaceful and relaxed while sitting with a cup of tea. | EN: V=0,213, A=0,022 | Hoài niệm |
| Tôi rất tức giận vì bị đối xử bất công. | VI: Sadness; V=-0,703, A=-0,345 | Buồn |
| I am in love and feel romantic when I think about my partner. | EN: V=0,424, A=0,374 | Tập trung |

## Nguyên nhân đã xác minh

### Mapper chọn khoảng cách, không phân loại nghĩa của tên mood

`backend/Services/BilingualMoodAnalysisService.cs` chọn tâm vùng mood gần V/A nhất. Mobile nhận `moodId` và dịch tên hiển thị; không đọc lại văn bản để tự đổi nhãn.

Với `I am happy today.`, model trả `(0,518548; 0,256273)`. Khoảng cách đến tâm Romantic `(0,65; 0,15)` khoảng `0,1690`, đến tâm Happy `(0,75; 0,60)` khoảng `0,4144`. Backend chọn Romantic vì gần hơn, không có bước kiểm tra ý nghĩa yêu đương.

Romantic, Focused và Nostalgic mang nghĩa về đối tượng, hoạt động hoặc ký ức. Hai tọa độ dễ chịu/năng lượng không bảo đảm phân biệt được chúng. Vùng Happy/Excited còn chồng lấn; nhãn có thể đổi theo tọa độ/geometry. Đây không phải lỗi Flutter dịch Happy thành Excited.

### PhoBERT phân loại emotion, không trực tiếp phân loại 7 mood của app

PhoBERT có `Anger, Disgust, Enjoyment, Fear, Other, Sadness, Surprise`; không có lớp riêng Relaxed/Excited/Focused/Romantic/Nostalgic.

Adapter trung bình điểm neo theo xác suất. Neo Enjoyment `(0,8; 0,7)` khiến câu thư giãn/tình yêu cùng được nhận là Enjoyment có thể bị đưa về vùng nhiều năng lượng. Bảng neo không biểu diễn được V dương/A âm, như baseline trước đã phân tích.

Trên các ví dụ VI có kỳ vọng emotion, khớp 15/17; đây vẫn là bộ nhỏ tự soạn. Emotion và mood là hai mức nhãn khác nhau. Câu giận trong bảng bị nhận thành Sadness, cho thấy có lỗi cả trước bước mapper.

Runtime VI còn báo `legacy_fallback_missing_training_export`. Candidate từ CSV chưa được chứng minh tương đương preprocessing lần train và chưa được đưa vào model active.

### EN regression không được train để chọn tên mood

Đã đối chiếu cell train trong `code-train/train-KLCN/emobank.ipynb` với `ai-service/va_predictor.py`: chuẩn hóa `(giá trị - 3) / 2`, lấy hidden state token đầu, dropout rồi linear hai đầu ra; serving ở chế độ eval. Không thấy thiếu phép đổi thang ở serving hiện tại.

Cell test khác ở cuối notebook dùng `pooler_output`, khác cell train. Serving hiện theo cell train; không đổi sang cell test đó khi chưa có bằng chứng.

EN dự đoán V/A, không sinh trực tiếp Happy/Romantic/Focused. Một số tọa độ dự đoán cũng lệch kỳ vọng, nên đổi tên nhãn trong Mobile không giải quyết toàn bộ.

### Kiểm thử luồng pass chưa là đạt chất lượng AI

Các test trước xác minh auth, receipt, model routing, kiểu dữ liệu, timeout, xác nhận, gợi ý và lịch sử. `matched` hiện nghĩa kết quả hợp lệ, mapper chọn được mood và cho phép xác nhận; không có nghĩa đã biết đáp án thật của câu.

Runner mới tách hai kết luận: hạ tầng pass mà còn lệch ngữ nghĩa sẽ báo `semantic_mismatches_found`.

## Toàn bộ 20 cặp câu

Kỳ vọng tự soạn chưa qua review độc lập. “Xem riêng” không tính vào mẫu số mood.

| # | Tiếng Việt | Kỳ vọng | Kết quả VI | Tiếng Anh | Kết quả EN |
| --- | --- | --- | --- | --- | --- |
| 01 | hôm nay tao vui | Vui vẻ | Vui vẻ | I am happy today. | Lãng mạn |
| 02 | Hôm nay tôi cảm thấy vui vẻ. | Vui vẻ | Vui vẻ | Today I feel happy. | Lãng mạn |
| 03 | Hôm nay tôi rất vui và hài lòng với cuộc sống. | Vui vẻ | Vui vẻ | Today I am happy and content with my life. | Lãng mạn |
| 04 | Tôi cực kỳ hào hứng, tràn đầy năng lượng và mong chờ buổi hòa nhạc tối nay! | Hào hứng | Vui vẻ | I am extremely excited, full of energy and cannot wait for tonight's concert! | Tập trung |
| 05 | Hôm nay tôi rất buồn, chỉ muốn khóc. | Buồn | Buồn | Today I feel very sad and just want to cry. | Buồn |
| 06 | Tôi thấy bình yên và thư giãn khi ngồi uống trà. | Thư giãn | Vui vẻ | I feel peaceful and relaxed while sitting with a cup of tea. | Hoài niệm |
| 07 | Tôi rất tức giận vì bị đối xử bất công. | Xem riêng | Buồn | I am very angry because I was treated unfairly. | Buồn |
| 08 | Tôi lo lắng và sợ hãi về chuyện ngày mai. | Xem riêng | Chưa đủ tin cậy | I am anxious and scared about tomorrow. | Buồn |
| 09 | Hôm nay tôi không vui, tôi thấy rất buồn. | Buồn | Buồn | I am not happy today; I feel very sad. | Buồn |
| 10 | Hôm qua tôi buồn nhưng hôm nay tôi vui. | Vui vẻ | Chưa đủ tin cậy | I was sad yesterday but today I am happy. | Tập trung |
| 11 | Tôi đang buồn nhưng muốn tìm nhạc vui để cải thiện tâm trạng. | Buồn | Buồn | I feel sad but want happy music to improve my mood. | Hoài niệm |
| 12 | Tôi vừa vui vừa lo lắng về công việc mới. | Xem riêng | Chưa đủ tin cậy | I feel both happy and anxious about my new job. | Hoài niệm |
| 13 | hom nay tao vui | Vui vẻ | Vui vẻ | I feel happy. | Lãng mạn |
| 14 | Hôm nay tôi vui quá 😊 | Vui vẻ | Vui vẻ | I am so happy today 😊 | Vui vẻ |
| 15 | Tôi vui lắm! | Vui vẻ | Vui vẻ | I am happy! | Tập trung |
| 16 | Tôi vui vì đã hoàn thành công việc hôm nay. | Vui vẻ | Vui vẻ | I am happy because I finished my work today. | Lãng mạn |
| 17 | Tôi đang yêu và thấy thật lãng mạn khi nghĩ về người ấy. | Lãng mạn | Vui vẻ | I am in love and feel romantic when I think about my partner. | Tập trung |
| 18 | Tôi đang tập trung cao độ để học và không muốn bị làm phiền. | Tập trung | Chưa đủ tin cậy | I am focused on studying and do not want to be disturbed. | Hoài niệm |
| 19 | Tôi nhớ tuổi thơ, những ký ức cũ khiến tôi bồi hồi hoài niệm. | Hoài niệm | Chưa đủ tin cậy | I miss my childhood and feel nostalgic about those old memories. | Hoài niệm |
| 20 | Tôi rất phấn khích và háo hức chờ chuyến đi sắp tới! | Hào hứng | Vui vẻ | I am thrilled and excited about my upcoming trip! | Tập trung |

## Đã làm trong lượt kiểm tra này

- Thêm `tests/BilingualMoodDiagnostics.cs` và option `--bilingual-diagnostic`.
- Chạy model thật qua đúng HTTP API, so raw output, mapping và lặp cùng câu; fingerprint trước/sau không đổi.
- Build backend/tests riêng: 0 warning/error. 14 kiểm thử Flutter liên quan pass.
- Backend/account/DB thử đã dọn; stack chính tiếp tục chạy.
- Không đổi weights, preprocessing, điểm neo, geometry, threshold hoặc active policy. **Chưa sửa xong chất lượng AI**; thêm test không tự làm dự đoán đúng.
- `adb devices` chưa thấy thiết bị. Lượt này kiểm tra HTTP API và unit/widget client, chưa thao tác Android UI trực tiếp.

## Hướng sửa còn cần triển khai

1. Chốt nghĩa cảm xúc hiện tại, năng lượng và nhu cầu giải trí. “Đang buồn, muốn nhạc vui” không được coi hiện tại là vui.
2. Tách đầu ra model thực sự khỏi nhãn mood chi tiết. Không dùng tâm V/A gần nhất làm kết luận ngữ nghĩa về yêu đương, tập trung hoặc hoài niệm.
3. Đánh giá classifier/ngữ nghĩa phù hợp taxonomy, hoặc cho người dùng chọn ứng viên khi model chưa phân biệt được. Classifier GoEmotions sẵn có có thể được khảo sát cho EN, nhưng phải ghi provenance và kiểm chứng pipeline mới; không gọi đó là chính model VA tự phân loại emotion.
4. Với VI, khôi phục/đánh giá preprocessing và adapter/model phù hợp. Không gán Other thành Relaxed hoặc sửa neo chỉ để các câu này pass.
5. Dùng validation có nhãn được review; kiểm tra phủ định, thời gian, pha trộn, tiếng Việt không dấu/tiếng lóng. Khi điều chỉnh từ bộ ví dụ này, cần test mới chưa dùng để lựa chọn phương pháp. Giữ quality gate và rollback.
6. Nghiệm thu riêng ngữ nghĩa, chất lượng emotion/VA, tỷ lệ từ chối và lỗi mỗi ngôn ngữ; không đóng phần chất lượng từ build/transport pass.

Đây là việc còn phải làm, không phải thay đổi sản phẩm đã triển khai trong lượt chẩn đoán.

## Chạy lại

Bật stack AI local trước, rồi chạy từ root project:

```powershell
dotnet build tests\Moodify.Integration.csproj --artifacts-path .local-build\mood-diagnostic-build
$env:MOODIFY_TEST_ROOT = (Get-Location).Path
$env:MOODIFY_BACKEND_DLL = Join-Path (Get-Location) '.local-build\mood-diagnostic-build\bin\mood_recommendation\debug\mood_recommendation.dll'
dotnet .local-build\mood-diagnostic-build\bin\Moodify.Integration\debug\Moodify.Integration.dll --bilingual-diagnostic
$diagnosticExit = $LASTEXITCODE
```

Dùng inference local có sẵn, không load thêm model trên GPU. Copy moods/policy vào DB riêng, mở backend thử ở cổng ngẫu nhiên rồi dọn. Không ghi account/history/ratings/receipt/metrics thử vào DB chính.

Runner trả `0` khi mọi ví dụ có kỳ vọng đều khớp, `2` khi kiểm tra hoàn tất nhưng có mismatch ngữ nghĩa; exception là lỗi hạ tầng/hợp đồng. Xem `$diagnosticExit` và JSON; native nonzero không có nghĩa model service đã tự tắt.

Bằng chứng local: `.local-build/bilingual-mood-diagnostic-20261029T151308Z/results.json`.

Xem [mapping](MAPPING_GAN_NHAN_AI_HIEN_TAI.md), [baseline 27/10](AI_BASELINE_PHUONG_2026-10-27.md), [protocol](AI_EVALUATION_PROTOCOL.md).
