# Cập nhật giao diện và trải nghiệm mobile — 24/10/2026

## Phạm vi

Thực hiện hai nhóm đã được duyệt: nâng giao diện các màn hiện có và tối ưu cách tải/hiển thị trong Flutter. Giữ năm tab Trang chủ, Khám phá, Thư viện, Lịch sử, Cá nhân. Cơ sở đối chiếu là đề cương CNTT-KLCN318 và các luồng nghiệp vụ đang có trong project.

Đợt này không triển khai các hạng mục mới cần duyệt riêng: quên mật khẩu, đăng nhập mạng xã hội, push notification, thống kê năm, model tiếng Việt, nguồn nội dung xu hướng. Mục cá nhân hoá đã bỏ trước đó không được đưa trở lại.

## Các thay đổi

| Khu vực | Kết quả |
|---|---|
| Giao diện chung | Theme tối/tím thống nhất; màu chữ, card, biểu mẫu, popup, nút và thanh điều hướng dùng cấu hình chung. Hiệu ứng xuất hiện 220 ms, tôn trọng tuỳ chọn giảm chuyển động. |
| Trang chủ | Giữ quiz emoji và quote theo ngày; Việt hoá tên các mood chuẩn ở lớp hiển thị; giữ tối đa hai bài hát và hai phim. Ảnh bìa nhạc vuông, poster phim dọc. |
| Khám phá | Nhập mô tả → xem kết quả → xác nhận → nhận gợi ý. Diễn giải trạng thái AI bằng tiếng Việt; chi tiết model/nhãn được đặt trong mục mở rộng. Phim/nhạc/cả hai và lựa chọn thủ công vẫn hoạt động. |
| Thư viện | Phim dạng lưới, nhạc dạng hàng. Giữ lọc thể loại và hiển thị thêm; bổ sung tìm theo đạo diễn từ metadata hiện có. Có kéo để làm mới. |
| Chi tiết | Header gọn với ảnh/tên/đạo diễn hoặc nghệ sĩ, đưa player/trailer lên trước phần mô tả. Giữ Spotify embed, YouTube embed và nút mở ứng dụng gốc. Đánh giá vẫn nằm trong trang chi tiết. |
| Lịch sử | Có hai mục Tâm trạng và Hoạt động; nhóm theo ngày. Biểu đồ các bản ghi cũ được giữ trong phần mở rộng. Có thể mở lại nội dung từ lịch sử và giữ recommendationId nếu bản ghi có lưu. |
| Thống kê | Tổng quan số liệu ở đầu; chuyển giữa Tâm trạng và Giải trí. Biểu đồ có mốc điểm/ngày, chú giải, thao tác chọn ngày và nút ngày trước/sau. Giữ khoảng 7/30/90 ngày, dữ liệu kỳ trước và phép tổng hợp của API. |
| Tài khoản | Giữ sửa tên, ảnh từ camera/thư viện, đổi email xác nhận mật khẩu, đổi mật khẩu và đăng xuất. Thêm hiện/ẩn mật khẩu, nhãn luôn nổi, popup dùng đủ chiều rộng màn hình trong giới hạn theme, kiểm tra độ dài tên/email/mật khẩu theo API, dự phòng ảnh hỏng và giữ vị trí cuộn. Lỗi biểu mẫu xuất hiện trong popup, giữ dữ liệu khi API từ chối. |
| Đăng nhập/đăng ký | Form thống nhất, câu chữ tiếng Việt, hỗ trợ autofill và thao tác tiếp/done trên bàn phím. Giữ chính sách mật khẩu và API hiện có. |
| Trạng thái tải/lỗi | Placeholder tĩnh, trạng thái rỗng và thử lại nhất quán. Giữ dữ liệu cũ khi tải lại; lỗi làm mới lịch sử được thử lại ở đúng trang thay vì tải nhầm trang tiếp. |

## Bổ sung cho Cá nhân sau khi được duyệt

