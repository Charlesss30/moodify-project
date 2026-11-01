# Quên mật khẩu, Google Login và thông báo đẩy

Cập nhật: **26/10/2026**. Hướng dẫn cho bản Android của Moodify và ASP.NET Core backend. Code đã tích hợp với Gmail SMTP, Firebase Authentication và Firebase Cloud Messaging (FCM); cần cấu hình riêng trên từng máy triển khai.

## 1. Những gì đã triển khai

| Chức năng | Điểm truy cập | Hành vi |
| --- | --- | --- |
| Quên mật khẩu | Đăng nhập → Quên mật khẩu | Gửi mã qua email đã đăng ký, nhập mã và mật khẩu mới, quay về đăng nhập. |
| Đăng nhập Google | Đăng nhập → Tiếp tục với Google | Chọn Google account; backend xác minh Firebase ID token rồi cấp phiên Moodify. |
| Liên kết tài khoản cũ | Đăng nhập Google bằng email đã có | Yêu cầu mật khẩu Moodify hiện tại trước khi liên kết. |
| Thiết lập mật khẩu cho tài khoản Google | Cá nhân → Thiết lập mật khẩu | Xác nhận qua email bằng luồng khôi phục; không yêu cầu một mật khẩu cũ chưa từng tạo. |
| Cài đặt thông báo | Cá nhân → Thông báo | Chủ động bật/tắt, chọn nhắc mood hằng ngày, nhắc nhìn lại tuần và giờ nhận. |
| Thử gửi push | Thông báo → Gửi thông báo thử | Gửi từ backend qua FCM đến thiết bị đã đồng ý; không dùng thông báo cục bộ thay thế. |

Đăng nhập email/mật khẩu, tài khoản, AI song ngữ và các luồng nghe/xem/đánh giá hiện có được giữ lại. Chức năng thông báo mặc định tắt. Chưa bổ sung Facebook hoặc SMS.

## 2. File cấu hình và thiết lập máy mới

Các file thật sau đây đã được `.gitignore` loại khỏi Git:

| File | Mục đích |
| --- | --- |
| `mobile-app/android/app/google-services.json` | Firebase Android app: project, package, OAuth client và tài nguyên native. |
| `backend/firebase-adminsdk.json` | Service account riêng của backend, dùng xác minh Google và gửi FCM. |
| `backend/appsettings.Development.json` | Gmail SMTP, Firebase backend, JWT key và cấu hình local khác. |
| `mobile-app/firebase-config.local.json` | Dart defines Android tùy chọn, do script tạo; Android không bắt buộc dùng file này. |

Android config và service account phải thuộc **cùng Firebase project**. `package_name` phải khớp `applicationId` trong `mobile-app/android/app/build.gradle.kts`; hiện là `com.example.moodify_application`.

Tại Firebase Console:

1. Tạo project và thêm Android app bằng đúng package.
2. Authentication → Sign-in method → bật Google và chọn email hỗ trợ.
3. Project settings → Android app → thêm SHA-1/SHA-256 của khóa ký APK đang dùng. Máy khác có debug keystore khác nên phải thêm chứng chỉ của máy đó.
4. Tải lại `google-services.json`, lưu đúng đường dẫn trong bảng. File phải có Web OAuth client sau khi bật Google.
5. Project settings → Service accounts → tạo khóa riêng, lưu thành `backend/firebase-adminsdk.json`. Khóa này chỉ ở backend.
6. Emulator cần Google Play services; Firebase/Google và SMTP cần truy cập Internet.

Tại Gmail: bật xác minh hai bước và tạo **App password** cho ứng dụng; không dùng mật khẩu đăng nhập Gmail thông thường.

Chạy từ root project:

```powershell
# Đồng bộ Firebase, giữ nguyên cấu hình Gmail/TMDB/Spotify/AI đang có:
.\scripts\configure-mobile-services.ps1

# Máy mới hoặc muốn thay Gmail: nhập app password trong ô ẩn được script hỏi:
.\scripts\configure-mobile-services.ps1 -GmailAddress 'your-address@gmail.com'
```

