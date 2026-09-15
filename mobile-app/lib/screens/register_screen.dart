import 'package:flutter/material.dart';
import 'package:lucide_icons_flutter/lucide_icons.dart';
import '../core/colors.dart';
import '../core/api_service.dart';

class RegisterScreen extends StatefulWidget {
  final VoidCallback onRegisterSuccess;
  final VoidCallback onNavigateToLogin;

  const RegisterScreen({
    super.key,
    required this.onRegisterSuccess,
    required this.onNavigateToLogin,
  });

  @override
  State<RegisterScreen> createState() => _RegisterScreenState();
}

class _RegisterScreenState extends State<RegisterScreen> {
  final _nameController = TextEditingController();
  final _emailController = TextEditingController();
  final _passwordController = TextEditingController();
  final _confirmPasswordController = TextEditingController();

  bool _showPassword = false;
  bool _agreedToTerms = false;
  bool _isLoading = false;

  String? _nameError;
  String? _emailError;
  String? _passwordError;
  String? _confirmPasswordError;
  String? _generalError;
  String? _successMsg;

  Future<void> _handleRegister() async {
    setState(() {
      _nameError = null;
      _emailError = null;
      _passwordError = null;
      _confirmPasswordError = null;
      _generalError = null;
      _successMsg = null;
    });

    final name = _nameController.text.trim();
    final email = _emailController.text.trim();
    final password = _passwordController.text;
    final confirm = _confirmPasswordController.text;

    // UC01 - Dòng 4: Kiểm tra tính hợp lệ dữ liệu
    bool hasError = false;
    if (name.isEmpty) {
      _nameError = 'Họ và tên không được để trống.';
      hasError = true;
    }
    if (email.isEmpty) {
      _emailError = 'Email không được để trống.';
      hasError = true;
    } else if (!RegExp(r'^\S+@\S+\.\S+$').hasMatch(email)) {
      _emailError = 'Định dạng email không hợp lệ.';
      hasError = true;
    }

    if (password.isEmpty) {
      _passwordError = 'Mật khẩu không được để trống.';
      hasError = true;
    } else if (password.length < 6) {
      _passwordError = 'Mật khẩu phải có ít nhất 6 ký tự.';
      hasError = true;
    }

    if (password != confirm) {
      _confirmPasswordError = 'Mật khẩu xác nhận không trùng khớp.';
      hasError = true;
    }

    if (!_agreedToTerms) {
      return;
    }

    if (hasError) {
      setState(() {});
      return;
    }

    setState(() => _isLoading = true);

    // UC01 - Dòng 5 & 6: Gọi API lưu thông tin vào PostgreSQL
    final result = await ApiService.register(
      fullName: name,
      email: email,
      password: password,
    );

    if (!mounted) return;
    setState(() => _isLoading = false);

    if (result['success'] == true) {
      // UC01 - Dòng 7: Thông báo thành công và quay lại login
      setState(() {
        _successMsg = 'Đăng ký thành công! Đang chuyển hướng về trang đăng nhập...';
      });

      Future.delayed(const Duration(milliseconds: 1200), () {
        if (mounted) widget.onRegisterSuccess();
      });
    } else {
      // UC01 - Dòng 5 sự kiện phụ: Báo lỗi trùng email hoặc lỗi server
      setState(() {
        _emailError = result['message'] ?? 'Tài khoản hoặc email này đã được đăng ký.';
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: AppColors.background,
      body: SafeArea(
        child: SingleChildScrollView(
          padding: const EdgeInsets.symmetric(horizontal: 24, vertical: 20),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Align(
                alignment: Alignment.centerLeft,
                child: IconButton(
                  onPressed: widget.onNavigateToLogin,
                  icon: const Icon(
                    LucideIcons.arrowLeft,
                    color: AppColors.textPrimary,
                    size: 20,
                  ),
                  style: IconButton.styleFrom(
                    backgroundColor: AppColors.surfaceSecondary,
                  ),
                ),
              ),
              const SizedBox(height: 12),
              const Text(
                'Create Account',
                style: TextStyle(
                  fontSize: 26,
                  fontWeight: FontWeight.bold,
                  color: AppColors.textPrimary,
                ),
              ),
              const SizedBox(height: 4),
              const Text(
                'Personalize your entertainment journey with AI',
                style: TextStyle(fontSize: 12, color: AppColors.textSecondary),
              ),
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
                      const Icon(LucideIcons.alertCircle, color: AppColors.danger, size: 18),
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
                    border: Border.all(
                      color: AppColors.positive.withOpacity(0.3),
                    ),
                  ),
                  child: Row(
                    children: [
                      const Icon(
                        LucideIcons.checkCircle2,
                        color: AppColors.positive,
                        size: 18,
                      ),
                      const SizedBox(width: 8),
                      Expanded(
                        child: Text(
                          _successMsg!,
                          style: const TextStyle(
                            color: Color(0xFF86EFAC),
                            fontSize: 12,
                          ),
                        ),
                      ),
                    ],
                  ),
                ),

              _buildField(
                'Full Name',
                'Alex Morgan',
                LucideIcons.user,
                _nameController,
                _nameError,
              ),
              const SizedBox(height: 12),
              _buildField(
                'Email address',
                'alex.designer@example.com',
                LucideIcons.mail,
                _emailController,
                _emailError,
              ),
              const SizedBox(height: 12),
              _buildField(
                'Password',
                'Tối thiểu 6 ký tự',
                LucideIcons.lock,
                _passwordController,
                _passwordError,
                isPassword: true,
              ),
              const SizedBox(height: 12),
              _buildField(
                'Confirm Password',
                'Nhập lại mật khẩu',
                LucideIcons.lock,
                _confirmPasswordController,
                _confirmPasswordError,
                isPassword: true,
              ),
              const SizedBox(height: 12),

              Row(
                children: [
                  SizedBox(
                    height: 22,
                    width: 22,
                    child: Checkbox(
                      value: _agreedToTerms,
                      activeColor: AppColors.accentPrimary,
                      side: BorderSide(color: Colors.white.withOpacity(0.2)),
                      onChanged: (v) =>
                          setState(() => _agreedToTerms = v ?? false),
                    ),
                  ),
                  const SizedBox(width: 8),
                  const Expanded(
                    child: Text(
                      'Tôi đồng ý với Điều khoản Dịch vụ & Chính sách Bảo mật',
                      style: TextStyle(
                        fontSize: 11,
                        color: AppColors.textSecondary,
                      ),
                    ),
                  ),
                ],
              ),
              const SizedBox(height: 20),

              ElevatedButton(
                onPressed: (_agreedToTerms && !_isLoading) ? _handleRegister : null,
                style: ElevatedButton.styleFrom(
                  padding: EdgeInsets.zero,
                  shape: RoundedRectangleBorder(
                    borderRadius: BorderRadius.circular(14),
                  ),
                  disabledBackgroundColor: AppColors.surfaceSecondary,
                ),
                child: Ink(
                  decoration: BoxDecoration(
                    gradient: _agreedToTerms
                        ? const LinearGradient(
                            colors: [
                              AppColors.accentPrimary,
                              AppColors.accentSecondary,
                            ],
                          )
                        : null,
                    color: !_agreedToTerms ? AppColors.surfaceSecondary : null,
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
                        : Text(
                            'Create Account',
                            style: TextStyle(
                              color: _agreedToTerms
                                  ? Colors.white
                                  : AppColors.textSecondary,
                              fontWeight: FontWeight.bold,
                            ),
                          ),
                  ),
                ),
              ),
              const SizedBox(height: 16),

              Row(
                mainAxisAlignment: MainAxisAlignment.center,
                children: [
                  const Text(
                    'Already have an account? ',
                    style: TextStyle(
                      fontSize: 12,
                      color: AppColors.textSecondary,
                    ),
                  ),
                  GestureDetector(
                    onTap: widget.onNavigateToLogin,
                    child: const Text(
                      'Sign In',
                      style: TextStyle(
                        fontSize: 12,
                        color: AppColors.accentPrimary,
                        fontWeight: FontWeight.bold,
                      ),
                    ),
                  ),
                ],
              ),
            ],
          ),
        ),
      ),
    );
  }

  Widget _buildField(
    String label,
    String hint,
    IconData icon,
    TextEditingController controller,
    String? error, {
    bool isPassword = false,
  }) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(
          label,
          style: const TextStyle(fontSize: 12, color: AppColors.textSecondary),
        ),
        const SizedBox(height: 6),
        TextField(
          controller: controller,
          obscureText: isPassword && !_showPassword,
          style: const TextStyle(color: AppColors.textPrimary, fontSize: 14),
          decoration: InputDecoration(
            prefixIcon: Icon(icon, color: AppColors.textSecondary, size: 18),
            suffixIcon: isPassword
                ? IconButton(
                    icon: Icon(
                      _showPassword ? LucideIcons.eyeOff : LucideIcons.eye,
                      color: AppColors.textSecondary,
                      size: 18,
                    ),
                    onPressed: () =>
                        setState(() => _showPassword = !_showPassword),
                  )
                : null,
            hintText: hint,
            hintStyle: TextStyle(
              color: AppColors.textSecondary.withOpacity(0.4),
            ),
            filled: true,
            fillColor: AppColors.surface,
            border: OutlineInputBorder(
              borderRadius: BorderRadius.circular(14),
              borderSide: BorderSide.none,
            ),
            enabledBorder: OutlineInputBorder(
              borderRadius: BorderRadius.circular(14),
              borderSide: BorderSide(
                color: error != null
                    ? AppColors.danger
                    : Colors.white.withOpacity(0.05),
              ),
            ),
          ),
        ),
        if (error != null)
          Padding(
            padding: const EdgeInsets.only(top: 4, left: 4),
            child: Text(
              error,
              style: const TextStyle(color: AppColors.danger, fontSize: 11),
            ),
          ),
      ],
    );
  }
}