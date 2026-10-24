import 'package:flutter/material.dart';

import '../core/api_service.dart';
import '../models/session_user.dart';
import '../widgets/app_widgets.dart';
import '../widgets/auth_frame.dart';

class LoginScreen extends StatefulWidget {
  const LoginScreen({
    super.key,
    required this.onLoginSuccess,
    required this.onNavigateToRegister,
  });
  final ValueChanged<SessionUser> onLoginSuccess;
  final VoidCallback onNavigateToRegister;
  @override
  State<LoginScreen> createState() => _LoginScreenState();
}

class _LoginScreenState extends State<LoginScreen> {
  final _form = GlobalKey<FormState>();
  final _identifier = TextEditingController(),
      _password = TextEditingController();
  bool _busy = false, _showPassword = false;
  String? _error;
  @override
  void dispose() {
    _identifier.dispose();
    _password.dispose();
    super.dispose();
  }

  Future<void> _login() async {
    if (_busy || !_form.currentState!.validate()) return;
    FocusManager.instance.primaryFocus?.unfocus();
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      final user = await ApiService.login(
        emailOrUsername: _identifier.text,
        password: _password.text,
      );
      if (mounted) widget.onLoginSuccess(user);
    } on ApiException catch (e) {
      if (mounted) {
        setState(() {
          _busy = false;
          _error = e.message;
        });
      }
    }
  }

  @override
  Widget build(BuildContext context) => AuthFrame(
    title: 'Chào mừng trở lại',
    subtitle: 'Một bộ phim, một bài hát — tìm điều hợp với tâm trạng của bạn.',
    child: AutofillGroup(
      child: Form(
        key: _form,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            if (_error != null)
              StatusCard(
                title: 'Chưa đăng nhập được',
                message: _error!,
                icon: Icons.info_outline,
              ),
            TextFormField(
              controller: _identifier,
              enabled: !_busy,
              autofillHints: const [AutofillHints.username],
              textInputAction: TextInputAction.next,
              autocorrect: false,
              decoration: const InputDecoration(
                labelText: 'Email hoặc tên đăng nhập',
                prefixIcon: Icon(Icons.person_outline),
              ),
              validator: (v) => v == null || v.trim().isEmpty
                  ? 'Nhập email hoặc tên đăng nhập.'
                  : null,
            ),
            const SizedBox(height: 16),
            TextFormField(
              controller: _password,
              enabled: !_busy,
              obscureText: !_showPassword,
              enableSuggestions: false,
              autocorrect: false,
              autofillHints: const [AutofillHints.password],
              textInputAction: TextInputAction.done,
              onFieldSubmitted: (_) => _login(),
              decoration: InputDecoration(
                labelText: 'Mật khẩu',
                prefixIcon: const Icon(Icons.lock_outline),
                suffixIcon: IconButton(
                  tooltip: _showPassword ? 'Ẩn mật khẩu' : 'Hiện mật khẩu',
                  onPressed: () =>
                      setState(() => _showPassword = !_showPassword),
                  icon: Icon(
                    _showPassword
                        ? Icons.visibility_off_outlined
                        : Icons.visibility_outlined,
                  ),
                ),
              ),
              validator: (v) =>
                  v == null || v.isEmpty ? 'Nhập mật khẩu của bạn.' : null,
            ),
            const SizedBox(height: 24),
            FilledButton(
              onPressed: _busy ? null : _login,
              child: Text(_busy ? 'Đang đăng nhập…' : 'Đăng nhập'),
            ),
            const SizedBox(height: 16),
            Wrap(
              alignment: WrapAlignment.center,
              crossAxisAlignment: WrapCrossAlignment.center,
              children: [
                const Text('Chưa có tài khoản?'),
                TextButton(
                  onPressed: _busy ? null : widget.onNavigateToRegister,
                  child: const Text('Đăng ký'),
                ),
              ],
            ),
          ],
        ),
      ),
    ),
  );
}
