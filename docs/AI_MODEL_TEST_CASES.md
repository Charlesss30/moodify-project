# Bộ ca thử PhoBERT tiếng Việt và RoBERTa VA tiếng Anh

Ngày đối chiếu mã nguồn: 25/10/2026. Đây là ca thử được soạn từ luồng hiện có, chưa phải kết quả kiểm thử. Không thay đổi model, mapping hay dữ liệu người dùng khi soạn tài liệu này.

## 1. Khi nào dùng model nào?

Hai model active độc lập; active đồng thời không có nghĩa mỗi câu chạy qua cả hai model.

| Nơi sử dụng | Cách chọn | Model thực sự được gọi |
| --- | --- | --- |
| Web người dùng, AI Mood Analysis | Chọn `Tiếng Việt · PhoBERT` | POST `/api/MoodAnalysis/valence-arousal`, `language=vi` → `/predict-vi` → PhoBERT đang active |
| Web người dùng, AI Mood Analysis | Chọn `English · RoBERTa VA` (mặc định) | Cùng API trên, `language=en` → `/predict-va` → RoBERTa VA đang active |
| Mobile, ô mô tả trong Khám phá | Tự nhận diện Việt/Anh, không có bộ chọn ngôn ngữ | `/api/MoodAnalysis/valence-arousal`: `vi` → PhoBERT, `en` → RoBERTa VA |
| Dashboard kỹ sư, Inference lab hiện tại | Mục `Test English text` của Emotion classifier | Luồng classifier tiếng Anh cũ, không dùng để kết luận chất lượng hai model mới |

Web chọn theo trường `language`, chưa tự phát hiện ngôn ngữ từ nội dung câu. Nhập tiếng Việt nhưng giữ English vẫn đi RoBERTa VA. Mobile đã chuyển từ classifier cũ sang API song ngữ: tự nhận diện dấu và từ thường gặp, không có lựa chọn ngôn ngữ thủ công. Câu không dấu hoặc pha ngôn ngữ có thể nhận diện chưa chính xác. Văn bản chỉ có emoji mặc định dùng tiếng Việt; văn bản Latin không có dấu hiệu tiếng Việt mặc định dùng tiếng Anh. Đây là quy tắc định tuyến nhẹ, không phải bộ nhận diện mọi ngôn ngữ.

Tên phiên bản đã dùng trong lần cấu hình gần nhất: `phobert_vsmec_v2` và `roberta_va_regression`. Nếu đổi active version, tên trả về sẽ đổi; không dựa vào tên file ZIP để xác định model đang xử lý.

## 2. Cách biết chắc câu vừa nhập dùng model nào

1. Đăng nhập Web người dùng tại `http://localhost:5170/`, tới AI Mood Analysis và chọn đúng ngôn ngữ.
2. Mở DevTools → Network → thực hiện phân tích → chọn request `MoodAnalysis/valence-arousal`.
3. Trong Payload, kiểm tra `language`. Trong Response, kiểm tra `language` và `modelVersion`. Đây là bằng chứng của request vừa chạy. Có tên model trong dashboard chỉ chứng minh trạng thái runtime, không chứng minh request trên Mobile/Web đã đi đúng luồng.
4. PhoBERT trả thêm `emotion`, `confidence`, `accepted`, `emotionCandidates`. RoBERTa VA trả `valence`, `arousal`, `moodCandidates`; không có confidence phân loại cảm xúc.

Trên Mobile, vào Khám phá → nhập câu → Phân tích tâm trạng. Một dòng nhỏ dưới ô nhập hiển thị `Model: PhoBERT · Tiếng Việt` hoặc `Model: RoBERTa VA · English`. Ngôn ngữ được nhận diện tự động; mở **Thông tin phân tích** để xem `Phiên bản` model thực sự trả về. Khi xác nhận nhận gợi ý, Mobile gửi nguyên V/A và phiên bản với `source=ai_va`. Nếu chọn lại mood thủ công, app xóa quyết định AI và gửi `source=manual`.