- **Đánh giá của tôi:** xem lịch sử đánh giá có ảnh bìa/poster, tên, loại nội dung, số sao, nhận xét và ngày đánh giá; phân trang và thử lại giữ trang cũ. Bấm thẻ mở chi tiết với recommendationId gốc nếu có. Không đặt thao tác chấm sao ngoài trang chi tiết.
- **Tổng quan 7 ngày:** bao gồm hôm nay theo múi giờ thiết bị; số ngày có ghi nhận mood, số contentId khác nhau có sự kiện `detail_open`, số bản ghi đánh giá trong khoảng. Mở cùng một nội dung nhiều lần vẫn tính một nội dung; tải player/mở trailer không tự tăng chỉ số này. Đây là 7 ngày gần nhất, không phải tuần lịch thứ Hai–Chủ nhật.
- **Nội dung đã lưu:** nút lưu/bỏ lưu ở chi tiết phim/nhạc; Thư viện tách Kho nội dung và Đã lưu. Đã lưu có lọc tất cả/phim/nhạc, phân trang, làm mới và bỏ lưu. Lối vào từ Cá nhân chọn đúng tab Thư viện đang có, giữ bộ lọc/cache catalog.
- Dữ liệu lưu trong `savedContents`, gắn accountId từ JWT, có index duy nhất account/content; PUT và DELETE có thể gọi lặp. Client chỉ đổi trạng thái sau khi server báo thành công. Xóa bookmark không xóa catalog, lịch sử hay đánh giá.
- Danh sách cá nhân đọc metadata hiện tại bằng truy vấn gộp và dùng snapshot dự phòng; không gọi hàng loạt TMDB/Spotify mỗi lần mở danh sách. Nội dung đã gỡ được báo không khả dụng; bookmark vẫn có thể bỏ lưu.
- Tổng quan và danh sách đã lưu chỉ cập nhật khi cần; không tải nền liên tục trong tab ẩn. Giao diện chưa bổ sung nhắc giờ hoặc xuất dữ liệu trong lượt này.
- API mới: `GET /api/SavedContent?page=1&type=Movie|Music` (bỏ type để xem tất cả), `GET/PUT/DELETE /api/SavedContent/{contentId}`. `GET /api/History/ratings` giữ các trường cũ và bổ sung content/available/recommendationId; `/api/Statistics` bổ sung moodDays/openedContents trong summary.

## Tối ưu đã thực hiện

- Các tab chỉ khởi tạo khi được mở lần đầu; tab đã mở giữ trạng thái và vị trí cuộn. `TickerMode` tắt ticker trong tab ẩn.
- Thư viện, gợi ý và lịch sử dùng danh sách/sliver dựng khi cuộn. Thư viện vẫn lấy catalog theo hợp đồng API cũ; đây chưa phải phân trang catalog ở server.
- Catalog từng loại được giữ trong vòng đời màn hình; kéo để làm mới hoặc nút tải lại lấy dữ liệu mới.
- Ảnh dùng thành phần chung với kích thước giải mã có giới hạn và bộ nhớ đệm ảnh có sẵn của Flutter. Avatar chỉ giải mã lại khi ảnh thay đổi.
- Biểu đồ tách repaint, chọn ngày thực hiện tại client và không gọi lại API.
- Route observer tháo player khi một màn hình khác phủ lên, khôi phục khi quay về; Spotify và YouTube xử lý việc app xuống nền. Không thay thuật toán bảo vệ tốc độ âm thanh Spotify.
- Tách `PageStorageKey` của từng phần mở rộng khỏi vị trí cuộn ở Lịch sử, Khám phá và Chi tiết. Ca tái hiện trước bản sửa xuất hiện đúng lỗi `type 'double' is not a subtype of type 'bool?'`; sau sửa, cuộn tạo thẻ mới, khôi phục thẻ đã mở và chuyển tab đều pass.
- Dọn tooltip đang mở khi đổi tab trên Web; dùng thành phần Flutter sẵn có, không thêm dependency vào app cho đợt nâng giao diện.

Trong bài test tải tab, lần mở Trang chủ chỉ gọi `/MoodMapping` cho quiz. `/Movies` chỉ gọi khi vào Thư viện; quay lại tab giữ nguyên ô tìm kiếm và không gọi lại catalog. Đây là bằng chứng giảm công việc khởi tạo, không phải phép đo FPS trên thiết bị thật.

## Bảo toàn nghiệp vụ và dữ liệu

