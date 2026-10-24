# Báo cáo tiến độ tuần 09 — Mobile và quản lý ánh xạ tâm trạng

**Phạm vi:** đối chiếu hai đầu việc trong kế hoạch: (1) lịch sử xem/nghe và đánh giá mức độ hài lòng trên ứng dụng người dùng; (2) quản lý ánh xạ tâm trạng – nội dung trên giao diện quản trị.

## 1. Mobile App: lịch sử và đánh giá nội dung

Ứng dụng có hai phần lịch sử để người dùng theo dõi hoạt động:

- **Lịch sử tâm trạng và gợi ý:** hiển thị các lần người dùng yêu cầu gợi ý, tâm trạng, valence/arousal, thời gian và danh sách nội dung được đề xuất. Có bộ lọc 7 ngày, 30 ngày, 90 ngày hoặc toàn bộ; dữ liệu được tải theo trang và giới hạn theo tài khoản đang đăng nhập.
- **Lịch sử tương tác:** ghi lại việc mở trang chi tiết, tải Spotify player, mở liên kết ngoài hoặc mở trailer. Mỗi mục có tên nội dung, ảnh nếu còn khả dụng và thời điểm tương tác; người dùng có thể mở lại trang chi tiết.
- **Đánh giá:** trong trang chi tiết phim/nhạc, người dùng chọn từ 1 đến 5 sao và nhập nhận xét. Đánh giá gắn với nội dung và lần gợi ý tương ứng, sau đó được lưu theo tài khoản để quản trị viên tra cứu. Không đặt thao tác đánh giá nhanh ở danh sách gợi ý.
- **Cập nhật dữ liệu:** lịch sử được làm mới sau thao tác liên quan; giao diện hỗ trợ tải thêm, thử tải lại khi API lỗi và hiển thị trạng thái rỗng khi chưa có dữ liệu.

### Kết quả

Người dùng có thể xem lại các lần nhận gợi ý, tra cứu nội dung đã mở và gửi phản hồi sau khi xem thông tin hoặc trải nghiệm player/trailer. Việc phân biệt lịch sử tâm trạng với lịch sử tương tác giúp tránh hiểu nhầm một lần mở player hay liên kết là đã nghe/xem hết nội dung.

## 2. Website Quản trị: quản lý ánh xạ tâm trạng – nội dung

Chức năng được chia theo loại ánh xạ cần quản lý:

- **Ánh xạ nội dung phim/nhạc:** trong phần quản lý nội dung, quản trị viên có thể xem và điều chỉnh Valence (tiêu cực đến tích cực) và Arousal (bình tĩnh đến sôi nổi) trên thang −1 đến 1. Có gợi ý ban đầu theo thể loại; người quản trị vẫn có thể sửa trước khi lưu. Việc lưu ghi nhận trạng thái đã được rà soát và nguồn điều chỉnh.
- **Ánh xạ nhãn cảm xúc của AI sang tâm trạng:** trong dashboard AI Engineer, kỹ sư có thể gắn nhãn cảm xúc GoEmotions với tâm trạng đang có trong cơ sở dữ liệu, xem trạng thái ánh xạ và phạm vi nội dung. Thao tác duyệt tạo phiên bản mapping mới; backend kiểm tra revision, quyền truy cập và lưu lịch sử kiểm toán. Cấu hình này giúp kết quả phân tích cảm xúc được chuyển thành tâm trạng mà hệ thống dùng để gợi ý.
- **Phân quyền:** quản trị viên quản lý mood và tọa độ Valence/Arousal của nội dung; AI Engineer quản lý mapping nhãn cảm xúc AI và duyệt phiên bản mapping. Việc phân quyền tránh nhầm chỉnh sửa catalog nội dung với cấu hình mô hình.

### Kết quả và giới hạn hiện tại

Các trường Valence/Arousal và luồng mapping AI đã có giao diện cùng API lưu/đọc. Trong snapshot đối chiếu database được ghi ở tài liệu alignment ngày 07/10/2026, có 6/28 nhãn cảm xúc đã ánh xạ và 22 nhãn vẫn chưa ánh xạ; đây là số liệu theo snapshot đó, không khẳng định trạng thái database hiện tại. Mapping hiện là cấu hình thử nghiệm do người dùng/kỹ sư xác nhận, không phải kết quả mô hình tự học. Tọa độ cảm xúc của nội dung cũng cần được rà soát thêm để bảo đảm phù hợp với phim và nhạc thực tế.

## 3. Khó khăn và phần chưa hoàn thành

- Lịch sử tương tác chỉ chứng minh thao tác đã mở nội dung/player/trailer; hệ thống chưa đo thời lượng nghe/xem hoặc xác nhận nội dung đã phát/xem hoàn tất.
- Chất lượng tọa độ Valence/Arousal và độ phù hợp của từng nội dung chưa được đánh giá thủ công đầy đủ trên toàn bộ catalog.
- Một phần nhãn cảm xúc vẫn chưa được ánh xạ. Không nên tự gán các nhãn còn lại nếu chưa có quy tắc được rà soát, vì ánh xạ sai có thể làm sai tâm trạng và gợi ý.
- Các đánh giá sao và nhận xét là phản hồi về nội dung gợi ý; chúng chưa tự động trở thành nhãn huấn luyện cảm xúc hoặc tự thay đổi tham số mô hình.
- Chưa có bằng chứng trong phạm vi hai đầu việc này về thông báo đẩy hoặc cơ chế tự động tối ưu chiến lược gợi ý theo phản hồi.

## 4. Kiểm thử

Lần xác minh ngày 24/10/2026: 48 kiểm thử Flutter vượt qua, analyzer không báo vấn đề; backend build thành công và 544 kiểm tra HTTP/nghiệp vụ vượt qua trên MongoDB tạm. Đã build APK debug/Web release, kiểm tra giao diện Web bằng dữ liệu giả và kiểm tra các màn trên Android emulator với backend thật. Chưa kiểm tra trên điện thoại thật hoặc đo thời lượng nghe/xem hoàn tất.

## 5. Đánh giá tiến độ theo đề cương

| Đầu việc | Tiến độ |
|---|---|
| Mobile: lịch sử xem/nghe, đánh giá 1–5 sao và nhận xét | Đã triển khai luồng lịch sử tâm trạng/tương tác và đánh giá trong trang chi tiết; giới hạn là chưa theo dõi thời lượng nghe/xem hoàn tất. |
| Website quản trị: mapping tâm trạng – nội dung | Đã có chỉnh sửa Valence/Arousal cho nội dung và dashboard duyệt mapping nhãn AI sang mood; còn nhãn chưa ánh xạ và dữ liệu nội dung cần rà soát thêm. |

**Tài liệu tham chiếu:** [AI–database alignment results](AI_DATABASE_ALIGNMENT_RESULTS.md), [thống kê cá nhân và training local](PERSONAL_STATS_LOCAL_TRAINING.md).

