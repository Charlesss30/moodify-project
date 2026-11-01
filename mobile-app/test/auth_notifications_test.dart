import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:moodify_application/core/api_service.dart';
import 'package:moodify_application/screens/forgot_password_screen.dart';
import 'package:moodify_application/screens/login_screen.dart';
import 'package:moodify_application/screens/notification_settings_screen.dart';
import 'package:moodify_application/services/auth/google_auth_service.dart';
import 'package:moodify_application/services/notifications/push_notifications.dart';

http.Response json(Object body, [int status = 200]) => http.Response(
  jsonEncode(body),
  status,
  headers: {'content-type': 'application/json; charset=utf-8'},
);
Map<String, Object?> session() => {
  'token': 'verified-backend-session',
  'user': {
    'taiKhoanID': 1,
    'tenDangNhap': 'An',
    'email': 'an@test.invalid',
    'vaiTro': 'User',
    'hasPassword': false,
  },
};
Map<String, Object?> settings(bool enabled) => {
  'enabled': enabled,
  'dailyMoodReminder': true,
  'weeklySummary': false,
  'hour': 20,
  'minute': 0,
  'offsetMinutes': 420,
  'deliveryAvailable': true,
};

class FakePushClient implements PushRegistrationClient {
  bool denied = false;
  int disables = 0;
  @override
  bool get available => true;
  @override
  Future<String> prepare({bool askPermission = true}) async {
    if (denied) throw const ApiException('Quyền thông báo chưa được cấp.');
    return 'fixture_installation_123456';
  }

  @override
  Future<void> disable() async {
    disables++;
  }
}