Có thể thử trực tiếp inference bằng PowerShell khi service đang chạy, không cần ghi lịch sử hay xin gợi ý:

```powershell
function Test-MoodModel {
    param(
        [ValidateSet('vi', 'en')][string]$Language,
        [string]$Text
    )
    $route = if ($Language -eq 'vi') { 'predict-vi' } else { 'predict-va' }
    $body = @{ text = $Text; language = $Language } | ConvertTo-Json
    Invoke-RestMethod -Method Post -Uri "http://127.0.0.1:8001/$route" `
        -ContentType 'application/json; charset=utf-8' `
        -Body ([System.Text.Encoding]::UTF8.GetBytes($body))
}

Test-MoodModel -Language vi -Text 'Tôi rất vui vì hôm nay được gặp lại bạn thân.'
Test-MoodModel -Language en -Text 'I feel calm and peaceful after a quiet evening.'
```

Cách gọi trực tiếp chỉ kiểm tra model. Muốn kiểm tra cả backend, mood trong database và giao diện, dùng Web như bước trên. Hai API Python nội bộ không thay thế endpoint xác thực của ứng dụng.

## 3. Cách đọc kết quả và chấm ca thử

- Valence (V): âm nghiêng về khó chịu/tiêu cực; dương nghiêng về dễ chịu/tích cực.
- Arousal (A): âm nghiêng về ít năng lượng, yên lặng; dương nghiêng về kích thích, mạnh, căng thẳng hoặc phấn khích. A cao không đồng nghĩa vui.
- V và A phải là số hữu hạn trong [-1, 1]. Không đặt trước con số chính xác cho câu chưa được gán nhãn VA độc lập.
- RoBERTa VA dự đoán trực tiếp hai tọa độ bằng mô hình regression. PhoBERT phân loại 7 cảm xúc rồi tính V/A bằng trung bình có trọng số xác suất trên các điểm neo do project quy định; V/A của PhoBERT không phải đầu ra của một model regression đã học riêng.
- Backend chọn mood có tâm vùng V/A gần nhất theo khoảng cách Euclidean; nếu bằng nhau, ưu tiên mood ID nhỏ hơn. Tên mood phụ thuộc cấu hình database, không bắt buộc một câu phải luôn ra tên mood cố định sau khi mapping bị chỉnh.
- `confidence_threshold.json` của phiên bản PhoBERT `phobert_vsmec_v2` hiện đặt ngưỡng **0.7559639811515808**. `accepted` phải bằng phép so sánh confidence >= ngưỡng này. Không phải câu đúng nhãn nào cũng nhất thiết vượt ngưỡng.
- Nếu `accepted=false`, Web hiện cảnh báo và không hiện nút lấy gợi ý cho kết quả đó. Backend vẫn có thể trả `status=matched`; không được dùng riêng status này làm bằng chứng vượt ngưỡng.
- Các nhãn/hướng V/A trong bảng dưới là kỳ vọng để đánh giá chất lượng, chưa được xác minh bằng suy luận thật. Trả JSON hợp lệ là đạt kỹ thuật; ra sai cảm xúc vẫn là chưa đạt chất lượng. Confidence cao cũng không chứng minh dự đoán đúng.

## 4. Mười ca thử PhoBERT tiếng Việt

Chọn Tiếng Việt trên Web hoặc dùng `Test-MoodModel -Language vi`. Mỗi ca phải trả đúng active version tiếng Việt, không đổi active model tiếng Anh.

