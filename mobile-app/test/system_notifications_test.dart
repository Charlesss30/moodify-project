import 'dart:async';
import 'dart:convert';

import 'package:firebase_messaging/firebase_messaging.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:moodify_application/core/api_service.dart';
import 'package:moodify_application/services/notifications/push_notifications.dart';
import 'package:moodify_application/services/notifications/system_notifications.dart';

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();
  final messenger =
      TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger;
  final calls = <MethodCall>[];

  Future<void> login() async {
    ApiService.clientFactory = () => MockClient(
      (_) async => http.Response(
        jsonEncode({
          'token': 'local-fixture-session',
          'user': {
            'taiKhoanID': 1,
            'tenDangNhap': 'Fixture',
            'email': 'fixture@test.invalid',
            'vaiTro': 'User',
          },
        }),
        200,
        headers: {'content-type': 'application/json'},
      ),
    );
    await ApiService.login(emailOrUsername: 'fixture', password: 'fixture');
  }

  RemoteMessage reminder({String screen = 'statistics'}) => RemoteMessage(
    messageId: 'same-fcm-id',
    data: {'screen': screen},
    notification: const RemoteNotification(
      title: 'Moodify',
      body: 'Weekly reminder',
    ),
  );

  setUp(() {
    debugDefaultTargetPlatformOverride = TargetPlatform.android;
    calls.clear();
    ApiService.logout();
    PushNotifications.active = false;
    messenger.setMockMethodCallHandler(SystemNotifications.channel, (
      call,
    ) async {
      calls.add(call);
      return call.method == 'show' ? true : null;
    });
  });

  tearDown(() {
    ApiService.logout();
    ApiService.clientFactory = http.Client.new;
    PushNotifications.active = false;
    SystemNotifications.channel.setMethodCallHandler(null);
    messenger.setMockMethodCallHandler(SystemNotifications.channel, null);
    debugDefaultTargetPlatformOverride = null;
  });

  test('foreground reminders require both login and opt-in', () async {
    await PushNotifications.handleForeground(reminder());
    PushNotifications.active = true;
    await PushNotifications.handleForeground(reminder());
    expect(calls, isEmpty);
    await login();
    PushNotifications.active = false;
    await PushNotifications.handleForeground(reminder());
    expect(calls, isEmpty);
    PushNotifications.active = true;
    await PushNotifications.handleForeground(reminder());
    expect(calls.single.method, 'show');
    expect(calls.single.arguments, {
      'title': 'Moodify',
      'body': 'Weekly reminder',
      'screen': 'statistics',
      'messageId': 'same-fcm-id',
    });
  });

  test('silent data-only messages never create a visible reminder', () async {
    await login();
    PushNotifications.active = true;
    await PushNotifications.handleForeground(
      const RemoteMessage(data: {'screen': 'statistics'}),
    );
    expect(calls, isEmpty);
  });

  test('untrusted screen values are confined to discover', () async {
    await login();
    PushNotifications.active = true;
    await PushNotifications.handleForeground(reminder(screen: '/admin/users'));
    expect((calls.single.arguments as Map)['screen'], 'discover');
  });

  test(
    'native launch and tap callbacks allow only known destinations',
    () async {
      messenger.setMockMethodCallHandler(SystemNotifications.channel, (
        call,
      ) async {
        return call.method == 'initialize' ? 'statistics' : null;
      });
      final opened = <String>[];
      await SystemNotifications.initialize(onOpen: opened.add);
      expect(opened, ['statistics']);
      const codec = StandardMethodCodec();
      for (final screen in ['discover', 'https://external.invalid']) {
        await messenger.handlePlatformMessage(
          SystemNotifications.channel.name,
          codec.encodeMethodCall(MethodCall('opened', screen)),
          (_) {},
        );
      }
      expect(opened, ['statistics', 'discover']);
    },
  );

  test(
    'a logout racing native presentation clears posted notifications',
    () async {
      await login();
      PushNotifications.active = true;
      final posted = Completer<bool>();
      messenger.setMockMethodCallHandler(SystemNotifications.channel, (
        call,
      ) async {
        calls.add(call);
        if (call.method == 'show') return posted.future;
        return null;
      });
      final pending = PushNotifications.handleForeground(reminder());
      await Future<void>.delayed(Duration.zero);
      ApiService.logout();
      posted.complete(true);
      await pending;
      expect(calls.map((c) => c.method), ['show', 'cancelAll']);
    },
  );

  test(
    'permission/platform failures return safely without blocking the app',
    () async {
      messenger.setMockMethodCallHandler(SystemNotifications.channel, (
        _,
      ) async {
        throw PlatformException(code: 'permission_denied');
      });
      expect(
        await SystemNotifications.show(
          title: 'Moodify',
          body: 'Reminder',
          screen: 'discover',
        ),
        isFalse,
      );
      await SystemNotifications.cancelAll();
      await SystemNotifications.initialize(onOpen: (_) {});
      debugDefaultTargetPlatformOverride = TargetPlatform.windows;
      expect(
        await SystemNotifications.show(
          title: 'Moodify',
          body: 'Reminder',
          screen: 'discover',
        ),
        isFalse,
      );
    },
  );
}
