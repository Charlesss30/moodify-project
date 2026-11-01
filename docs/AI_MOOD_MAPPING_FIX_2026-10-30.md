# Sửa luồng chọn tâm trạng Việt/Anh — 30/10/2026

> **Bản cập nhật v3 — 30/10/2026:** Đã sửa xử lý phủ định và rà 136 câu/nhóm luồng. [Kết quả mới, phạm vi và giới hạn](AI_FLOW_AUDIT_2026-10-30.md). Các số liệu bên dưới là snapshot trước v3.

## Đã triển khai

**Đã thay bước chọn mood bằng tâm V/A gần nhất trong API Mobile/Web bằng ánh xạ ngữ cảnh có phiên bản `context-mapping-v2`.** Hai model vẫn phân tích đúng ngôn ngữ và trả điểm gốc; phần diễn giải mô tả trực tiếp là quy tắc ứng dụng minh bạch, không phải khả năng mới được train thêm vào weights.

- Câu trực tiếp nói vui/happy chọn Happy, hào hứng/excited chọn Excited, thư giãn/relaxed chọn Relaxed.
- Xử lý phủ định, quá khứ so với hiện tại, mô tả cảm xúc người khác, và mong muốn nhạc/phim. “Đang buồn nhưng muốn nhạc vui” giữ trạng thái hiện tại là buồn.
- Câu pha trộn hoặc không có mood phù hợp như giận/lo sợ trả `unknown`, không ép thành Sad, Focused hoặc Romantic.
- Khi không có mô tả trực tiếp: VI chỉ suy ra nhóm được nhãn emotion hỗ trợ; EN chỉ dùng các trạng thái tổng quát phù hợp V/A, không tự suy ra tình yêu/ký ức/tập trung từ tọa độ.
- Giữ nguyên `AI.Enabled`, giới hạn confidence VI và policy. Model chưa đạt ngưỡng vẫn cần người dùng chọn lại.
- Không tìm thấy mood phù hợp trong policy/catalog thì trả chưa xác định; không chuyển sang mood khác chỉ để có kết quả.
- Mobile/Web giải thích rõ nguồn chọn mood và vẫn yêu cầu xác nhận.

Đây là **AI kết hợp quy tắc ngữ cảnh ở tầng ứng dụng**. Không gọi kết quả này là accuracy của riêng PhoBERT/RoBERTa hoặc tuyên bố đã retrain hai model.

## Điểm model và điểm dùng để gợi ý

| Trường | Ý nghĩa |
| --- | --- |
| Analysis `valence/arousal` | Điểm gốc do model/adapter trả, giữ nguyên |
| Analysis `recommendationValence/recommendationArousal` | Target cho nội dung, được giới hạn trong vùng mood được chọn |
| Analysis `mappingMethod` | `explicit_self_description`, `model_emotion`, `model_va_general`, hoặc `needs_clarification` |
| Receipt `valence/arousal` | Điểm gốc, giữ phía server; không lưu văn bản người dùng |
| Recommendation `valence/arousal` | Target thật dùng trong ranking |
| History `analysisValence/analysisArousal` | Điểm model gốc |
| History `valence/arousal` | Target thật dùng trong lần gợi ý |

Target là phép chiếu tọa độ vào hình chữ nhật mood bằng clamp từng trục. Ví dụ VI mô tả thư giãn nhưng raw model trả A=0,614: target A bị giới hạn về maxArousal của Relaxed (0,1). Nhãn và ranking nhờ đó không đi theo hai nhóm trái nhau. Đây là xử lý target ở ứng dụng, không chỉnh sửa output đã lưu của model.

`mappingVersion` gồm version policy + version mapper + phương pháp. Xác nhận kiểm tra version hiện tại, geometry hash, model version, user, hạn dùng và lease. Receipt từ logic cũ bị từ chối, người dùng phân tích lại. Chọn lại thủ công vẫn được ghi là quyết định manual.

Các tên/alias trong taxonomy phải nhận diện được và không trùng. Mood tùy biến ngoài các nhóm hiện có không bị tự ghép nghĩa; cần cấu hình/đánh giá phù hợp trước khi dùng tự động.

## Đối chiếu các ca đã báo lỗi

| Câu | Trước sửa | Sau sửa |
| --- | --- | --- |
| hôm nay tao vui | Vui vẻ | Vui vẻ |
| I am happy today. | Lãng mạn | Vui vẻ |
| Tôi cực kỳ hào hứng, tràn đầy năng lượng và mong chờ buổi hòa nhạc tối nay! | Vui vẻ | Hào hứng |
| I am extremely excited, full of energy and cannot wait for tonight's concert! | Tập trung | Hào hứng |
| Tôi thấy bình yên và thư giãn khi ngồi uống trà. | Vui vẻ | Thư giãn |
| I feel peaceful and relaxed while sitting with a cup of tea. | Hoài niệm | Thư giãn |
| Tôi đang buồn nhưng muốn tìm nhạc vui để cải thiện tâm trạng. | Buồn | Buồn |
| I feel sad but want happy music to improve my mood. | Hoài niệm | Buồn |
| Tôi đang yêu và thấy thật lãng mạn khi nghĩ về người ấy. | Vui vẻ | Lãng mạn |
| I am in love and feel romantic when I think about my partner. | Tập trung | Lãng mạn |
| I am focused on studying and do not want to be disturbed. | Hoài niệm | Tập trung |