Script kiểm tra project/package/OAuth client trước khi ghi, giữ các section cấu hình khác, sao lưu file cũ dưới `.local-backups/mobile-services-*`, rồi cập nhật các file local. Khi `Jwt:Key` chưa có, script tạo khóa ngẫu nhiên và giữ ổn định qua những lần chạy sau để mã khôi phục không mất hiệu lực chỉ vì backend restart. Script không tự gửi email/push hay thay đổi Firebase Console.

Mẫu không chứa credential nằm ở `backend/appsettings.Development.example.json`. Các khóa được đọc bởi backend:

```text
Email:Host = smtp.gmail.com
Email:Port = 587
Email:Username = địa chỉ Gmail gửi
Email:From = cùng địa chỉ Gmail gửi
Email:Password = app password, bỏ khoảng trắng
Firebase:ProjectId = Firebase project ID
Firebase:ServiceAccountPath = firebase-adminsdk.json
Firebase:GoogleServerClientId = Web OAuth client ID
Jwt:Key = khóa ngẫu nhiên tối thiểu 32 byte
```

Có thể thay bằng biến môi trường tương ứng như `Email__Password`, `Firebase__ServiceAccountPath`, `Jwt__Key`. Đường dẫn service account tương đối được tính từ thư mục `backend`. Chạy local theo launch profile `http` dùng môi trường Development để đọc file Development.

Các file local và bản sao chứa credential không đi kèm khi người khác clone Git; người nhận cần cấu hình Firebase/Gmail của họ hoặc nhận cấu hình riêng qua kênh phù hợp. Chỉ push source và file example.

## 3. Chạy lại sau khi cấu hình

Cần **khởi động lại backend** và **Stop rồi Run app hoàn toàn trong Android Studio**; hot reload không nạp được Firebase native plugin hoặc tài nguyên `google-services.json`.

Nếu dùng runner:

```powershell
.\Stop.ps1
.\Start.ps1
```

Nếu chạy thủ công, dừng terminal backend cũ bằng `Ctrl+C` rồi:

```powershell
dotnet run --project backend/mood_recommendation.csproj --launch-profile http
```

Trong terminal khác:

```powershell
cd mobile-app
flutter pub get
flutter run -d <device-id>
```

Thay `<device-id>` bằng ID từ `flutter devices`. Android emulator dùng API `http://10.0.2.2:5170`. Android Studio Run đọc trực tiếp Firebase native resources; không cần tự nhập Dart defines. Để kiểm thử ba tính năng tài khoản/thông báo không cần model AI chạy.

Kiểm tra backend đã nhận cấu hình mà không gửi thư/push:

```powershell
Invoke-RestMethod http://localhost:5170/api/Auth/providers
```

Kỳ vọng `passwordReset: true`, `google: true` và có Google client ID công khai. Kết quả này cho biết cấu hình đã được nạp, chưa chứng minh Google account hoặc thiết bị đã đăng nhập/nhận thông báo thành công.

## 4. Cách thử thực tế

### Quên mật khẩu

1. Chọn **Quên mật khẩu**, nhập email của tài khoản thử có quyền truy cập hộp thư.
2. Bấm gửi, kiểm tra Inbox/Spam và nhập mã 6 số cùng mật khẩu mới tối thiểu 8 ký tự.
3. Đăng nhập bằng mật khẩu mới; mật khẩu cũ và các phiên JWT cũ của tài khoản phải bị từ chối.
4. Mã có hạn 10 phút, dùng một lần; tối đa 5 lần nhập sai. Gửi lại cần chờ 60 giây. Dùng mã trong email tương ứng với yêu cầu đang mở.
5. Với email không tồn tại, app vẫn hiện thông báo chung, không tiết lộ email nào có tài khoản.

Đây là thao tác đổi mật khẩu thật. Dùng tài khoản thử phù hợp; hệ thống không tự đổi mật khẩu khi chỉ nhập địa chỉ email.

### Đăng nhập Google

