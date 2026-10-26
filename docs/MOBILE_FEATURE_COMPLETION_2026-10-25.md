# Hoàn thiện Mobile theo đề cương và đối chiếu ứng dụng cùng nhóm

Ngày cập nhật nội dung gốc: 25/10/2026. **Bổ sung 26/10/2026:** quên mật khẩu, Google Login và push Android đã có code/cấu hình; xem [hướng dẫn hiện tại](AUTH_NOTIFICATIONS_SETUP.md). Các trạng thái “chưa triển khai” trong bảng và kế hoạch dưới đây phản ánh lượt trước, được thay thế bởi bổ sung cuối tài liệu. Phạm vi: lựa chọn các phần còn thiếu phù hợp với Moodify, triển khai trên code hiện tại và giữ giao diện đã phục hồi từ `eeda4b6`, AI song ngữ, player, đánh giá và tài khoản.

## 1. Đối chiếu và lựa chọn

Đây là lựa chọn thiết kế cho Moodify dựa trên đề cương và tính năng của các sản phẩm tham khảo; không phải tuyên bố Moodify đã có quy mô dữ liệu hoặc chất lượng như những sản phẩm đó.

| Hạng mục | Mức phù hợp và quyết định | Kết quả trong lượt này |
| --- | --- | --- |
| Thống kê theo năm | Cao: giúp nhìn lại tâm trạng và thói quen qua các tháng, cùng hướng thống kê dài hạn của Daylio. | Đã triển khai năm lịch, biểu đồ theo tháng và so sánh kỳ trước. |
| Metadata phim đầy đủ | Cao: cần biết diễn viên, năm và thời lượng trước khi chọn xem; phù hợp hướng tra cứu phim của Letterboxd. | Đã triển khai model/DTO/API, TMDB, Admin và hiển thị Mobile. |
| Khám phá phổ biến/nội dung mới | Cao: thêm lựa chọn khi người dùng chưa muốn nhập mood; các hub khám phá là một hướng phổ biến ở Spotify và Letterboxd. | Đã triển khai lượt quan tâm trong Moodify và nội dung vừa cập nhật trong kho. |
| Phân tích tiếng Việt | Là chức năng cốt lõi và đã có trước lượt này. | Giữ PhoBERT cho Việt, RoBERTa VA cho Anh, tự nhận diện và dòng model dưới ô nhập. |
| Quên mật khẩu | Phù hợp, nên bổ sung khi chuẩn bị cho người dùng thật. Cần kênh gửi email xác minh; project chưa có dịch vụ email/SMTP được cấu hình. | Chưa triển khai trong lượt này. Cần email gửi, SMTP/API nhà cung cấp và thiết kế mã/link dùng một lần có hạn, giới hạn thử/gửi lại. |
| Đăng nhập Google/Facebook/số điện thoại | Google phù hợp để giảm thao tác đăng nhập; Facebook/SMS có thể làm sau khi cần. Cần provider/app ID và cấu hình callback/Android signing certificate tương ứng. | Chưa triển khai; giữ luồng email/tên đăng nhập hiện có. |
| Push và cài đặt thông báo | Phù hợp với nhắc ghi nhận mood nếu người dùng chủ động bật. Cần dự án Firebase, cấu hình app và phần gửi FCM phía server. | Chưa triển khai hoặc thêm nút bật không hoạt động. Thông báo cục bộ không được coi là push từ server. |

Nguồn tham khảo chính thức:

- [Daylio: Activity and Mood Statistics](https://daylio.net/faq/docs/daylio-faq/about/activity-and-mood-statistics/): có phân tích theo khoảng dài, gồm Last Year.
- [Spotify: Find playlists](https://support.spotify.com/bw/article/find-playlists/): các playlist khám phá như Discover Weekly và daylist.
- [Letterboxd: Welcome](https://letterboxd.com/welcome/?register=true): ghi nhật ký, đánh giá, watchlist, cơ sở dữ liệu cast/crew và khám phá phim.
- [TMDB: Movie details](https://developer.themoviedb.org/reference/movie-details): nguồn metadata phim theo ID.
- [Firebase: Flutter Cloud Messaging](https://firebase.google.com/docs/cloud-messaging/flutter/get-started): yêu cầu cấu hình dự án/app cho tích hợp FCM.

## 2. Thống kê theo năm

Cách dùng: **Cá nhân → mở thống kê → Theo năm → chọn năm**.

- Giữ các khoảng 7/30/90 ngày.
- Bổ sung năm lịch từ 2000 đến năm hiện tại, theo múi giờ thiết bị.
- Với năm đã kết thúc, biểu đồ có 12 tháng. Với năm hiện tại, chỉ hiển thị đến tháng hiện tại.
- Mỗi điểm cảm xúc là trung bình các bản ghi thực tế trong tháng; tháng không có mood giữ `null`, không ép thành 0.
- Giữ số ngày có mood/ngày hoạt động theo ngày thực tế; không tính số tháng thành số ngày.
- Hỗ trợ năm nhuận. Năm hiện tại so sánh cùng giai đoạn năm trước; năm đã kết thúc so sánh năm trước đầy đủ.
- Biểu đồ có chú thích tháng và nút tháng trước/sau. Tương tác phản ánh thao tác ghi nhận trong app, chưa phải số phút xem/nghe.

API:

```text
GET /api/Statistics?days=7|30|90&offsetMinutes=420
GET /api/Statistics?year=2024&offsetMinutes=420
```

Phản hồi thêm `year`, `granularity=day|month`. Trường `days` cho năm phản ánh số ngày trong khoảng thực tế; `trend` của năm dùng ngày đầu tháng làm khóa. JWT tiếp tục giới hạn dữ liệu theo tài khoản.

## 3. Metadata phim

- Bổ sung `releaseYear`, `runtimeMinutes`, `cast` trong model phim, DTO và schema MongoDB.
- TMDB lookup nhận thêm runtime và danh sách diễn viên, ngoài đạo diễn, poster, thể loại, mô tả, rating và trailer đã có.
- Admin → Movies → Add/Edit → **Cast, year & runtime**: chỉnh và lưu các trường mới; thao tác tìm TMDB tự điền dữ liệu trả về. Phần này đóng/mở để popup gọn.
- Mobile → chi tiết phim: hiển thị năm, thời lượng bằng phút, diễn viên chính khi nguồn có dữ liệu.
- Thời lượng nhạc vẫn là giây; không dùng runtime phim thay vào trường nhạc.
- Phim cũ thiếu metadata được bổ sung ở phản hồi chi tiết từ TMDB bằng TMDB ID/IMDb ID, khi backend có token hợp lệ. Cách này không tự ghi đè database hay điểm V/A đã duyệt.
- Cache metadata thành công 6 giờ; lần thất bại được cache ngắn. Thời gian chờ bổ sung giới hạn 3 giây; nếu nguồn lỗi hoặc ID cũ không hợp lệ, vẫn trả dữ liệu phim đang có.
- Trường đã có giá trị được giữ; không bịa thông tin thiếu. Các phim không có ID nguồn hoặc TMDB không có dữ liệu vẫn có thể thiếu trường.

## 4. Nội dung nổi bật

Cách dùng: **Khám phá → biểu tượng xu hướng ở góc trên → Nội dung nổi bật**.

Có hai mục, cùng bộ lọc Tất cả/Phim/Nhạc:

1. **Được quan tâm:** nội dung trong kho được các tài khoản mở trang chi tiết trong 7 ngày gần nhất. Một tài khoản mở lại cùng nội dung chỉ tính một lần; tải player hoặc mở liên kết ngoài không cộng thêm vào chỉ số này.
2. **Vừa cập nhật:** xếp theo `updatedAt` của catalog. Đây là thời điểm cập nhật trong Moodify, không phải ngày phát hành.

Danh sách ghi rõ nguồn Moodify; không gọi đây là bảng xếp hạng toàn cầu hoặc Spotify/TMDB trending. Khi chưa có lượt quan tâm, giao diện báo rỗng và hướng người dùng xem Vừa cập nhật. Bấm một mục mở chi tiết đang có để dùng trailer/player/đánh giá.

API:

```text
GET /api/Discovery?mode=popular|updated&type=Movie|Music&limit=12
```

Bỏ `type` để xem cả hai. API cần JWT, giới hạn tối đa 50 mục, cache 1 phút, trả dữ liệu tổng hợp và không công khai danh tính người tương tác. Nội dung đã xóa không được đưa vào danh sách. Lượt mở danh sách không tự tạo lịch sử nghe/xem hoặc lần gợi ý mới.

## 5. Xác minh và phạm vi dữ liệu

- `flutter analyze --no-pub`: sạch.
- 62 kiểm tra Flutter pass, gồm năm/tháng, metadata và phản hồi khám phá đến muộn cùng các hồi quy hiện có.
- 571 kiểm tra HTTP/nghiệp vụ pass trên database `moodify_test_<GUID>` riêng tại MongoDB local. Có kiểm tra năm nhuận 2024, đổi năm theo múi giờ, trung bình tháng, thiếu dữ liệu, phân quyền, lưu/đọc metadata và lượt quan tâm duy nhất.
- Database kiểm thử được dọn theo runner. Không chạy migration hoặc cập nhật metadata hàng loạt trên database chính trong lượt này.
- Bản sao file trước triển khai nằm trong `.local-backups/mobile-completion-20261025-214856/`, được Git bỏ qua.

Các con số kiểm thử này không chứng minh chất lượng mô hình AI hoặc đã nghe/xem hết nội dung. Chưa đo hiệu năng trên điện thoại thật trong lượt này.

## 6. Các phần cần cấu hình khi làm tiếp

- Quên mật khẩu: chọn nhà cung cấp email/SMTP, địa chỉ gửi và cách mở link/mã trên app; không đặt mật khẩu mới chỉ bằng việc biết email.
- Google sign-in: OAuth client ID, package name, signing SHA và xác minh token ở backend; chưa cần triển khai đồng thời cả Facebook lẫn SMS.
- Push: dự án Firebase, cấu hình Android/iOS, đăng ký token theo thiết bị/tài khoản, xử lý token hết hạn và quyền nhận thông báo. Người dùng có thể chủ động bật/tắt và chọn lịch nhận.

Không đưa các khóa dịch vụ vào Flutter hoặc Git. Các mục trên là kế hoạch còn lại, không tính là chức năng đã hoàn thành.

### Xác minh bổ sung cuối lượt

- 14 kiểm tra hợp đồng TMDB pass, gồm bổ sung phim cũ, giữ metadata/V/A đã có, dùng cache và bỏ qua ID nguồn không hợp lệ.
- Kiểm tra TMDB thật chỉ đọc pass: tìm kiếm, IMDb/TMDB ID, đạo diễn, diễn viên, năm phát hành, thời lượng, poster và trailer chính thức; không ghi MongoDB.
- Build backend/tests: 0 warning/error. Admin `npm run build` thành công. APK debug mới build thành công tại `mobile-app/build/app/outputs/flutter-apk/app-debug.apk`.
- Chưa chạy lại APK mới trên thiết bị thật trong lượt này. Khởi động lại backend và app để dùng các API/màn mới.

## Bổ sung 26/10/2026: tài khoản và thông báo

Đã triển khai quên mật khẩu bằng mã email, đăng nhập/liên kết Google bằng Firebase Auth, cài đặt và gửi thông báo FCM trên Android. Gmail SMTP, service account và Web OAuth client từ cấu hình được cung cấp đã được nạp; khóa chỉ lưu local, không vào Git.

- Mã khôi phục có hạn, một lần dùng, giới hạn thử sai và gửi lại; đặt mật khẩu mới vô hiệu phiên cũ.
- Google xác minh token ở backend; liên kết email cũ cần mật khẩu hiện tại, không tự nâng quyền quản trị.
- Push mặc định tắt, có quyền hệ điều hành, lịch nhắc/ngừng nhận, đăng ký theo thiết bị và chống gửi lặp.
- 67 Flutter tests và 612 HTTP/nghiệp vụ assertions pass; analyzer sạch; backend/tests và APK debug build thành công.
- Xác thực Gmail/Firebase thật pass nhưng không gửi email/push trong lượt kiểm tra. Chưa xác minh chọn Google account và nhận email/push thật trên thiết bị.

Các mục quên mật khẩu/Google/push trong bảng và kế hoạch cũ phía trên không còn là phần chưa viết code. Cách chạy lại backend/app và phạm vi còn cần thử được ghi tại [AUTH_NOTIFICATIONS_SETUP.md](AUTH_NOTIFICATIONS_SETUP.md).
