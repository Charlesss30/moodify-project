import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';

import 'core/app_theme.dart';
import 'widgets/app_startup.dart';
import 'core/navigation.dart';
import 'services/auth/firebase_bootstrap.dart';
import 'services/auth/google_auth_service.dart';
import 'services/notifications/push_notifications.dart';
import 'screens/mood_discovery_screen.dart';
import 'screens/statistics_screen.dart';
import 'core/api_service.dart';
import 'models/session_user.dart';
import 'screens/login_screen.dart';
import 'screens/register_screen.dart';
import 'screens/app_shell.dart';

void main() {
  WidgetsFlutterBinding.ensureInitialized();
  runApp(const MoodifyApp());
}

Future<void> _initializeServices() async {
  await FirebaseBootstrap.initialize();
  try {
    await PushNotifications.initialize();
  } catch (_) {
    // Optional notifications do not prevent starting the app.
  }
}

class MoodifyApp extends StatelessWidget {
  const MoodifyApp({super.key});

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      title: 'Moodify',
      navigatorObservers: [appRouteObserver],
      navigatorKey: appNavigatorKey,
      locale: const Locale('vi', 'VN'),
      supportedLocales: const [Locale('vi', 'VN'), Locale('en')],
      localizationsDelegates: GlobalMaterialLocalizations.delegates,
      debugShowCheckedModeBanner: false,
      theme: AppTheme.dark,
      home: AppStartup(
        initialize: _initializeServices,
        child: const AppFlowCoordinator(),
      ),
    );
  }
}

class AppFlowCoordinator extends StatefulWidget {
  const AppFlowCoordinator({super.key});

  @override
  State<AppFlowCoordinator> createState() => _AppFlowCoordinatorState();
}

class _AppFlowCoordinatorState extends State<AppFlowCoordinator> {
  // Tiền điều kiện UC01, UC02: Người dùng mở ứng dụng và chưa đăng nhập (view = 'login')
  String _currentView = 'login';
  SessionUser? _currentUserSession;

  @override
  void initState() {
    super.initState();
    ApiService.onUnauthorized = _expireSession;
    PushNotifications.onOpen = _openNotification;
    PushNotifications.onForeground = (title, body, screen) {
      if (!mounted || _currentView != 'home') return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text('$title: $body'),
          action: SnackBarAction(
            label: 'Mở',
            onPressed: () => _openNotification(screen),
          ),
        ),
      );
    };
  }

  void _openNotification(String screen) {
    if (!mounted || _currentView != 'home') {
      PushNotifications.pendingScreen = screen;
      return;
    }
    appNavigatorKey.currentState?.push(
      MaterialPageRoute<void>(
        builder: (_) => screen == 'statistics'
            ? const StatisticsScreen()
            : const MoodDiscoveryScreen(),
      ),
    );
  }

  void _loggedIn(SessionUser user) {
    setState(() {
      _currentUserSession = user;
      _currentView = 'home';
    });
    unawaited(PushNotifications.startSession());
    final pending = PushNotifications.pendingScreen;
    PushNotifications.pendingScreen = null;
    if (pending != null) {
      WidgetsBinding.instance.addPostFrameCallback(
        (_) => _openNotification(pending),
      );
    }
  }

  void _expireSession() {
    if (!mounted) return;
    unawaited(PushNotifications.endSession());
    Navigator.of(
      context,
      rootNavigator: true,
    ).popUntil((route) => route.isFirst);
    setState(() {
      ApiService.expireSession();
      _currentUserSession = null;
      _currentView = 'login';
    });
  }

  Future<void> _logout() async {
    await PushNotifications.endSession();
    await GoogleAuthService.signOut();
    ApiService.logout();
    if (!mounted) return;
    Navigator.of(
      context,
      rootNavigator: true,
    ).popUntil((route) => route.isFirst);
    setState(() {
      _currentUserSession = null;
      _currentView = 'login';
    });
  }

  @override
  Widget build(BuildContext context) {
    switch (_currentView) {
      case 'register':
        // UC01: Hiển thị biểu mẫu đăng ký
        return RegisterScreen(
          // Dòng 7: Thông báo thành công và điều hướng về trang đăng nhập
          onRegisterSuccess: () {
            setState(() => _currentView = 'login');
          },
          onNavigateToLogin: () {
            setState(() => _currentView = 'login');
          },
        );

      case 'home':
        // Hậu điều kiện UC02: Có mã phiên làm việc, mở khóa chức năng
        return AppShell(
          currentUser: _currentUserSession!,
          // UC03 - Dòng 4 & 5: Kết thúc phiên làm việc, quay về trạng thái vãng lai (login)
          onLogoutConfirmed: () {
            _logout();
          },
        );

      case 'login':
      default:
        // Tiền điều kiện mặc định
        return LoginScreen(
          // UC02 - Dòng 6: Xác thực thành công -> Lưu session -> Vào trang chủ
          onLoginSuccess: _loggedIn,
          // UC01 - Dòng 1: Chọn chức năng đăng ký
          onNavigateToRegister: () {
            setState(() => _currentView = 'register');
          },
        );
    }
  }

  @override
  void dispose() {
    ApiService.onUnauthorized = null;
    PushNotifications.onOpen = null;
    PushNotifications.onForeground = null;
    super.dispose();
  }
}
