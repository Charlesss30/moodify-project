import 'package:flutter/material.dart';
import 'package:lucide_icons_flutter/lucide_icons.dart';
import '../core/colors.dart';
import '../core/mock_database.dart';
import '../core/api_service.dart';

class LoginScreen extends StatefulWidget {
  final Function(UserAccount user) onLoginSuccess;
  final VoidCallback onNavigateToRegister;

  const LoginScreen({
    super.key,
    required this.onLoginSuccess,
    required this.onNavigateToRegister,
  });

  @override
  State<LoginScreen> createState() => _LoginScreenState();
}

class _LoginScreenState extends State<LoginScreen> {
  final _identifierController = TextEditingController();
  final _passwordController = TextEditingController();

  bool _showPassword = false;
  bool _rememberMe = true;
  bool _isLoading = false;

  String? _identifierError;
  String? _passwordError;
  String? _generalError;
  String? _successMsg;

  Future<void> _handleLogin() async {
    setState(() {
      _identifierError = null;
      _passwordError = null;
      _generalError = null;
      _successMsg = null;
    });

    final identifier = _identifierController.text.trim();
    final password = _passwordController.text;

    // UC02 - Dòng 4: Kiểm tra không để trống
    bool hasEmpty = false;
    if (identifier.isEmpty) {
      _identifierError = 'Vui lòng điền email hoặc tên đăng nhập.';
      hasEmpty = true;
    }
    if (password.isEmpty) {
      _passwordError = 'Vui lòng điền mật khẩu.';
      hasEmpty = true;
    }

    if (hasEmpty) {
      setState(() {
        _generalError = 'Vui lòng điền đầy đủ thông tin tài khoản và mật khẩu.';
      });
      return;
    }

    setState(() => _isLoading = true);

    // Gọi API thật từ ASP.NET Core backend
    final result = await ApiService.login(
      emailOrUsername: identifier,
      password: password,
    );

    if (!mounted) return;
    setState(() => _isLoading = false);

    if (result['success'] == true) {
      // UC02 - Dòng 6: Đăng nhập thành công
      setState(() {
        _successMsg = 'Đăng nhập thành công! Đang chuyển hướng...';
      });

      Future.delayed(const Duration(milliseconds: 600), () {
        if (mounted) {
          final fullName = identifier.contains('@') 
              ? identifier.split('@').first 
              : identifier;
          widget.onLoginSuccess(UserAccount(
            fullName: fullName,
            email: identifier,
            password: '',
            role: 'NguoiDung',
          ));
        }
      });
    } else {
      // UC02 - Dòng 5 sự kiện phụ: Báo lỗi từ server (sai mật khẩu, bị khoá,...)
      setState(() {
        _generalError = result['message'] ?? 'Tên đăng nhập hoặc mật khẩu chưa chính xác.';
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: AppColors.background,
      body: SafeArea(
        child: SingleChildScrollView(
          padding: const EdgeInsets.symmetric(horizontal: 24, vertical: 24),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              const SizedBox(height: 16),
              Center(
                child: Container(
                  width: 56,
                  height: 56,
                  decoration: BoxDecoration(
                    borderRadius: BorderRadius.circular(16),
                    gradient: const LinearGradient(colors: [AppColors.accentPrimary, AppColors.accentSecondary]),
                    boxShadow: [
                      BoxShadow(color: AppColors.accentPrimary.withOpacity(0.3), blurRadius: 15, offset: const Offset(0, 6)),
                    ],
                  ),
                  child: const Icon(LucideIcons.sparkles, color: Colors.white, size: 28),
                ),
              ),
              const SizedBox(height: 16),
              const Text('Welcome to Moodify', textAlign: TextAlign.center, style: TextStyle(fontSize: 24, fontWeight: FontWeight.bold, color: AppColors.textPrimary)),
              const SizedBox(height: 4),
              const Text('Sign in to analyze your mood and discover matched movies & music', textAlign: TextAlign.center, style: TextStyle(fontSize: 12, color: AppColors.textSecondary)),
              const SizedBox(height: 18),

              if (_generalError != null)
                Container(
                  padding: const EdgeInsets.all(12),
                  margin: const EdgeInsets.only(bottom: 12),
                  decoration: BoxDecoration(
                    color: AppColors.danger.withOpacity(0.1),
                    borderRadius: BorderRadius.circular(12),
                    border: Border.all(color: AppColors.danger.withOpacity(0.3)),
                  ),
                  child: Row(
                    children: [
                      const Icon(LucideIcons.alertCircle, color: AppColors.danger, size: 16),
                      const SizedBox(width: 8),
                      Expanded(child: Text(_generalError!, style: const TextStyle(color: Color(0xFFFDA4AF), fontSize: 12))),
                    ],
                  ),
                ),

              if (_successMsg != null)
                Container(
                  padding: const EdgeInsets.all(12),
                  margin: const EdgeInsets.only(bottom: 12),
                  decoration: BoxDecoration(
                    color: AppColors.positive.withOpacity(0.1),
                    borderRadius: BorderRadius.circular(12),
                    border: Border.all(color: AppColors.positive.withOpacity(0.3)),
                  ),
                  child: Row(
                    children: [
                      const Icon(LucideIcons.checkCircle2, color: AppColors.positive, size: 16),
                      const SizedBox(width: 8),
                      Expanded(child: Text(_successMsg!, style: const TextStyle(color: Color(0xFF86EFAC), fontSize: 12))),
                    ],
                  ),
                ),

              const Text('Email / Tên đăng nhập', style: TextStyle(fontSize: 12, color: AppColors.textSecondary)),
              const SizedBox(height: 6),
              TextField(
                controller: _identifierController,
                style: const TextStyle(color: AppColors.textPrimary, fontSize: 14),
                decoration: InputDecoration(
                  prefixIcon: const Icon(LucideIcons.mail, color: AppColors.textSecondary, size: 18),
                  hintText: 'alex.designer@example.com',
                  hintStyle: TextStyle(color: AppColors.textSecondary.withOpacity(0.4)),
                  filled: true,
                  fillColor: AppColors.surface,
                  border: OutlineInputBorder(borderRadius: BorderRadius.circular(14), borderSide: BorderSide.none),
                  enabledBorder: OutlineInputBorder(
                    borderRadius: BorderRadius.circular(14),
                    borderSide: BorderSide(color: _identifierError != null ? AppColors.danger : Colors.white.withOpacity(0.05)),
                  ),
                ),
              ),
              if (_identifierError != null)
                Padding(
                  padding: const EdgeInsets.only(top: 4, left: 4),
                  child: Text(_identifierError!, style: const TextStyle(color: AppColors.danger, fontSize: 11)),
                ),
              const SizedBox(height: 14),

              const Text('Mật khẩu', style: TextStyle(fontSize: 12, color: AppColors.textSecondary)),
              const SizedBox(height: 6),
              TextField(
                controller: _passwordController,
                obscureText: !_showPassword,
                style: const TextStyle(color: AppColors.textPrimary, fontSize: 14),
                decoration: InputDecoration(
                  prefixIcon: const Icon(LucideIcons.lock, color: AppColors.textSecondary, size: 18),
                  suffixIcon: IconButton(
                    icon: Icon(_showPassword ? LucideIcons.eyeOff : LucideIcons.eye, color: AppColors.textSecondary, size: 18),
                    onPressed: () => setState(() => _showPassword = !_showPassword),
                  ),
                  hintText: '••••••••••••',
                  hintStyle: TextStyle(color: AppColors.textSecondary.withOpacity(0.4)),
                  filled: true,
                  fillColor: AppColors.surface,
                  border: OutlineInputBorder(borderRadius: BorderRadius.circular(14), borderSide: BorderSide.none),
                  enabledBorder: OutlineInputBorder(
                    borderRadius: BorderRadius.circular(14),
                    borderSide: BorderSide(color: _passwordError != null ? AppColors.danger : Colors.white.withOpacity(0.05)),
                  ),
                ),
              ),
              if (_passwordError != null)
                Padding(
                  padding: const EdgeInsets.only(top: 4, left: 4),
                  child: Text(_passwordError!, style: const TextStyle(color: AppColors.danger, fontSize: 11)),
                ),

              const SizedBox(height: 10),
              Row(
                mainAxisAlignment: MainAxisAlignment.spaceBetween,
                children: [
                  Row(
                    children: [
                      SizedBox(
                        height: 22,
                        width: 22,
                        child: Checkbox(
                          value: _rememberMe,
                          activeColor: AppColors.accentPrimary,
                          side: BorderSide(color: Colors.white.withOpacity(0.2)),
                          onChanged: (v) => setState(() => _rememberMe = v ?? false),
                        ),
                      ),
                      const SizedBox(width: 6),
                      const Text('Remember me', style: TextStyle(fontSize: 12, color: AppColors.textSecondary)),
                    ],
                  ),
                  const Text('Forgot password?', style: TextStyle(fontSize: 12, color: AppColors.accentPrimary, fontWeight: FontWeight.w600)),
                ],
              ),
              const SizedBox(height: 18),

              ElevatedButton(
                onPressed: _isLoading ? null : _handleLogin,
                style: ElevatedButton.styleFrom(
                  padding: EdgeInsets.zero,
                  shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(14)),
                ),
                child: Ink(
                  decoration: BoxDecoration(
                    gradient: const LinearGradient(colors: [AppColors.accentPrimary, AppColors.accentSecondary]),
                    borderRadius: BorderRadius.circular(14),
                  ),
                  child: Container(
                    height: 48,
                    alignment: Alignment.center,
                    child: _isLoading
                        ? const SizedBox(
                            width: 22,
                            height: 22,
                            child: CircularProgressIndicator(color: Colors.white, strokeWidth: 2),
                          )
                        : const Text('Sign In', style: TextStyle(color: Colors.white, fontWeight: FontWeight.bold)),
                  ),
                ),
              ),
              const SizedBox(height: 24),

              Row(
                mainAxisAlignment: MainAxisAlignment.center,
                children: [
                  const Text("Don't have an account? ", style: TextStyle(fontSize: 12, color: AppColors.textSecondary)),
                  GestureDetector(
                    onTap: widget.onNavigateToRegister,
                    child: const Text('Sign Up', style: TextStyle(fontSize: 12, color: AppColors.accentPrimary, fontWeight: FontWeight.bold)),
                  ),
                ],
              ),
            ],
          ),
        ),
      ),
    );
  }
}