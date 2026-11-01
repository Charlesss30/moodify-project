# Thông báo hệ thống Android — cập nhật 28/10/2026

## Hành vi hiện tại

Lời nhắc Moodify được hiển thị trong **ngăn thông báo Android**. Khi app đang mở, message FCM được đưa sang phần native để hiển thị notification hệ thống thay vì thanh nội dung SnackBar ở cuối app.

- Kênh `moodify_reminders_v2` dùng mức ưu tiên cao để hỗ trợ banner nổi.
- Bấm lời nhắc tâm trạng mở Khám phá; bấm nhìn lại tuần mở Thống kê.
- Nếu cần đăng nhập lại, ứng dụng giữ đích chờ và mở sau đăng nhập.
- Cùng message ID cập nhật cùng notification, không thêm hai bản.
- Tắt nhận hoặc đăng xuất sẽ dọn lời nhắc đang hiển thị.
- Data-only message không tự bị biến thành một thông báo nhìn thấy.
- Khi app ở background, FCM tiếp tục xử lý notification payload; phần foreground không tạo thêm bản thứ hai cho background.

Lịch nhắc, opt-in và backend FCM được giữ. Đây là lớp **hiển thị message đã nhận**, không thay FCM bằng một bộ hẹn giờ local, không tạo polling nền và không thêm thư viện Flutter mới.

Thanh “Đã gửi yêu cầu thông báo thử” sau khi bấm nút là xác nhận thao tác. Nội dung lời nhắc thực tế sẽ nằm ở thông báo Android.

## Cách dùng và chạy bản mới

1. Build/cài APK mới hoặc Stop/Run từ Android Studio; thay đổi Kotlin/Manifest không nhận qua hot reload.
2. Đăng nhập → Cá nhân → Thông báo → bật Nhận thông báo và cấp quyền Android.
3. Bấm Gửi thông báo thử, kiểm tra banner hoặc kéo ngăn thông báo xuống.
4. Bấm notification để mở đúng màn hình.

Backend cần chạy để gửi FCM/lời nhắc theo lịch. Thử trên emulator cần Google Play services, Firebase config hợp lệ và Internet như trước.

Android quyết định khả năng hiện banner theo quyền, mức ưu tiên kênh và cài đặt của thiết bị; không ép vượt lựa chọn của người dùng. Khi chuyển từ kênh cũ, code giữ trạng thái đã chặn và mức ưu tiên mà Android xác định là do người dùng đặt.

Kênh cũ `moodify_reminders` có importance thường. Android không cho nâng importance một kênh đã tạo bằng cách đăng ký lại cùng ID, nên có ID mới; không xóa kênh cũ để làm mất lựa chọn người dùng.

Tham khảo: [notification channels Android](https://developer.android.com/develop/ui/compose/notifications/channels), [foreground FCM Flutter](https://firebase.google.com/docs/cloud-messaging/flutter/receive-messages#foreground_and_notification_messages).

## Phần code được cập nhật

| File | Vai trò |
| --- | --- |
| [SystemNotificationBridge.kt](../mobile-app/android/app/src/main/kotlin/com/example/moodify_application/SystemNotificationBridge.kt) | Kênh, Android notification, tap intent, dọn lời nhắc |
| [MainActivity.kt](../mobile-app/android/app/src/main/kotlin/com/example/moodify_application/MainActivity.kt) | Gắn Flutter method channel và nhận tap intent |
| [system_notifications.dart](../mobile-app/lib/services/notifications/system_notifications.dart) | Gọi presenter Android, nhận đích mở ban đầu/đang chạy |
| [push_notifications.dart](../mobile-app/lib/services/notifications/push_notifications.dart) | Chuyển foreground FCM sang system notification khi đã đăng nhập/opt-in |
| [main.dart](../mobile-app/lib/main.dart) | Giữ điều hướng, bỏ SnackBar chứa nội dung push |
| [AndroidManifest.xml](../mobile-app/android/app/src/main/AndroidManifest.xml) | Channel mặc định mới cho FCM |
| [FirebaseGateway.cs](../backend/Services/FirebaseGateway.cs) | Gửi FCM vào cùng channel ID |
| [build.gradle.kts](../mobile-app/android/app/build.gradle.kts) | Chọn application ID riêng cho kiểm thử bằng explicit Dart define |

Intent bấm notification dùng action/extra và request code riêng, không đặt URI data; tránh Flutter tự tạo thêm route không tồn tại. Không tắt deep linking chung của ứng dụng.

## Kiểm chứng

- Backend build riêng thành công, 0 warning/error.
- Flutter analyzer sạch; **81 unit/widget tests pass**, gồm 6 ca mới về notification.
- Native Android test pass trên `emulator-5554`: notification nhìn thấy trong ngăn Android, cùng message ID post hai lần chỉ có **1 notification** của package thử, bấm nhận đích `statistics` và không phát sinh lỗi route.
- Kiểm tra mới bao phủ login/opt-in, data-only message, allowlist đích, native failure và logout cạnh tranh với thao tác hiển thị.
- Native test dùng presenter trực tiếp, **không phải lần gửi/nhận FCM qua Internet**. Chưa đo trên điện thoại thật.

Bằng chứng gitignored: `.local-build/system-notifications-native-result.json`, `system-notifications-native.log`, `system-notifications-flutter-tests.log`, `system-notifications-analyze.log`.

### Lưu ý kiểm thử Android

Đã phát hiện cờ môi trường Gradle không bảo đảm APK test dùng package riêng; Flutter còn sinh entrypoint tạm nên không suy ra test chỉ từ đường dẫn target. Luồng test hiện truyền `--dart-define=MOODIFY_INTEGRATION=true`, được Gradle đọc để chọn `com.example.moodify_application.smoke`. Application ID đã được đối chiếu trong APK.

Harness AI Android cũng được bổ sung define này. APK ứng dụng chính được build từ `lib/main.dart`, bỏ define test và kiểm tra ID `com.example.moodify_application` trước cài. Không phân phối APK integration như app chính.

Trong kiểm thử đã sửa một lỗi thật: URI trong tap intent làm Flutter mở route tên ID notification; native test bắt được và pass sau khi bỏ URI.

## Phạm vi dữ liệu

Không đổi model AI, policy mood, weights, lịch nhắc hoặc tự bật thông báo cho người dùng. Không dùng account/notification preferences chính cho native fixture; package thử được dọn sau test. Bản APK chính và stack được khôi phục ở bước bàn giao.


Bàn giao: đã build/cài APK chính có channel mới, xác minh app chính còn và package thử đã được dọn. Backend 5170, Admin 5174, VI/EN readiness đều HTTP 200. Source APK chính nằm ở `.local-build/moodify-system-notifications-debug.apk`; app mở màn đăng nhập sau cập nhật native. Không commit/push trong lượt này.
