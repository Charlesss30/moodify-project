import 'package:flutter/material.dart';

import '../core/api_service.dart';
import '../widgets/app_widgets.dart';
import '../widgets/auth_frame.dart';

class RegisterScreen extends StatefulWidget {
  const RegisterScreen({
    super.key,
    required this.onRegisterSuccess,
    required this.onNavigateToLogin,
  });
  final VoidCallback onRegisterSuccess, onNavigateToLogin;
  @override
  State<RegisterScreen> createState() => _RegisterScreenState();
}

class _RegisterScreenState extends State<RegisterScreen> {
  final _form = GlobalKey<FormState>();
  final _name = TextEditingController(),
      _email = TextEditingController(),
      _password = TextEditingController(),
      _confirm = TextEditingController();
  bool _busy = false, _showPassword = false;
  String? _error;
  @override
  void dispose() {
    _name.dispose();
    _email.dispose();
    _password.dispose();
    _confirm.dispose();
    super.dispose();
  }

  Future<void> _register() async {
    if (_busy || !_form.currentState!.validate()) return;
    FocusManager.instance.primaryFocus?.unfocus();
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      await ApiService.register(
        fullName: _name.text,
        email: _email.text,
        password: _password.text,
      );
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text('Đã tạo tài khoản. Bạn có thể đăng nhập.'),
        ),
      );
      widget.onRegisterSuccess();
    } on ApiException catch (e) {
      if (mounted) {
        setState(() {
          _busy = false;
          _error = e.message;
        });
      }
    }
  }

  Widget _passwordField(
    TextEditingController controller,
    String label, {
    bool confirm = false,
  }) => TextFormField(
    controller: controller,
    enabled: !_busy,
    obscureText: !_showPassword,
    enableSuggestions: false,
    autocorrect: false,
    autofillHints: const [AutofillHints.newPassword],
    textInputAction: confirm ? TextInputAction.done : TextInputAction.next,
    onFieldSubmitted: confirm ? (_) => _register() : null,
    decoration: InputDecoration(
      labelText: label,
      prefixIcon: const Icon(Icons.lock_outline),
      helperText: confirm ? null : 'Tối thiểu 6 ký tự.',
      suffixIcon: IconButton(
        tooltip: _showPassword ? 'Ẩn mật khẩu' : 'Hiện mật khẩu',
        onPressed: () => setState(() => _showPassword = !_showPassword),
        icon: Icon(
          _showPassword
              ? Icons.visibility_off_outlined
              : Icons.visibility_outlined,
        ),
      ),
    ),
    validator: (v) => confirm
        ? (v != _password.text ? 'Mật khẩu xác nhận chưa khớp.' : null)
        : ((v?.length ?? 0) < 6 ? 'Mật khẩu cần ít nhất 6 ký tự.' : null),
  );
  @override
  Widget build(BuildContext context) => AuthFrame(
    title: 'Tạo tài khoản',
    subtitle: 'Bắt đầu hành trình khám phá phim và nhạc cùng Moodify.',
    onBack: _busy ? null : widget.onNavigateToLogin,
    child: AutofillGroup(
      child: Form(
        key: _form,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            if (_error != null)
              StatusCard(message: _error!, title: 'Chưa tạo được tài khoản'),
            TextFormField(
              controller: _name,
              enabled: !_busy,
              textInputAction: TextInputAction.next,
              hintLocales: const [Locale('vi', 'VN'), Locale('en')],
              autofillHints: const [AutofillHints.username],
              decoration: const InputDecoration(
                labelText: 'Tên đăng nhập',
                prefixIcon: Icon(Icons.person_outline),
              ),
              validator: (v) => v == null || v.trim().isEmpty
                  ? 'Nhập tên bạn muốn sử dụng.'
                  : null,
            ),
            const SizedBox(height: 16),
            TextFormField(
              controller: _email,
              enabled: !_busy,
              textInputAction: TextInputAction.next,
              autocorrect: false,
              keyboardType: TextInputType.emailAddress,
              autofillHints: const [AutofillHints.email],
              decoration: const InputDecoration(
                labelText: 'Email',
                prefixIcon: Icon(Icons.mail_outline),
              ),
              validator: (v) =>
                  RegExp(r'^\S+@\S+\.\S+$').hasMatch(v?.trim() ?? '')
                  ? null
                  : 'Nhập email hợp lệ.',
            ),
            const SizedBox(height: 16),
            _passwordField(_password, 'Mật khẩu'),
            const SizedBox(height: 16),
            _passwordField(_confirm, 'Nhập lại mật khẩu', confirm: true),
            const SizedBox(height: 24),
            FilledButton(
              onPressed: _busy ? null : _register,
              child: Text(_busy ? 'Đang tạo tài khoản…' : 'Đăng ký'),
            ),
            const SizedBox(height: 16),
            Wrap(
              alignment: WrapAlignment.center,
              crossAxisAlignment: WrapCrossAlignment.center,
              children: [
                const Text('Đã có tài khoản?'),
                TextButton(
                  onPressed: _busy ? null : widget.onNavigateToLogin,
                  child: const Text('Đăng nhập'),
                ),
              ],
            ),
          ],
        ),
      ),
    ),
  );
}
