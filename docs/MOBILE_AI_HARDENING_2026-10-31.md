# Hoàn tất tối ưu luồng AI cho mobile — 31/10/2026

## Kết quả và phạm vi

Đã hoàn tất phần đang dở từ 30/10 trong **`D:/hoc-tap/moodify-project`**. Backend local đang dùng **`context-mapping-v5`** và **`vi-en-heuristic-v2`**. Hai model giữ nguyên: PhoBERT `phobert_vsmec_v2`, RoBERTa VA `roberta_va_regression_v2`; GoEmotions cũ vẫn sẵn sàng CUDA.

Phạm vi bám luồng đề cương: **mô tả tâm trạng → nhận diện Việt/Anh → phân tích model → ánh xạ mood/V-A → người dùng xác nhận → gợi ý Hybrid → lịch sử/đánh giá**. Không bổ sung chatbot, không tải model vào điện thoại, không retrain, không thay weights/tokenizer/threshold/preprocessing/policy/model registry, không bật bandit hoặc tính năng ngoài phạm vi.

Các thay đổi là xử lý ngữ cảnh và ổn định luồng ứng dụng; không được mô tả thành một model mới đã được huấn luyện.

## Những lỗi đã tìm thấy và sửa

### 1. Xác định đúng người đang có cảm xúc

Trước đây parser xét chủ thể cho cả câu, dễ lẫn cảm xúc của người khác với người dùng hoặc bỏ mất cảm xúc người dùng ở phía sau. Bản mới xét chủ thể gắn với từng cụm cảm xúc, có loại trừ đại từ nằm trong cụm như “bạn tôi”, “mẹ tôi”, “my coworker”.

| Mô tả | Cách xử lý |
| --- | --- |
| Tôi thấy bạn tôi đang buồn. | Không tự gán Buồn cho người dùng; yêu cầu chọn/mô tả bản thân |
| Bạn tôi buồn còn tôi đang vui. | Nhận cảm xúc hiện tại của người dùng: Vui |
| My friend is sad and I feel happy. | Happy, dùng model Anh |
| Bạn đang buồn phải không? | Không coi câu hỏi về người đối diện là trạng thái người dùng |
| Bản thân đang buồn. | Giữ được mô tả bản thân; tránh nhầm “bản” bỏ dấu với “bạn” |
| That sad movie makes me feel happy. | Xét phản ứng của người dùng, không dùng sắc thái của phim thay thế |

### 2. Phân biệt hiện tại, quá khứ, tương lai và nguyên nhân

Không bỏ cả câu chỉ vì có “hôm qua/was”. Thời điểm được xét gắn với cảm xúc:

- “Lúc sáng tôi buồn nhưng bây giờ tôi vui” → dùng trạng thái hiện tại.
- “Tôi buồn vì đã mất việc hôm qua” → chuyện quá khứ là nguyên nhân, cảm xúc Buồn vẫn ở hiện tại.
- “Hôm nay tôi nghĩ ngày mai tôi sẽ vui” → chưa xác định được cảm xúc hiện tại.
- “Tôi buồn nhưng ngày mai tôi sẽ vui” → không lấy trạng thái tương lai ghi đè hiện tại.
- “Tôi đang hào hứng về chuyến đi ngày mai” → cảm xúc hiện tại hướng về một sự kiện tương lai; không coi mọi chữ “ngày mai” là phủ nhận cảm xúc hiện tại. Model vẫn có thể chưa đạt threshold trong câu này.

### 3. Ngăn nhầm từ, phủ định và giả định

- “Buồn ngủ”, “buồn nôn”, “vui lòng”, “Happy birthday”, “chúc mừng sinh nhật” không tự thành mood Sad/Happy.
- Giữ phủ định “không hề”, bổ sung “không thể”; không nhầm “hề” bỏ dấu với đại từ Anh “he”.
- “Tôi chỉ giả vờ vui”, “I am just pretending to be happy” yêu cầu làm rõ; không biến cảm xúc giả định thành trạng thái hiện tại.
- Giữ xử lý nhiều cảm xúc, mỉa mai có dấu hiệu, quotes, mong muốn nội dung và double negation của các phiên trước.

Không dùng phủ định để tự suy ra cảm xúc đối lập. Ví dụ “not happy” chỉ được nhận một mood thay thế khi bằng chứng model phù hợp và đạt điều kiện hiện hành.