| Mã | Văn bản nhập nguyên câu | Kỳ vọng cảm xúc để đối chiếu | Điều cần quan sát |
| --- | --- | --- | --- |
| VI-01 | Tôi rất vui vì hôm nay được gặp lại bạn thân. | Enjoyment | Phát hiện niềm vui rõ ràng; ghi confidence, accepted và top 3. |
| VI-02 | Tôi tức giận vì người ta liên tục nói dối và thất hứa với tôi. | Anger | Phân biệt giận với buồn; V thường âm, A thường dương khi xác suất Anger chiếm ưu thế. |
| VI-03 | Tôi thấy ghê tởm khi nhìn thức ăn ôi thiu đầy giòi. | Disgust | Phân biệt ghê tởm với sợ hãi. |
| VI-04 | Tôi sợ quá, tim đập nhanh khi phải đi một mình trong con hẻm tối. | Fear | Lo sợ và kích thích mạnh; tránh diễn giải A cao thành vui vẻ. |
| VI-05 | Tôi buồn và cô đơn, chẳng còn muốn làm gì sau khi chia tay. | Sadness | Buồn, năng lượng thấp; V/A thường cùng âm nếu Sadness áp đảo. |
| VI-06 | Tôi bất ngờ quá, không ngờ bạn lại xuất hiện ngay trước cửa nhà! | Surprise | Nhận ra bất ngờ; điểm neo Surprise hiện dương V và A, đây là quy ước project. |
| VI-07 | Hôm nay tôi đi làm lúc tám giờ và về nhà lúc năm giờ. | Other | Câu tường thuật không thể hiện cảm xúc rõ; không ép thành vui/buồn. V/A không nhất thiết đúng bằng 0 vì là trung bình xác suất. |
| VI-08 | Tôi không buồn nữa, giờ tôi thấy vui và nhẹ nhõm rồi. | Enjoyment | Ca phủ định và đổi cảm xúc; không chỉ bám từ “buồn”. |
| VI-09 | Tôi vui vì đậu phỏng vấn nhưng vẫn lo lắng về ngày đầu đi làm. | Enjoyment hoặc Fear | Cảm xúc pha trộn, không ép một đáp án duy nhất; xem top 3 có phản ánh hai phía và accepted có đúng ngưỡng. |
| VI-10 | 😭😭😭 | Sadness | Kiểm tra emoji mặc định được chuẩn hóa thành từ “khóc buồn”; không lỗi vì mất hết nội dung. |

PhoBERT hiện có đúng 7 lớp: Anger, Disgust, Enjoyment, Fear, Other, Sadness, Surprise. Không yêu cầu trực tiếp trả nhãn Romantic/Relaxed: đó là mood của ứng dụng, khác bộ nhãn huấn luyện.

## 5. Mười ca thử RoBERTa VA tiếng Anh

Chọn English trên Web hoặc dùng `Test-MoodModel -Language en`. Không yêu cầu trả nhãn GoEmotions hay confidence vì đây là regression V/A.

| Mã | Văn bản nhập nguyên câu | Hướng V/A mong đợi để đối chiếu | Mục đích |
| --- | --- | --- | --- |
| EN-01 | I am thrilled that I got my dream job! I want to celebrate and dance all night. | V dương, A dương | Tích cực, năng lượng cao. |
| EN-02 | I feel calm and peaceful after a quiet evening reading at home. | V dương, A âm | Tích cực, yên bình; phân biệt với EN-01. |
| EN-03 | I am furious about being lied to. My heart is racing and I want to shout. | V âm, A dương | Giận dữ, kích thích mạnh. |
| EN-04 | I feel lonely and heartbroken. I have no energy and just want to stay in bed. | V âm, A âm | Buồn và thiếu năng lượng; phân biệt với EN-03. |
| EN-05 | I am terrified about tomorrow's surgery and cannot stop shaking. | V âm, A dương | Sợ hãi; không nhầm kích thích cao với tích cực. |
| EN-06 | I feel safe and loved while quietly holding my partner's hand. | V dương, A thấp | Tình cảm tích cực, không nhất thiết phấn khích; so với EN-01. |
| EN-07 | Nothing special happened today. I completed my usual tasks and went home. | V gần trung tính, A thấp hoặc vừa | Mốc tường thuật trung tính; không buộc đúng (0, 0). |
| EN-08 | I am not sad anymore. I feel hopeful and happy about what comes next. | V nghiêng dương | Phủ định; không chỉ phản ứng với từ “sad”. A không có một đáp án cứng. |
| EN-09 | I am excited about my new job, but anxious about leaving my family behind. | V có thể pha trộn, A nghiêng cao | Cảm xúc hỗn hợp; không chấm sai chỉ vì V gần 0, cần đánh giá ngữ cảnh. |
| EN-10 | After the panic passed, I finally feel relieved and can rest peacefully. | V nghiêng dương, A thấp hơn EN-05 | Chuyển từ căng thẳng sang nhẹ nhõm; ưu tiên trạng thái hiện tại. |

