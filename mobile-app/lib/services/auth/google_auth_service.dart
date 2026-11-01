import 'package:firebase_auth/firebase_auth.dart';
import 'package:flutter/foundation.dart';
import 'package:google_sign_in/google_sign_in.dart';

import '../../core/api_service.dart';
import 'firebase_bootstrap.dart';

class GoogleAuthService {
  static Future<void>? _googleInitialization;
  @visibleForTesting
  static Future<String?> Function() tokenProvider = _getToken;
  static Future<String?> signIn() => tokenProvider();
  static Future<String?> _getToken() async {
    if (!FirebaseBootstrap.ready) {
      throw const ApiException(
        'Đăng nhập Google chưa sẵn sàng. Hãy dùng email.',
      );
    }
    try {
      UserCredential credential;
      if (kIsWeb) {
        credential = await FirebaseAuth.instance.signInWithPopup(
          GoogleAuthProvider()
            ..setCustomParameters({'prompt': 'select_account'}),
        );
      } else {
        final providers = await ApiService.request(
          '/Auth/providers',
          authenticated: false,
        );
        if (providers is! Map || providers['google'] != true) {
          throw const ApiException(
            'Đăng nhập Google chưa sẵn sàng. Hãy dùng email.',
          );
        }
        final clientId = FirebaseBootstrap.googleClientId.isNotEmpty
            ? FirebaseBootstrap.googleClientId
            : providers['googleServerClientId'] as String? ?? '';
        await (_googleInitialization ??= GoogleSignIn.instance.initialize(
          serverClientId: clientId.isEmpty ? null : clientId,
        ));
        final account = await GoogleSignIn.instance.authenticate();
        final token = account.authentication.idToken;
        if (token == null) {
          throw const ApiException(
            'Google chưa trả về phiên đăng nhập hợp lệ.',
          );
        }
        credential = await FirebaseAuth.instance.signInWithCredential(
          GoogleAuthProvider.credential(idToken: token),
        );
      }
      return await credential.user?.getIdToken();
    } on GoogleSignInException catch (error) {
      if (error.code == GoogleSignInExceptionCode.canceled) return null;
      throw const ApiException('Chưa đăng nhập được Google. Hãy thử lại.');
    } on FirebaseAuthException catch (error) {
      if (error.code == 'popup-closed-by-user' ||
          error.code == 'cancelled-popup-request') {
        return null;
      }
      throw const ApiException(
        'Chưa xác minh được tài khoản Google. Hãy thử lại.',
      );
    }
  }

  static Future<void> signOut() async {
    if (!FirebaseBootstrap.ready) return;
    try {
      await FirebaseAuth.instance.signOut();
    } on FirebaseException {
      // The Moodify session is cleared separately.
    }
    if (!kIsWeb && _googleInitialization != null) {
      try {
        await GoogleSignIn.instance.signOut();
      } on GoogleSignInException {
        // The Moodify session is cleared separately.
      }
    }
  }
}