### 4. Chọn phần đưa vào model trong cùng một câu

Bản v4 chủ yếu chọn các mệnh đề lớn; một mệnh đề vẫn có thể chứa cả “bạn tôi buồn” và “tôi vui”. V5 chia tiếp phần **đưa vào model**, chọn nguyên văn trạng thái hiện tại đã được xác định phù hợp.

Toàn bộ mô tả gốc vẫn qua kiểm tra chủ thể/thời gian/phủ định/nhiều cảm xúc trước khi chọn và khi ánh xạ. Không cắt bỏ phần mâu thuẫn để ép ra một mood. Không tự viết câu mẫu từ nhãn regex; bảo toàn Unicode/offset và yêu cầu ngôn ngữ của model.

Mỗi lượt hợp lệ vẫn gọi model **một lần**. Thử model toàn văn và model phần đã chọn chỉ là phép so sánh trong runner chẩn đoán, không phải cơ chế chạy nhiều lần trong app rồi chọn score cao nhất.

### 5. Tự nhận diện Việt/Anh ổn định hơn

V2 xét từ vựng đã chuẩn hóa và bằng chứng từ tiếng Việt có dấu, thay vì cứ gặp một ký tự có dấu là chọn PhoBERT:

| Mô tả | Tuyến model |
| --- | --- |
| I feel happy at the café. | EN / RoBERTa VA |
| I'm sad after visiting Hà Nội. | EN / RoBERTa VA |
| Tôi feel sad today | VI / PhoBERT |
| Hôm nay tôi vui | VI / PhoBERT |
| hom nay toi buon | VI / PhoBERT |

Mobile có bộ ví dụ dùng chung để kiểm tra phần gợi ý ngôn ngữ. Backend vẫn là nơi quyết định model thực tế; mobile luôn gửi `language=auto`. Script ngoài Việt/Anh được từ chối ngay cả khi trộn với chữ Việt. Chuỗi chỉ có số, dấu câu hoặc một URL được yêu cầu mô tả thêm trước khi gọi model.

Đây vẫn là heuristic có version, không phải bộ nhận diện mọi ngôn ngữ trên thế giới. Câu trộn phức tạp hoặc ngôn ngữ Latin khác còn có thể cần diễn đạt lại/tự chọn.

## Ổn định thao tác trên mobile

| Tình huống | Hành vi sau sửa |
| --- | --- |
| Sửa mô tả khi AI đang trả lời | Hủy chờ HTTP của lượt cũ, đóng client và bỏ kết quả cũ |
| Chọn mood thủ công khi AI đang chạy | Giữ quyết định manual; kết quả AI trả chậm không ghi đè |
| Gợi ý trả về sau khi đã sửa mô tả/phân tích lại | Request version riêng chặn kết quả cũ xuất hiện |
| Đổi Phim/Nhạc trước lần xác nhận đầu | Giữ kết quả phân tích đang hợp lệ; không bắt gọi model lại |
| Đổi bộ lọc sau khi receipt đã được gửi | Giữ bảo vệ chống gửi lại receipt với request khác; yêu cầu quyết định mới khi cần |
| Chờ quá lâu, mất mạng, model bận/chưa sẵn sàng | Kết thúc loading, cho thử lại hoặc tự chọn tâm trạng |
| Receipt hết hạn theo thời gian | Mobile nhận biết từ `expiresAt`, hướng dẫn phân tích lại |
| Server từ chối receipt do version/policy/model thay đổi | HTTP 409 dẫn về phân tích lại, không lặp cùng request lỗi |
| Đã tự chọn mood nhưng receipt gốc hết hạn | Gửi quyết định manual độc lập; không giả thành kết quả AI còn hiệu lực |

Hủy HTTP ở mobile **không bảo đảm ngắt được kernel GPU đã chạy hoặc hoàn tác recommendation đã commit trên server**. Bảo đảm chính là đóng client/chặn cập nhật UI lỗi thời; user/model/expiry/hash/lease/idempotency của backend tiếp tục bảo vệ xác nhận.

Không thêm request AI theo từng phím gõ, không thêm polling hay thư viện suy luận nặng lên mobile. Model vẫn nằm ở server. Giao diện giữ nguyên tab, player Spotify/YouTube, tài khoản, lịch sử, thư viện và đánh giá.

## Kết quả kiểm thử cuối