1. Chọn **Tiếp tục với Google** và chọn tài khoản.
2. Email mới tạo tài khoản Moodify vai trò User. Chỉ khi backend chấp nhận token thì app mới vào Trang chủ.
3. Email đã thuộc tài khoản Moodify chưa liên kết sẽ hỏi mật khẩu hiện tại để liên kết. Nhập sai không đăng nhập và không sửa liên kết.
4. Hủy màn hình chọn Google account quay lại form; không coi là đăng nhập thành công.
5. Admin/AIEngineer vẫn dùng luồng đăng nhập quản trị; Google không tự nâng quyền.

Backend xác minh chữ ký, project, hạn token, `email_verified` và nhà cung cấp `google.com`; không tin email/UID do client tự khai báo.

### Thông báo đẩy

1. Đăng nhập Android → **Cá nhân → Thông báo**.
2. Bật **Nhận thông báo** và cấp quyền hệ điều hành khi được hỏi. Nếu bị từ chối, vào Android Settings → Apps → Moodify → Notifications để bật lại.
3. Chọn nhắc tâm trạng hằng ngày, nhìn lại tuần vào Chủ nhật, và giờ nhận.
4. Bấm **Gửi thông báo thử** khi backend chạy và thiết bị có Internet.
5. App đang mở: thấy lời nhắc trong app với nút **Mở**. App ở nền: kiểm tra khay thông báo Android.
6. Chạm lời nhắc mood mở Khám phá; lời nhắc tuần mở Thống kê. Nếu chưa đăng nhập, app chờ đăng nhập trước khi điều hướng.
7. Tắt nhận thông báo hoặc đăng xuất để ngừng đăng ký nhận trên thiết bị đó.

Thông báo tuần là lời nhắc mở thống kê, chưa có nội dung tóm tắt cá nhân tự sinh trong push. Nếu cả hai loại bật, Chủ nhật có thể nhận hai lời nhắc.

Bộ lịch chạy ngay trong backend .NET, kiểm tra mỗi phút. Backend phải tiếp tục chạy đến giờ gửi; không cần để app ở tiền cảnh. Chống gửi lặp theo tài khoản/thiết bị/loại/ngày địa phương; nếu backend mở muộn trong ngày, có thể gửi một lời nhắc bù. Không bù các ngày backend đã tắt. Múi giờ lưu theo offset lúc cài đặt; khi đổi múi giờ, mở lại cài đặt và lưu giờ nhận.

Thông báo thử báo thành công khi FCM **chấp nhận yêu cầu**, chưa chứng minh Android đã hiển thị. Mạng, quyền hệ điều hành, chế độ tiết kiệm pin, force-stop app và Google Play services có thể ảnh hưởng việc nhận.

## 5. API và dữ liệu

| API | Vai trò |
| --- | --- |
| `GET /api/Auth/providers` | Trạng thái cấu hình và public OAuth client ID. |
| `POST /api/Auth/forgot-password` | Nhận email, trả challenge ID và thời gian chờ; không trả mã. |
| `POST /api/Auth/reset-password` | Challenge ID + code + mật khẩu mới. |
| `POST /api/Auth/google` | Firebase ID token; thêm currentPassword khi liên kết tài khoản cũ. |
| `GET/PUT /api/Notifications/settings` | Đọc/lưu cài đặt của chính tài khoản đã đăng nhập. |
| `POST /api/Notifications/devices` | Đăng ký FID của installation vào tài khoản đã đồng ý. |
| `POST /api/Notifications/unregister` | Gỡ installation thuộc chính tài khoản đó. |
| `POST /api/Notifications/test` | Gửi thử, giới hạn 3 yêu cầu/phút/tài khoản. |

SDK Firebase Messaging 17 trong repo dùng `register/onRegistered/unregister` và FID; backend dùng `Message.Fid`. Không trộn API FID này với hướng dẫn `getToken/onTokenRefresh` cũ.

