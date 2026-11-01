import 'dart:async';

import 'package:firebase_messaging/firebase_messaging.dart';
import 'package:flutter/foundation.dart';

import '../../core/api_service.dart';
import '../auth/firebase_bootstrap.dart';

@pragma('vm:entry-point')
Future<void> moodifyBackgroundMessage(RemoteMessage message) async {
  await FirebaseBootstrap.initialize();
}

abstract interface class PushRegistrationClient {
  bool get available;
  Future<String> prepare({bool askPermission = true});
  Future<void> disable();
}

class FirebasePushClient implements PushRegistrationClient {
  @override
  bool get available =>
      FirebaseBootstrap.ready &&
      !kIsWeb &&
      defaultTargetPlatform == TargetPlatform.android;
  StreamSubscription<String>? _registered;
  Completer<String>? _pending;
  String? _fid;
  Future<String>? _registration;
  @override
  Future<String> prepare({bool askPermission = true}) =>
      _registration ??= _prepare(askPermission)
          .timeout(
            const Duration(seconds: 20),
            onTimeout: () => throw const ApiException(
              'Đăng ký thông báo quá lâu. Hãy thử lại.',
            ),
          )
          .whenComplete(() => _registration = null);

  Future<String> _prepare(bool askPermission) async {
    if (!available) {
      throw const ApiException('Thông báo chưa sẵn sàng trên thiết bị này.');
    }
    final messaging = FirebaseMessaging.instance;
    try {
      final settings = askPermission
          ? await messaging.requestPermission()
          : await messaging.getNotificationSettings();
      if (settings.authorizationStatus != AuthorizationStatus.authorized &&
          settings.authorizationStatus != AuthorizationStatus.provisional) {
        throw const ApiException(
          'Quyền thông báo chưa được cấp. Bạn có thể bật trong cài đặt thiết bị.',
        );
      }
      if (_fid != null) return _fid!;
      _registered ??= messaging.onRegistered.listen((fid) {
        _fid = fid;
        if (_pending?.isCompleted == false) _pending!.complete(fid);
        if (PushNotifications.active && ApiService.isAuthenticated) {
          unawaited(PushNotifications.register(fid).catchError((Object _) {}));
        }
      });
      _pending = Completer<String>();
      await messaging.setAutoInitEnabled(true);
      await messaging.register();
      return await _pending!.future.timeout(const Duration(seconds: 15));
    } on ApiException {
      rethrow;
    } catch (_) {
      throw const ApiException('Chưa đăng ký được thông báo. Hãy thử lại.');
    }
  }

  @override
  Future<void> disable() async {
    _fid = null;
    if (_registered != null) await _registered!.cancel();
    _registered = null;
    if (!available) return;
    try {
      await FirebaseMessaging.instance.unregister();
      await FirebaseMessaging.instance.setAutoInitEnabled(false);
    } catch (_) {
      // Backend opt-out remains authoritative.
    }
  }
}

class PushNotifications {
  @visibleForTesting
  static PushRegistrationClient client = FirebasePushClient();
  static bool get available => client.available;
  static Future<String> prepare({bool askPermission = true}) =>
      client.prepare(askPermission: askPermission);
  static bool active = false;
  static String? _fid;
  static StreamSubscription<RemoteMessage>? _foreground, _opened;
  static void Function(String screen)? onOpen;
  static void Function(String title, String body, String screen)? onForeground;
  static String? pendingScreen;

  static Future<void> initialize() async {
    if (!client.available) return;
    FirebaseMessaging.onBackgroundMessage(moodifyBackgroundMessage);
    await FirebaseMessaging.instance.setAutoInitEnabled(false);
    _foreground ??= FirebaseMessaging.onMessage.listen((message) {
      if (!active || !ApiService.isAuthenticated) return;
      final screen = _screen(message);
      onForeground?.call(
        message.notification?.title ?? 'Moodify',
        message.notification?.body ?? 'Có một lời nhắc dành cho bạn.',
        screen,
      );
    });
    _opened ??= FirebaseMessaging.onMessageOpenedApp.listen(
      (message) => _open(_screen(message)),
    );
    final initial = await FirebaseMessaging.instance.getInitialMessage();
    if (initial != null) _open(_screen(initial));
  }

  static String _screen(RemoteMessage message) =>
      message.data['screen'] == 'statistics' ? 'statistics' : 'discover';
  static void _open(String screen) {
    if (ApiService.isAuthenticated) {
      onOpen?.call(screen);
    } else {
      pendingScreen = screen;
    }
  }

  static Future<void> startSession() async {
    if (!client.available || !ApiService.isAuthenticated) return;
    final session = ApiService.sessionRevision;
    try {
      final settings = await ApiService.request('/Notifications/settings');
      if (settings is Map && settings['enabled'] == true) {
        final fid = await client.prepare(askPermission: false);
        if (ApiService.isAuthenticated &&
            session == ApiService.sessionRevision) {
          await register(fid);
        }
      }
    } catch (_) {
      // Optional notifications never block login or the main app.
    }
  }

  static Future<void> register(String fid) async {
    await ApiService.request(
      '/Notifications/devices',
      method: 'POST',
      body: {'fid': fid},
    );
    _fid = fid;
    active = true;
  }

  static Future<void> disable() async {
    active = false;
    final fid = _fid;
    _fid = null;
    if (fid != null && ApiService.isAuthenticated) {
      try {
        await ApiService.request(
          '/Notifications/unregister',
          method: 'POST',
          body: {'fid': fid},
        );
      } on ApiException {
        // Native unregister also disables this installation.
      }
    }
    await client.disable();
  }

  static Future<void> endSession() async {
    active = false;
    try {
      await disable().timeout(const Duration(seconds: 4));
    } catch (_) {
      // Session logout still completes when the device is offline.
    }
  }
}