Model thật được chạy lúc **31/10/2026 22:39 (UTC+7)** trên RTX 3050 Laptop, sau khi model đã nạp.

| Hạng mục | Kết quả |
| --- | --- |
| Build backend/tests | Thành công, 0 warnings/errors |
| Bộ ngữ cảnh cũ | **328 assertions pass** |
| Ca biên mới và routing dùng chung | **92 checks pass**: 82 câu ngữ cảnh + 10 routing |
| Hồi quy backend HTTP/business trên MongoDB tạm | **1.049 assertions pass** |
| Flutter toàn bộ | **105 tests pass**, gồm 17 test mới về routing và luồng bất đồng bộ |
| Flutter analyzer | Không có vấn đề |
| Android debug APK | Build thành công |
| Câu VI/EN chạy model thật qua API mobile | **256**: 131 VI, 125 EN |
| Câu có mood kỳ vọng | **146** |
| Được nhận và khớp mood kỳ vọng | **141/146**: VI 72/76, EN 69/70 |
| Được nhận nhưng sai mood kỳ vọng | **0 trong phần có kỳ vọng của tập này** |
| Ca bắt buộc làm rõ bị nhận nhầm | **0/98** |
| API/model/phạm vi input/receipt/raw-target/Hybrid/history | **2,244 assertions pass** |
| Ca dùng đầu vào được chọn theo ngữ cảnh | 101 |
| Median API VI / EN | 16.92 / 16.61 ms |
| P95 API VI / EN | 20.39 / 19.17 ms |

145/256 lượt được chấp nhận tổng cộng; 141 trong số đó thuộc nhóm có mood kỳ vọng cố định. Các ví dụ còn lại không có cùng loại kỳ vọng nên không gộp thành một tỷ lệ accuracy.

Bộ câu là hồi quy do người triển khai soạn và đã dùng để sửa, **không phải gold độc lập**. Không gọi tỷ lệ 141/146 là chất lượng tổng quát của hai model.

Runner model thật trả exit **2** vì còn 5 câu có mood kỳ vọng nhưng trả unknown. Các assertion hạ tầng và guard đều pass; không đổi kỳ vọng hoặc hạ threshold để làm đẹp mã thoát.

### Năm câu còn cần tự chọn

| ID | Mô tả | Lý do |
| --- | --- | --- |
| `vi-18` | Tôi đang tập trung cao độ để học và không muốn bị làm phiền. | `low_confidence` |
| `vi-19` | Tôi nhớ tuổi thơ, những ký ức cũ khiến tôi bồi hồi hoài niệm. | `low_confidence` |
| `en-request-07` | I am not happy so I want a song. | `negation_model_conflict` |
| `vi-request-15` | Tôi đang thư giãn nên tôi muốn nghe nhạc nhẹ. | `low_confidence` |
| `vi-edge-19` | Tôi đang hào hứng về chuyến đi ngày mai. | `low_confidence` |

Bốn câu VI dưới ngưỡng model/policy. Câu EN “not happy” chưa có bằng chứng V/A đủ phù hợp để nhận Sad và không tự đồng nghĩa với Sad. Đây là giới hạn của model/bằng chứng hiện tại; muốn cải thiện chất lượng cần preprocessing đúng bản train và đánh giá/huấn luyện theo quy trình riêng, không ép gán nhãn ở UI.

## Kiểm chứng trên stack đang dùng

Đã nạp backend 5170, Admin 5174; PhoBERT, RoBERTa VA và GoEmotions cũ đều sẵn sàng CUDA. Registry/model active giữ nguyên.

Đã kiểm tra **7 tình huống trên API chính**:

1. Câu dài “Vì tôi đang buồn nên tôi muốn…” → Sad bằng PhoBERT.
2. “Bạn tôi buồn còn tôi đang vui” → Happy bằng PhoBERT.
3. “I feel happy at the café” → Happy bằng RoBERTa VA.
4. “My friend is sad and I feel happy” → Happy bằng RoBERTa VA.
5. “Tôi thấy bạn tôi đang buồn” → unknown, không gán cảm xúc người khác cho người dùng.
6. “I am just pretending to be happy” → unknown, không gán cảm xúc giả định.
7. “12345” → unknown/insufficient_text, không gọi model.