MongoDB thêm các collection `passwordResets`, `notificationPreferences`, `pushDevices`, `pushReceipts`. Reset challenge có TTL; mã chỉ lưu dạng HMAC. Receipt chống lặp có TTL 8 ngày, installation không cập nhật sau 90 ngày được dọn. Account thêm Google UID, trạng thái có mật khẩu và phiên bản phiên đăng nhập. Không lưu mật khẩu Google hoặc mật khẩu Gmail trong MongoDB.

## 6. Xác minh đã thực hiện và giới hạn

- Backend và integration tests build: **0 warning/error**.
- APK debug mới build thành công với Firebase native plugins: `mobile-app/build/app/outputs/flutter-apk/app-debug.apk`.
- **67 Flutter tests pass**, `flutter analyze --no-pub` sạch.
- **612 kiểm tra HTTP/nghiệp vụ pass** trên MongoDB tạm riêng. Gồm hết hạn/sai mã/dùng lại mã, vô hiệu phiên cũ, liên kết Google, phân quyền, opt-in/opt-out, quyền sở hữu installation và chống gửi lặp.
- Test email dùng pickup `.eml` trong môi trường kiểm thử riêng, không gửi SMTP thật. Test Google/FCM dùng đối tượng giả; không đăng nhập Google account thật trong các test này.
- Xác thực thật Gmail SMTP qua TLS và Firebase service account OAuth thành công; Web OAuth client đã có trong Android config. Các lượt này **không gửi email hoặc push thật**.
- Chưa xác minh trọn luồng chọn Google account và nhận email/push trên thiết bị trong lượt này. Cần thực hiện mục 4 để xác nhận delivery thực tế.
- Push triển khai cho **Android**. Chưa cấu hình iOS hoặc Web push.
- Google sign-in có nhánh Flutter Web popup nhưng chưa cấu hình/test Firebase Web app trong lượt này. Web cần Firebase app ID/API key đúng loại Web, authorized domain và Dart defines tương ứng; không dùng file defines Android làm cấu hình Web.
- Session Moodify vẫn theo cơ chế bộ nhớ hiện có; mở lại app có thể cần đăng nhập. Bấm push khi chưa đăng nhập được lưu điều hướng chờ.

## 7. Chi phí và xử lý lỗi

