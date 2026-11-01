import 'package:flutter/foundation.dart';
import 'package:flutter/services.dart';

/// Displays received FCM reminders using Android's notification UI.
class SystemNotifications {
  static const channel = MethodChannel('moodify/system_notifications');
  static void Function(String screen)? _onOpen;

  static bool get _supported =>
      !kIsWeb && defaultTargetPlatform == TargetPlatform.android;

  static String? _screen(Object? value) =>
      value == 'discover' || value == 'statistics' ? value as String : null;

  static Future<void> initialize({
    required void Function(String screen) onOpen,
  }) async {
    _onOpen = onOpen;
    if (!_supported) return;
    channel.setMethodCallHandler((call) async {
      final destination = _screen(call.arguments);
      if (call.method == 'opened' && destination != null) {
        _onOpen?.call(destination);
      }
    });
    try {
      final initial = _screen(await channel.invokeMethod<String>('initialize'));
      if (initial != null) _onOpen?.call(initial);
    } on MissingPluginException {
      // Older APKs/platforms without this presenter remain usable.
    } on PlatformException {
      // Optional notifications never block app startup.
    }
  }

  static Future<bool> show({
    required String title,
    required String body,
    required String screen,
    String? messageId,
  }) async {
    if (!_supported) return false;
    try {
      return await channel.invokeMethod<bool>('show', {
            'title': title,
            'body': body,
            'screen': _screen(screen) ?? 'discover',
            'messageId': messageId,
          }) ??
          false;
    } on MissingPluginException {
      return false;
    } on PlatformException {
      return false;
    }
  }

  static Future<void> cancelAll() async {
    if (!_supported) return;
    try {
      await channel.invokeMethod<void>('cancelAll');
    } on MissingPluginException {
      // No native notifications to clear.
    } on PlatformException {
      // Logging out/turning off reminders must still finish.
    }
  }
}