Các ca trên kiểm tra ngữ nghĩa, không tạo bộ chuẩn đủ để công bố accuracy hoặc chất lượng mô hình tổng thể. Để đánh giá VA định lượng cần tập độc lập có nhãn số và dùng MAE/RMSE/correlation phù hợp; để đánh giá PhoBERT cần nhãn cảm xúc độc lập và theo dõi macro-F1, ma trận nhầm lẫn, coverage cùng selective accuracy.

## 6. Mẫu ghi nhận khi chạy

| Mã ca | modelVersion thực tế | emotion/top 3 (VI) | confidence/accepted (VI) | V | A | mood trên Web | Đạt kỹ thuật | Nhận xét ngữ nghĩa |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Điền mã ca | | | | | | | | |

Nên ghi kết quả trước khi bấm lấy gợi ý để tách lỗi hiểu cảm xúc khỏi lỗi ranking nội dung. Model hiểu câu đúng nhưng phim/nhạc chưa hợp có thể do kho nội dung, mapping V/A hoặc thuật toán recommendation, chưa đủ kết luận model sai.

## 7. Mã nguồn đã đối chiếu

- `backend/wwwroot/index.html`: lựa chọn ngôn ngữ của Web.
- `backend/wwwroot/app.js`: gửi ngôn ngữ, nhận V/A, xử lý accepted=false.
- `backend/Controllers/MoodAnalysisController.cs`: hai endpoint MoodAnalysis cũ và valence-arousal mới.
- `backend/Services/VietnameseEmotionService.cs`, `ValenceArousalAnalysisService.cs`: gọi inference và tìm mood gần nhất.
- `backend/Services/MoodAnalysisService.cs`: luồng classifier tiếng Anh cũ.
- `ai-service/vietnamese_predictor.py`, `va_predictor.py`, `main.py`: chuẩn hóa, suy luận và schema đầu ra.
- `mobile-app/lib/core/api_service.dart`, `mobile-app/lib/screens/mood_discovery_screen.dart`, `mobile-app/lib/services/mood_analysis/mood_language.dart`: API song ngữ, tự nhận diện ngôn ngữ và dòng model dưới ô nhập, gửi đúng V/A và modelVersion.
- `admin-web/src/ai-workspace.js`: Inference lab hiện ghép lại mục Test English text.

## 8. Kết quả kiểm tra tích hợp Mobile ngày 25/10/2026

- 13 ca kiểm tra tập trung đã pass: chọn model qua gateway giả lập theo Việt/Anh, Unicode, parser hợp đồng, giữ nguyên V/A, ghi modelVersion trong request nhận gợi ý, độ tin cậy thấp, chọn thủ công, đổi bộ lọc và loại bỏ phản hồi cũ khi sửa mô tả.
- Các kiểm tra này xác minh mã Mobile với HTTP giả lập; không phải kết quả chạy 20 câu đánh giá chất lượng ở hai bảng trên.
- Kiểm tra cổng 8001 trong lượt này không kết nối được; chưa chạy lại suy luận thật hay xác nhận trên emulator/điện thoại trong lượt tích hợp này. Cần bật backend/model trước khi thử bằng app. Không có dữ liệu MongoDB nào được tạo hay sửa bởi các kiểm tra giả lập.


Sau khi khôi phục giao diện `eeda4b6` ngày 25/10/2026, toàn bộ **57 ca kiểm tra Flutter pass**; các ca AI song ngữ vẫn kiểm tra đúng endpoint/modelVersion/V/A trong giao diện đã khôi phục. Thông tin phiên bản nằm trong mục mở rộng **Thông tin phân tích**. Kết quả này không thay thế việc chạy 20 câu đánh giá chất lượng trên model thật.