void main() {
  final originalToken = GoogleAuthService.tokenProvider;
  final originalPush = PushNotifications.client;
  tearDown(() async {
    ApiService.logout();
    await PushNotifications.endSession();
    ApiService.clientFactory = http.Client.new;
    ApiService.onUnauthorized = null;
    GoogleAuthService.tokenProvider = originalToken;
    PushNotifications.client = originalPush;
    PushNotifications.active = false;
  });

  testWidgets(
    'email code reset retains input on rejection and never logs in automatically',
    (tester) async {
      addTearDown(() => tester.pumpWidget(const SizedBox.shrink()));
      final submitted = <Map<String, dynamic>>[];
      ApiService.clientFactory = () => MockClient((request) async {
        final body =
            jsonDecode(utf8.decode(request.bodyBytes)) as Map<String, dynamic>;
        if (request.url.path.endsWith('/forgot-password')) {
          expect(body, {'email': 'an@test.invalid'});
          expect(request.headers.containsKey('authorization'), isFalse);
          return json({'challengeId': 'a' * 32, 'expiresInSeconds': 600});
        }
        submitted.add(body);
        return submitted.length == 1
            ? json({'message': 'Mã không hợp lệ.'}, 400)
            : json({});
      });
      await tester.pumpWidget(const MaterialApp(home: ForgotPasswordScreen()));
      await tester.enterText(
        find.byType(TextFormField).first,
        'an@test.invalid',
      );
      await tester.tap(find.text('Gửi mã xác nhận'));
      await tester.pumpAndSettle();
      final fields = find.byType(TextFormField);
      await tester.enterText(fields.at(1), '123456');
      await tester.enterText(fields.at(2), 'New-password-1');
      final save = find.text('Đặt mật khẩu mới');
      await tester.ensureVisible(save);
      await tester.pumpAndSettle();
      await tester.tap(save);
      await tester.pumpAndSettle();
      expect(find.text('Mã không hợp lệ.'), findsOneWidget);
      expect(
        tester.widget<TextFormField>(fields.at(2)).controller!.text,
        'New-password-1',
      );
      await tester.ensureVisible(save);
      await tester.pumpAndSettle();
      await tester.tap(save);
      await tester.pumpAndSettle();
      expect(submitted.last, {
        'challengeId': 'a' * 32,
        'code': '123456',
        'newPassword': 'New-password-1',
      });
      expect(find.text('Mật khẩu đã được cập nhật'), findsOneWidget);
      expect(ApiService.isAuthenticated, isFalse);
      expect(tester.takeException(), isNull);
    },
  );

  testWidgets('Google cancellation returns to normal login without API calls', (
    tester,
  ) async {
    GoogleAuthService.tokenProvider = () async => null;
    var calls = 0;
    ApiService.clientFactory = () => MockClient((_) async {
      calls++;
      return json({});
    });
    await tester.pumpWidget(
      MaterialApp(
        home: LoginScreen(
          onLoginSuccess: (_) =>
              fail('Canceled Google login must not succeed.'),
          onNavigateToRegister: () {},
        ),
      ),
    );
    final button = find.text('Tiếp tục với Google');
    await tester.ensureVisible(button);
    await tester.pumpAndSettle();
    await tester.tap(button);
    await tester.pumpAndSettle();
    expect(calls, 0);
    expect(ApiService.isAuthenticated, isFalse);
    expect(
      tester
          .widget<OutlinedButton>(
            find.widgetWithText(OutlinedButton, 'Tiếp tục với Google'),
          )
          .onPressed,
      isNotNull,
    );
  });

  testWidgets(
    'Google existing account requires password before session adoption',
    (tester) async {
      GoogleAuthService.tokenProvider = () async => 'firebase-verified-token';
      final calls = <Map<String, dynamic>>[];
      var loggedIn = false;
      ApiService.clientFactory = () => MockClient((request) async {
        expect(request.url.path, '/api/Auth/google');
        final body =
            jsonDecode(utf8.decode(request.bodyBytes)) as Map<String, dynamic>;
        calls.add(body);
        return calls.length == 1
            ? json({'message': 'Liên kết cần mật khẩu.'}, 409)
            : json(session());
      });
      await tester.pumpWidget(
        MaterialApp(
          home: LoginScreen(
            onLoginSuccess: (user) {
              loggedIn = true;
              expect(user.hasPassword, isFalse);
            },
            onNavigateToRegister: () {},
          ),
        ),
      );
      final button = find.text('Tiếp tục với Google');
      await tester.ensureVisible(button);
      await tester.pumpAndSettle();
      await tester.tap(button);
      await tester.pumpAndSettle();
      expect(loggedIn, isFalse);
      expect(ApiService.isAuthenticated, isFalse);
      await tester.enterText(
        find.descendant(
          of: find.byType(AlertDialog),
          matching: find.byType(TextFormField),
        ),
        'Existing-password',
      );
      await tester.tap(find.text('Xác nhận liên kết'));
      await tester.pumpAndSettle();
      expect(calls.last, {
        'idToken': 'firebase-verified-token',
        'currentPassword': 'Existing-password',
      });
      expect(loggedIn, isTrue);
      expect(ApiService.isAuthenticated, isTrue);
      expect(tester.takeException(), isNull);
    },
  );

  testWidgets(
    'notifications need consent and register an installation only after saving',
    (tester) async {
      final push = FakePushClient();
      PushNotifications.client = push;
      var enabled = false;
      final requests = <Map<String, dynamic>>[];
      ApiService.clientFactory = () => MockClient((request) async {
        if (request.method == 'GET') return json(settings(enabled));
        final body =
            jsonDecode(utf8.decode(request.bodyBytes)) as Map<String, dynamic>;
        requests.add({'path': request.url.path, ...body});
        if (request.url.path.endsWith('/settings')) {
          enabled = body['enabled'] == true;
          return json({...settings(enabled), ...body});
        }
        return json({});
      });
      await tester.pumpWidget(
        const MaterialApp(home: NotificationSettingsScreen()),
      );
      await tester.pumpAndSettle();
      expect(requests, isEmpty);
      await tester.tap(find.widgetWithText(SwitchListTile, 'Nhận thông báo'));
      await tester.pumpAndSettle();
      expect(requests[0]['enabled'], isTrue);
      expect(requests[1]['path'], '/api/Notifications/devices');
      expect(requests[1]['fid'], 'fixture_installation_123456');
      expect(requests[1].containsKey('accountId'), isFalse);
      await tester.tap(find.widgetWithText(SwitchListTile, 'Nhận thông báo'));
      await tester.pumpAndSettle();
      expect(enabled, isFalse);
      expect(push.disables, greaterThan(0));
      expect(tester.takeException(), isNull);
    },
  );

  testWidgets(
    'permission denial keeps notifications off without saving an enable',
    (tester) async {
      PushNotifications.client = FakePushClient()..denied = true;
      var writes = 0;
      ApiService.clientFactory = () => MockClient((request) async {
        if (request.method != 'GET') writes++;
        return json(settings(false));
      });
      await tester.pumpWidget(
        const MaterialApp(home: NotificationSettingsScreen()),
      );
      await tester.pumpAndSettle();
      await tester.tap(find.widgetWithText(SwitchListTile, 'Nhận thông báo'));
      await tester.pumpAndSettle();
      expect(writes, 0);
      expect(find.text('Quyền thông báo chưa được cấp.'), findsOneWidget);
      expect(tester.takeException(), isNull);
    },
  );
}
