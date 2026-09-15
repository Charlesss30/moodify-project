import 'package:flutter/material.dart';
import 'package:google_fonts/google_fonts.dart';
import 'package:lucide_icons_flutter/lucide_icons.dart';
import 'core/colors.dart';
import 'core/mock_database.dart';
import 'screens/login_screen.dart';
import 'screens/register_screen.dart';
import 'screens/home_screen.dart';

void main() {
  runApp(const MoodifyApp());
}

class MoodifyApp extends StatelessWidget {
  const MoodifyApp({super.key});

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      title: 'Moodify',
      debugShowCheckedModeBanner: false,
      theme: ThemeData(
        brightness: Brightness.dark,
        scaffoldBackgroundColor: AppColors.background,
        textTheme: GoogleFonts.interTextTheme(ThemeData.dark().textTheme),
        useMaterial3: true,
      ),
      home: const AppFlowCoordinator(),
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
  UserAccount? _currentUserSession; // Quản lý phiên làm việc sau UC02

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
        return HomeScreen(
          currentUser: _currentUserSession!,
          // UC03 - Dòng 4 & 5: Kết thúc phiên làm việc, quay về trạng thái vãng lai (login)
          onLogoutConfirmed: () {
            setState(() {
              _currentUserSession = null;
              _currentView = 'login';
            });
          },
        );

      case 'login':
      default:
        // Tiền điều kiện mặc định
        return LoginScreen(
          // UC02 - Dòng 6: Xác thực thành công -> Lưu session -> Vào trang chủ
          onLoginSuccess: (user) {
            setState(() {
              _currentUserSession = user;
              _currentView = 'home';
            });
          },
          // UC01 - Dòng 1: Chọn chức năng đăng ký
          onNavigateToRegister: () {
            setState(() => _currentView = 'register');
          },
        );
    }
  }
}