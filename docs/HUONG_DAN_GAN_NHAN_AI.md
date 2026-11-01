# Anh làm phần dữ liệu đánh giá AI như thế nào?

Ngày 27/10/2026. Công cụ và file mẫu đã chuẩn bị sẵn. Anh làm nhãn; hệ thống chạy model và tính chỉ số sau. Không cần biết lập trình hoặc huấn luyện lại để làm bước này.

Xem [mapping và các loại nhãn đang dùng trong hệ thống](MAPPING_GAN_NHAN_AI_HIEN_TAI.md) để phân biệt điểm neo PhoBERT, mood ứng dụng và nhãn đánh giá độc lập.

## 1. Mở công cụ

1. Mở `tools/ai-label-review.html` bằng Edge/Chrome (nhấp đúp file).
2. Nhập mã của anh, ví dụ `reviewer-a`.
3. Chọn file `docs/ai-evaluation/review-starter.json`.
4. Đọc từng câu, chọn nhãn rồi nhấn **Tiếp**.
5. Nhấn **Tải bản đang làm** sau mỗi đợt khoảng 10–20 câu. Có thể dùng **Khôi phục bản lưu trên máy** để làm tiếp.

Không có dữ liệu gửi qua mạng. Mẫu khởi động có 48 câu (24 cặp Việt/Anh), do AI soạn để tập quy trình, tất cả `reviewed=false`. Mẫu này **chưa phải gold set độc lập đạt nghiệm thu**, chưa đủ số lượng đánh giá cuối cùng và không đại diện đầy đủ người dùng thực tế.

## 2. Điền gì cho mỗi câu?

### Cảm xúc tiếng Việt

Chọn một trong 7 nhãn VSMEC: giận, ghê tởm/phản cảm, vui thích, sợ/lo sợ, Other, buồn, ngạc nhiên. `Other` dùng khi không rõ sáu lớp còn lại; **không coi Other là Thư giãn**. Nếu câu pha cảm xúc, ghi lý do chọn cảm xúc chính hoặc để chưa review nếu chưa quyết định.

Model tiếng Anh dự đoán VA trực tiếp nên công cụ không bắt anh gán lớp VSMEC cho câu Anh.

### Mức tích cực — Valence

- `−1`: rất khó chịu/tiêu cực.
- `0`: trung tính.
- `+1`: rất dễ chịu/tích cực.

### Mức kích hoạt — Arousal

- `−1`: rất lắng dịu, ít năng lượng.
- `0`: mức vừa.
- `+1`: rất kích động, tỉnh táo hoặc nhiều năng lượng.

Hai trục khác nhau. Ví dụ minh họa cách hiểu, **không phải đáp án cho file mẫu**:

| Mô tả | Tích cực | Kích hoạt |
| --- | --- | --- |
| Bình yên, dễ chịu sau khi nghỉ | Dương | Âm |
| Hào hứng ăn mừng | Dương | Dương |
| Bực bội, muốn phản ứng ngay | Âm | Dương |
| Buồn bã, không muốn làm gì | Âm | Thường thấp |

Gán theo cảm xúc được diễn đạt trong câu, không theo thể loại nhạc/phim anh muốn hệ thống trả về. Câu Anh cần cả VA; câu Việt cần emotion, VA nên bổ sung nếu anh có thể đánh giá độc lập. Không chắc thì để trống và ghi lý do; đừng đánh dấu đã review.

### Mood của ứng dụng

Có thể chọn mood anh thấy phù hợp: vui vẻ, buồn, thư giãn, hào hứng, tập trung, lãng mạn, hoài niệm. Để trống nếu không khớp. Đây là nhãn riêng, không lấy mood mà model dự đoán làm đáp án.

Các ID trong công cụ là snapshot mood 27/10: 1–7 theo thứ tự trên. Nếu catalog mood thay đổi phải xuất lại danh sách, không tự đổi ID trong file cũ.

## 3. Review độc lập

- Anh gán lần đầu từ file mẫu gốc → xuất `labels-reviewer-a.json`.
- Nhờ một người khác hiểu ngôn ngữ gán từ **file mẫu gốc chưa có nhãn**, dùng mã `reviewer-b`.
- Giữ hai file riêng. Câu có nhãn khác nhau cần trao đổi/rà soát; không âm thầm chọn đáp án model hoặc trung bình mọi bất đồng.
- Lưu các file vào `docs/ai-evaluation/completed/` (đã gitignore), rồi báo mình đường dẫn để kiểm tra/ghép. Không gửi mật khẩu hoặc thông tin riêng tư vào câu.

Không đổi ID, cặp câu hoặc split validation/test trong lúc làm. Hai câu tương đương Việt/Anh đã được giữ cùng split.

## 4. Để có bộ đánh giá cuối cùng

Mẫu này giúp bắt đầu. Cần thêm câu tự viết/thu thập hợp lệ từ cách người dùng thực tế mô tả tâm trạng: không dấu, teencode, emoji, phủ định, lẫn cảm xúc, thư giãn/tập trung/hoài niệm. Đánh dấu rõ câu AI soạn và câu thu thập tự nhiên để báo cáo đúng phạm vi.

Protocol khởi đầu hướng tới ít nhất 100 câu mỗi ngôn ngữ; số lượng đó vẫn không tự bảo đảm chất lượng thống kê. Gán và review trước, chốt validation/tiêu chí rồi mới mở kết quả test. Không dùng 20 câu demo hoặc các câu đã train làm test độc lập.

## 5. Thu phản hồi thật cho MF/Hybrid

Đây là việc khác với gán nhãn cảm xúc:

1. Người dùng đăng nhập bằng tài khoản của họ.
2. Nhận gợi ý và mở chi tiết phim/nhạc.
3. Xem thông tin/trailer hoặc nghe thử, rồi đánh giá 1–5 sao theo trải nghiệm thật; nhận xét tùy chọn.
4. Thu thập qua thời gian để tách train/validation/test. Không tạo tài khoản/sao giả chỉ để đủ số lượng.

Hiện gate MF cần ít nhất 20 warm validation và 20 warm test pair; còn cần train đủ để user/item xuất hiện trước các mốc đó. Đủ 40 sao tổng cộng không tự đáp ứng yêu cầu này. Mình xử lý xuất dữ liệu, train và tính chỉ số; anh không cần tự chia database.

## 6. RL/bandit

Theo chỉ đạo hiện tại, RL/bandit không nằm trong phạm vi bắt buộc của lượt hoàn thiện này. Giữ feedback loop đã có; phần pilot/bandit hiện hữu tiếp tục tắt. Không ghi đã hoàn thành RL trong báo cáo hoặc thay đổi đề cương gốc.

Đã tiếp nhận bản nhãn v2 được người dùng xác nhận và chạy 48 câu đối chiếu: [baseline ngày 27/10/2026](AI_BASELINE_PHUONG_2026-10-27.md). Đây là nhãn AI được chấp nhận để chẩn đoán; phần gold độc lập vẫn chưa hoàn tất.