- Tên mood tiếng Việt là nhãn hiển thị; ID và tên lưu ở backend không bị đổi.
- AI vẫn dùng model tiếng Anh hiện có; giữ bước xác nhận, analysisId và xử lý đổi bộ lọc để tránh dùng lại kết quả cũ.
- Không đổi thuật toán gợi ý, trọng số, mapping hoặc schema MongoDB.
- Mỗi chấm thống kê tâm trạng là trung bình VA của các bản ghi trong ngày; ngày thiếu dữ liệu để trống. Dữ liệu này phản ánh những tâm trạng đã được ghi nhận trong app.
- Tương tác vẫn là thao tác mở nội dung/player/liên kết, không phải số phút hoặc xác nhận đã nghe/xem xong.
- Lọc lịch sử áp dụng trên các trang đã tải và giao diện nêu rõ phạm vi. Thống kê dùng endpoint tổng hợp toàn khoảng ngày.
- Session đăng nhập vẫn theo cơ chế hiện có; không bổ sung lưu token lâu dài.

## Xác minh

- `flutter analyze`: sạch.
- `flutter test`: 48 tests pass, gồm 30 tests trước đó, 11 tests giao diện/hồi quy cho tải tab, dữ liệu thiếu ngày, phản hồi API đến muộn, xác nhận đổi email, vòng đời player, màn hình hẹp/chữ lớn/bàn phím, lịch sử/retry, cuộn và khôi phục thẻ mở rộng, avatar hỏng và luồng đổi mật khẩu; 7 tests mới cho lưu/bỏ lưu, phản hồi bộ lọc đến muộn, lỗi mạng và retry, metadata/provenance đánh giá, tổng quan khi tab ẩn, điều hướng Cá nhân → Đã lưu, màn hình hẹp và dữ liệu saved sai kiểu.
- `node --test test/spotify_audio_guard.test.cjs`: 1 test pass.
- Backend build: 0 warnings/errors; 544 HTTP/business assertions pass trên database MongoDB tạm. Có kiểm tra phân quyền tài khoản, phân trang và snapshot khi nội dung không còn khả dụng.
- Build APK debug và Web release thành công.
- Kiểm tra trực quan Flutter Web bằng Edge headless ở 390 × 844. Giao diện các màn chính đã được xem qua ảnh chụp; API, ảnh và iframe trong lần kiểm tra dùng fixture cục bộ.
- Widget test kiểm tra màn hình 320 px, chữ tăng 40% và popup tài khoản với bàn phím 300 px.
- Smoke APK debug trên `emulator-5554`: đăng nhập bằng tài khoản được cấp để test, bỏ qua quiz, tải lịch sử thật từ backend đang chạy, cuộn nhiều lần và mở danh sách trong thẻ Lãng mạn; không còn màn đỏ. Kiểm tra tab Cá nhân, ảnh/tên/email và hai popup tài khoản/đổi mật khẩu. Không gửi cập nhật hồ sơ hoặc đổi mật khẩu thật; kiểm thử gửi/reject/submit lặp dùng API giả.

Runner kiểm tra giao diện nằm ở `mobile-app/tool/ui_smoke.mjs`. Chạy từ root sau khi cài dependency Admin và build Web:

```powershell
Push-Location mobile-app
flutter build web --release
Pop-Location
node mobile-app/tool/ui_smoke.mjs
```

Runner dùng `playwright-core` đã có trong `admin-web/node_modules` và Microsoft Edge; chặn toàn bộ request `/api/**` bằng fixture, không đăng nhập/ghi vào backend thật. Ảnh chụp và kết quả nằm trong `.local-build/mobile-ui-review/`, được gitignore. Cần để cổng 5377 trống khi chạy runner.

Theo yêu cầu kết thúc task để đưa mã lên Git, dừng các lượt kiểm thử tiếp theo. Các kết quả 48 Flutter tests và 544 backend assertions ở trên là những lượt đã hoàn tất trước yêu cầu này. Luồng lưu → khởi động lại app → bỏ lưu trên Android chưa được kiểm tra trọn vẹn; sẽ thực hiện khi người dùng yêu cầu.

## Phần cần kiểm tra trên thiết bị thật

Đã kiểm tra các màn nêu trên bằng emulator Android; chưa kiểm tra điện thoại thật, chưa đo FPS/bộ nhớ hoặc xác minh lại âm thanh, quyền camera/thư viện và hành vi WebView trên Android/iOS thật. Smoke Web dùng iframe fixture nên không chứng minh Spotify/YouTube phát thật. Khi có thiết bị, chạy bản profile với API đúng của máy để đo cuộn/chuyển tab và kiểm tra nghe/xem:

```powershell
Push-Location mobile-app
flutter run --profile -d <device-id> --dart-define=API_BASE_URL=http://<IP-may-backend>:5170
Pop-Location
```