## Kết quả kiểm chứng

- 40 câu model thật, 20 VI/20 EN, đúng HTTP API dùng bởi Mobile.
- VI khớp mood kỳ vọng từ **10/17 lên 14/17**; EN từ **4/17 lên 17/17**.
- **31/34** câu có kỳ vọng mood được nhận và khớp; không có câu được nhận mà sai mood kỳ vọng trong bộ này.
- Ba câu VI còn lại là `vi-10` (quá khứ/hiện tại), `vi-18` (tập trung), `vi-19` (hoài niệm): bị từ chối vì model dưới ngưỡng confidence. Không hạ threshold để ép pass.
- Sáu câu giận/lo sợ/pha trộn trả chưa rõ/cần chọn lại, không tính là các mood đã phân loại đúng.
- **346 assertion** qua model thật, gồm raw V/A, ngôn ngữ, receipt, target trong vùng mood, Hybrid và history.
- **158 assertion** về ngữ cảnh/negation/mục tiêu/chủ thể/taxonomy.
- **962 assertion backend** trên MongoDB tạm pass.
- **81 Flutter unit/widget tests** pass; 11 test liên quan chạy lại sau chỉnh provenance manual cũng pass; analyzer sạch.
- Build .NET 0 warning/error; kiểm tra cú pháp JS Web pass.
- Fingerprint hai model active trước/sau các ca thật giữ nguyên; không đổi weights, file preprocessing, threshold, registry hoặc cấu hình dataset/train.

Bộ 40 câu đã được dùng để tìm và sửa lỗi, vì vậy chỉ là **hồi quy chẩn đoán**, không còn là test độc lập. Chưa kết luận accuracy tổng quát hoặc chất lượng trên cách diễn đạt ngoài phạm vi quy tắc. Không dùng các số này để approve candidate mới.

Bằng chứng local sau sửa: `.local-build/bilingual-mood-diagnostic-20261030T071439Z/results.json`. Mã trả về diagnostic vẫn là **2** do 3 ca có kỳ vọng còn bị từ chối; không ghi toàn bộ 40 câu pass.

## File chính

- `backend/Services/SemanticMoodMapper.cs`: ngữ cảnh, taxonomy, fallback và target projection.
- `backend/Services/BilingualMoodAnalysisService.cs`: model acceptance + chọn mood + provenance/version.
- `backend/Controllers/RecommendationController.cs`: dùng target phù hợp mood, giữ raw trong history.
- `backend/Services/BilingualPolicyService.cs`, `backend/Controllers/AiWorkspaceController.cs`: metadata version/scope.
- `mobile-app/lib/services/mood_analysis/mood_analyzer.dart`, `mobile-app/lib/screens/mood_discovery_screen.dart`: nguồn xác định mood và chọn lại manual.
- `backend/wwwroot/app.js`: nội dung Web tương ứng.
- `tests/SemanticMoodMapperTests.cs`, `tests/BilingualAnalysisTests.cs`, `tests/BilingualMoodDiagnostics.cs`: hồi quy mới; điều chỉnh test policy và real-inference theo hợp đồng target/raw tách biệt.

## Vận hành và giới hạn

Đã khởi động stack với backend được sửa. App gọi cổng 5170 sẽ nhận logic chọn mood mới; các thay đổi giải thích trong Flutter cần hot restart/build lại từ source. Không có Android kết nối trong `adb devices` lúc kiểm tra nên chưa cài APK hoặc kiểm thử trực tiếp trên thiết bị trong lượt này.

Không có huấn luyện model mới hoặc thay đổi nhãn người dùng. Preprocessing VI gốc còn thiếu export, khả năng hiểu câu ngoài quy tắc vẫn phụ thuộc model; không có cam kết 100% mọi câu tự nhiên. Câu chưa chắc chắn tiếp tục cho người dùng chọn mood thủ công.

Chạy kiểm tra ngữ cảnh:

```powershell
dotnet build tests\Moodify.Integration.csproj --artifacts-path .local-build\mood-context-build
dotnet .local-build\mood-context-build\bin\Moodify.Integration\debug\Moodify.Integration.dll --mood-context-contract
```

Bộ model thật chạy theo hướng dẫn của [báo cáo chẩn đoán](AI_MOOD_ROUTING_DIAGNOSTIC_2026-10-29.md), đổi artifacts-path sang `mood-context-build`. Chỉ các database tạm bị ghi/xóa khi test; catalog/account/history/ratings chính không dùng để ghi đánh giá thử.
