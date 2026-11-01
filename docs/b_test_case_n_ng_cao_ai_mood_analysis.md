# Bộ Ca Thử Nâng Cao: Nhận Diện Cảm Xúc (Stress-Test)

Ngày đối chiếu: 25/10/2026. 
Bộ test case này tập trung vào các trường hợp khó, mơ hồ, teencode, và mỉa mai (sarcasm) nhằm đánh giá giới hạn và cách xử lý lỗi của hệ thống đối với 2 mô hình PhoBERT (Tiếng Việt) và RoBERTa VA (Tiếng Anh).

## 1. Bộ Test Case Tiếng Việt (Mô hình PhoBERT)

**Mục đích:** Kiểm tra khả năng hiểu teencode, bóc tách mỉa mai, và kiểm tra ngưỡng tin cậy (confidence threshold hiện tại là `0.7559`). Đánh giá xem hệ thống có trả về `accepted=false` khi câu quá mơ hồ hay không.

| Mã | Văn bản nhập nguyên câu | Đặc điểm / Độ khó | Kỳ vọng & Điều cần quan sát theo logic hệ thống |
| :--- | :--- | :--- | :--- |
| **TV-01** | `Chán chả buồn nói, chằm zn thực sự luôn á =)))` | **Teencode / Emoji mâu thuẫn** | Từ lóng "chằm zn" (trầm cảm) đi kèm mỉa mai và emoji cười. Chờ xem hệ thống bám vào chữ "buồn", emoji "=)))" hay từ lóng. Có thể rớt ngưỡng tin cậy (`accepted=false`). |
| **TV-02** | `Tuyệt vời, trượt môn rồi, vui ghê cơ, tạ ơn trời phật.` | **Mỉa mai (Sarcasm)** | Nghĩa đen là vui, nghĩa bóng là tức giận/buồn. Kỳ vọng: Nếu model hiểu sâu sẽ ra Anger/Sadness. Nếu model học vẹt từ "tuyệt vời", "vui" sẽ ra Enjoyment (Sai ngữ nghĩa). |
| **TV-03** | `cay the nho` | **Không dấu (Test luồng Mobile)** | Thiếu dấu tiếng Việt. Trên Mobile, câu này có thể bị hệ thống tự động đẩy nhầm sang model RoBERTa VA tiếng Anh do không có dấu hiệu tiếng Việt. Cần kiểm tra xem nó đi vào `/predict-vi` hay `/predict-va`. |
| **TV-04** | `Vừa khóc vừa cười, đậu đại học rồi nhưng xa nhà nhớ ba má quá.` | **Cảm xúc pha trộn (Mơ hồ)** | Pha trộn rõ ràng giữa Enjoyment (đậu) và Sadness (nhớ nhà). Kỳ vọng: Confidence phân tán, rất có thể `< 0.755` dẫn đến `accepted=false`. |
| **TV-05** | `Bình thường, nay ăn cơm sườn, mai ăn bún bò.` | **Trung tính / Tường thuật** | Không có từ khóa cảm xúc. Kỳ vọng: Nhãn `Other`. Valence/Arousal không nhất thiết bằng 0 nhưng nằm sát tâm. |
| **TV-06** | `Không thể tin được là mình không tạch, hết hồn chim én!` | **Phủ định kép / Lóng** | Chứa từ tiêu cực "tạch" (trượt) nhưng bị phủ định "không tạch". Kỳ vọng: Ra nhãn Surprise hoặc Enjoyment. |
| **TV-07** | `😡😭🤬` | **Chỉ có Emoji (Test luồng)** | Kỳ vọng: Theo tài liệu, câu chỉ có emoji mặc định dùng tiếng Việt. Cần xem bước chuẩn hóa text có dịch đúng các icon này thành từ khóa Anger/Sadness hay không. |

## 2. Bộ Test Case Tiếng Anh (Mô hình RoBERTa VA)

**Mục đích:** Kiểm tra khả năng hồi quy trực tiếp ra tọa độ Valence (V) và Arousal (A), không bị phụ thuộc vào phân loại cảm xúc tĩnh.

| Mã | Văn bản nhập nguyên câu | Đặc điểm / Độ khó | Hướng V/A mong đợi & Điều cần quan sát |
| :--- | :--- | :--- | :--- |
| **EN-01** | `This exam is literally sending me, I'm dead 💀` | **Slang Gen Z / Ẩn dụ** | Chứa từ "dead" và icon đầu lâu nhưng mang nghĩa sốc/hài hước. Kỳ vọng: Arousal dương (kích thích cao), Valence có thể hỗn hợp. |
| **EN-02** | `Oh fantastic, another flat tire in the pouring rain. Best day ever.` | **Mỉa mai (Sarcasm)** | Chứa toàn từ tích cực (fantastic, best) nhưng ngữ cảnh cực kỳ tồi tệ. Kỳ vọng: Valence âm (tiêu cực), Arousal dương (bực bội/kích thích). Nếu V dương là model phân tích sai. |
| **EN-03** | `It is what it is. I guess I'll just keep moving forward.` | **Cam chịu / Trống rỗng** | Cảm xúc rất mơ hồ, buông xuôi. Kỳ vọng: Valence hơi âm hoặc trung tính, Arousal âm (năng lượng rất thấp). |
| **EN-04** | `I'm so exhausted I could cry, but I finally finished the marathon!` | **Cảm xúc mạnh đối lập** | Mệt mỏi rã rời (Arousal thấp, Valence âm) nhưng thành tựu to lớn (Valence dương, Arousal cao). Kỳ vọng: Đánh giá cách model cân bằng V/A qua phép hồi quy (regression). |
| **EN-05** | `Not exactly the worst movie, but definitely lacking flavor.` | **Phủ định nhẹ nhàng** | Tường thuật đánh giá. Kỳ vọng: V hơi âm, A thấp. |
| **EN-06** | `I luv u nhìu lắm` | **Pha trộn ngôn ngữ (Test luồng Mobile)** | Vừa Anh vừa Việt lóng. Kỳ vọng: Kiểm tra xem Mobile tự động nhận diện đưa vào tiếng Việt hay tiếng Anh. Đảm bảo backend không văng ngoại lệ (exception). |
| **EN-07** | `idk tbh ngl it was mid` | **Viết tắt (Acronyms)** | Chứa toàn từ viết tắt (I don't know, to be honest, not gonna lie, mid). Kỳ vọng: V/A tiệm cận 0 (trung tính/nhàm chán). Cần xem model xử lý văn bản phi tiêu chuẩn ra sao. |

## 3. Hướng dẫn thực thi và ghi nhận

Để đảm bảo tính chính xác của các ca thử hệ thống này:
1. Mở Web người dùng, bật **DevTools -> Network**.
2. Thực hiện dự đoán, kiểm tra kỹ trường `language` và `modelVersion` trong Payload/Response của API `/api/MoodAnalysis/valence-arousal`.
3. Đối với các ca tiếng Việt, đặc biệt chú ý giá trị `accepted`. Nếu trả về `false`, ghi nhận lại mức `confidence` để làm cơ sở tinh chỉnh ngưỡng 0.755 sau này.