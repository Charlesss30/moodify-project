import 'package:firebase_core/firebase_core.dart';
import 'package:flutter/foundation.dart';

class FirebaseBootstrap {
  static bool ready = false;
  static const googleClientId = String.fromEnvironment(
    'GOOGLE_SERVER_CLIENT_ID',
  );
  static Future<void>? _initializing;
  static Future<void> initialize() => _initializing ??= _initialize();
  static Future<void> _initialize() async {
    const apiKey = String.fromEnvironment('FIREBASE_API_KEY');
    const appId = String.fromEnvironment('FIREBASE_APP_ID');
    const projectId = String.fromEnvironment('FIREBASE_PROJECT_ID');
    const senderId = String.fromEnvironment('FIREBASE_MESSAGING_SENDER_ID');
    try {
      if ([apiKey, appId, projectId, senderId].any((value) => value.isEmpty)) {
        if (kIsWeb) return;
        await Firebase.initializeApp();
        ready = true;
        return;
      }
      await Firebase.initializeApp(
        options: FirebaseOptions(
          apiKey: apiKey,
          appId: appId,
          projectId: projectId,
          messagingSenderId: senderId,
          authDomain:
              const String.fromEnvironment('FIREBASE_AUTH_DOMAIN').isEmpty
              ? '$projectId.firebaseapp.com'
              : const String.fromEnvironment('FIREBASE_AUTH_DOMAIN'),
        ),
      );
      ready = true;
    } catch (_) {
      ready = false;
    }
  }
}