Các lượt smoke dùng tài khoản đã được cho phép kiểm thử: chỉ phân tích, tạo 4 receipt ngắn hạn và metric tổng hợp; **không gọi Recommendation/ratings trên DB chính**. Recommendation/history/rating thử nghiệm chỉ nằm trong DB tạm.

Đối chiếu trước/sau: 4 tài khoản, 60 phim, 57 bài hát, 117 contents, 7 mood, 7 đánh giá và 84 history; số lượng giữ nguyên. Validator/options receipt cũng giữ nguyên.

Lượt bật runtime đầu đã timeout 240 giây và runner tự dừng đúng tiến trình của stack. Lượt bật lại thành công sau khi build APK đã hoàn tất; không thay deadline, không hạ ngưỡng, không đổi model để vượt qua readiness. Đây là thông tin vận hành của lượt kiểm tra, không phải benchmark thời gian khởi động.

## Giới hạn nghiệm thu

- Chưa có emulator/điện thoại Android kết nối trong lượt này. Đã build Android và kiểm thử widgets; **chưa đo FPS/khung hình hay thao tác trên thiết bị Android thật**.
- Số đo API local khi model đã warm không phải độ trễ mạng trên điện thoại, không bao gồm startup hoặc tải player.
- Model không được huấn luyện lại trong việc này. Preprocessing export gốc, gold độc lập và quality approval vẫn theo các báo cáo trước; chưa thể bảo đảm mọi câu tự nhiên đều được hiểu đúng.
- Tâm trạng ngoài danh mục, câu mơ hồ và các trường hợp còn yếu vẫn đi qua lựa chọn/xác nhận của người dùng.

## File và cách chạy lại

Source chính:

- `backend/Services/SemanticMoodMapper.cs`
- `backend/Services/MoodLanguageRouter.cs`
- `backend/Services/BilingualMoodAnalysisService.cs`
- `mobile-app/lib/core/api_service.dart`
- `mobile-app/lib/screens/mood_discovery_screen.dart`
- `mobile-app/lib/services/mood_analysis/mood_analyzer.dart`
- `mobile-app/lib/services/mood_analysis/mood_language.dart`

Kiểm thử bổ sung:

- `tests/MobileAiEdgeTests.cs`
- `tests/ai-cases/mobile-ai-edge-cases.json`
- `tests/ai-cases/language-routing-cases.json`
- `tests/BilingualAnalysisTests.cs`, `tests/BilingualMoodDiagnostics.cs`
- `mobile-app/test/mobile_ai_resilience_test.dart`

```powershell
dotnet build tests\Moodify.Integration.csproj --artifacts-path .local-build\mobile-ai-hardening
$env:MOODIFY_TEST_ROOT = (Get-Location).Path
$env:MOODIFY_BACKEND_DLL = Join-Path $env:MOODIFY_TEST_ROOT '.local-build\mobile-ai-hardening\bin\mood_recommendation\debug\mood_recommendation.dll'
dotnet .local-build\mobile-ai-hardening\bin\Moodify.Integration\debug\Moodify.Integration.dll --mood-context-contract
dotnet .local-build\mobile-ai-hardening\bin\Moodify.Integration\debug\Moodify.Integration.dll --mood-edge-contract
dotnet .local-build\mobile-ai-hardening\bin\Moodify.Integration\debug\Moodify.Integration.dll
# Cần hai model local ready:
dotnet .local-build\mobile-ai-hardening\bin\Moodify.Integration\debug\Moodify.Integration.dll --bilingual-full-audit
```

Flutter chạy từ `mobile-app` với `PUB_CACHE=D:/hoc-tap/moodify-pub-cache`: `flutter test`, `flutter analyze`, `flutter build apk --debug --no-pub`.

Chứng cứ local được ignore trên Git:

- `.local-build/bilingual-mood-diagnostic-20261031T153927Z/results.json`
- `.local-build/mobile-ai-backend-20261031.log`
- `.local-build/mobile-ai-flutter-20261031.log`, `mobile-ai-analyzer-20261031.log`
- `.local-build/mobile-ai-apk-20261031.log`
- `.local-build/mobile-ai-main-smoke-20261031.json`
- `.local-build/mobile-ai-main-before-20261031.json`, `mobile-ai-main-after-20261031.json`

APK: `mobile-app/build/app/outputs/flutter-apk/app-debug.apk`. Bản sao source trước sửa: `.local-backups/mobile-ai-hardening-*` và `.local-backups/mobile-ai-resume-*`.