Google sign-in phù hợp gói Firebase Spark trong giới hạn miễn phí; FCM được liệt kê là dịch vụ không thu phí. Tích hợp này không tự bật Cloud Functions hay chuyển dự án sang gói trả phí. Gmail SMTP cá nhân dùng được trong hạn mức gửi của tài khoản; khi đưa lên quy mô lớn cần chọn dịch vụ gửi email phù hợp. Xem [Firebase pricing](https://firebase.google.com/pricing), [Gmail sending limits](https://support.google.com/mail/answer/22839?hl=vi) và [Gmail App passwords](https://support.google.com/accounts/answer/185833?hl=vi).

| Hiện tượng | Kiểm tra |
| --- | --- |
| Google chưa sẵn sàng | Đã restart backend/app; file config đúng project/package; Google enabled; có Web OAuth client. |
| Không chọn/đăng nhập Google được trên máy khác | SHA của đúng debug/release keystore đã thêm; tải lại JSON; emulator có Google Play services. |
| Không nhận mã email | Email đúng tài khoản active, chờ gửi, kiểm tra Spam; app password/SMTP/TLS và log backend. |
| Mã không hợp lệ | Đúng yêu cầu đang mở, chưa quá 10 phút, chưa dùng hoặc sai quá 5 lần; gửi mã mới sau thời gian chờ. |
| Thông báo chưa sẵn sàng | Android build có Firebase config, backend có service account đúng project, hai phía được khởi động lại. |
| FCM chấp nhận nhưng chưa thấy thông báo | Quyền Android, Google Play services, Internet, channel Moodify, tiết kiệm pin và trạng thái force-stop. |
| Không nhận đúng giờ | Backend phải chạy, loại thông báo đã bật, giờ/offset đúng; scheduler kiểm tra mỗi phút. |

Tài liệu [README](../README.md) là điểm bắt đầu chạy hệ thống. Các báo cáo trước ngày cập nhật này có thể mô tả trạng thái chưa triển khai ba tính năng trên.

## Sửa lỗi đăng nhập Google — 26/10/2026

Nguyên nhân đã tái hiện: Firebase Admin dùng Newtonsoft.Json cho claims lồng nhau (`JObject`). Code dùng System.Text.Json để serialize trực tiếp đối tượng đó, khiến giá trị `sign_in_provider` bị chuyển thành mảng rỗng. Lượt đầu gọi `GetString()` gây HTTP 500; bản sửa chỉ kiểm tra kiểu tiếp theo tránh 500 nhưng vẫn từ chối tài khoản hợp lệ vì giá trị đã bị mất.

Bản sửa hiện tại xuất JSON đúng bằng `JToken.ToString(Formatting.None)` khi claims là đối tượng Newtonsoft, sau đó mới đọc provider. Các đối tượng dictionary/JsonElement vẫn được xử lý theo kiểu tương ứng. Luồng vẫn bắt buộc Firebase Admin xác minh chữ ký, project và hạn token trước khi đọc claims, cùng `email_verified=true` và provider Google.

- Thêm fixture dùng `JsonConvert.DeserializeObject<Dictionary<string, object>>` giống kiểu dữ liệu SDK. Fixture xác nhận claims lồng nhau là JObject.
- Fixture đã thất bại trên bản sửa trước và pass sau khi sửa serializer.
- 34 kiểm tra verified claims và 646 kiểm tra API/nghiệp vụ pass trên DB tạm; backend/tests build sạch.
- Backend local đã được khởi động lại với bản sửa mới; giữ nguyên inference và dashboard. Không cần build lại app.
- Chưa xác minh lần chọn tài khoản Google thật sau bản sửa; cần bấm Google lại trên app để kiểm tra trọn luồng.

Chạy kiểm tra parser riêng, không dùng Firebase hoặc MongoDB:

```powershell
dotnet run --project tests/Moodify.Integration.csproj -- --firebase-contract
```

Tham khảo kiểu claims và thư viện JSON trong [mã nguồn Firebase Admin SDK](https://github.com/firebase/firebase-admin-dotnet/blob/master/FirebaseAdmin/FirebaseAdmin/Auth/FirebaseToken.cs). Không lưu hoặc đưa JWT/email thật vào fixture.

## Sửa đăng ký thông báo Android — 26/10/2026

Exception gốc trên emulator là `IllegalStateException: API disabled`: API `register()` của FCM cần opt-in FID trong Android Manifest. Trước đó luồng Dart đã dùng API FID mới nhưng Manifest thiếu cờ bắt buộc.

Đã thêm trong `<application>`:

```xml
<meta-data android:name="firebase_messaging_installation_id_enabled" android:value="true" />
```

Vẫn giữ `firebase_messaging_auto_init_enabled=false`, quyền hệ điều hành và cài đặt tài khoản mặc định tắt. Cờ mới cho phép gọi API FID, không thay thế sự đồng ý nhận thông báo của người dùng. Vì là thay đổi native, hot reload/hot restart không đủ; phải cài APK mới hoặc Stop/Run lại từ Android Studio.

Đối chiếu [Firebase Flutter FCM setup](https://firebase.google.com/docs/cloud-messaging/flutter/get-started) và [Android FirebaseMessaging.register](https://firebase.google.com/docs/reference/android/com/google/firebase/messaging/FirebaseMessaging#register()).

Xác minh sau sửa: APK debug build thành công, kiểm tra Manifest trong APK có cờ FID `true` và auto-init vẫn `false`. Đã cài đè APK trên `emulator-5554`, mở lại app và gọi API native đăng ký FCM thật qua debugger: `register()` hoàn tất thành công; sau đó `unregister()` cũng thành công để gỡ đăng ký kiểm tra. Không gửi push hoặc chỉnh cài đặt nhận thông báo trong database. Cần đăng nhập lại sau khi app khởi động lại; người dùng có thể bật nhận và tự bấm Gửi thông báo thử. Kiểm tra này xác nhận đăng ký FCM native, chưa chứng minh gửi/nhận push trọn luồng từ backend.
