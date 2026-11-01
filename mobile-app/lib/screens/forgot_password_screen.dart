import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../core/api_service.dart';
import '../widgets/auth_frame.dart';
import '../widgets/app_widgets.dart';

class ForgotPasswordScreen extends StatefulWidget {
  const ForgotPasswordScreen({super.key, this.initialEmail = ''});
  final String initialEmail;
  @override
  State<ForgotPasswordScreen> createState() => _ForgotPasswordScreenState();
}

class _ForgotPasswordScreenState extends State<ForgotPasswordScreen> {
  final _form = GlobalKey<FormState>();
  late final _email = TextEditingController(text: widget.initialEmail);
  final _code = TextEditingController(), _password = TextEditingController();
  String? _challenge, _error;
  bool _busy = false, _showPassword = false, _done = false;
  int _wait = 0;
  Timer? _timer;
  @override
  void dispose() {
    _timer?.cancel();
    _email.dispose();
    _code.dispose();
    _password.dispose();
    super.dispose();
  }

  Future<void> _send() async {
    if (_busy || _wait > 0) return;
    if (!RegExp(r'^[^\s@]+@[^\s@]+\.[^\s@]+$').hasMatch(_email.text.trim())) {
      setState(() => _error = 'Hãy nhập email hợp lệ.');
      return;
    }
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      final result = await ApiService.requestPasswordReset(_email.text);
      final id = result['challengeId'];
      if (id is! String || id.isEmpty) {
        throw const ApiException('Chưa nhận được yêu cầu hợp lệ. Hãy thử lại.');
      }
      if (!mounted) return;
      setState(() {
        _challenge = id;
        _wait = 60;
        _code.clear();
      });
      _timer?.cancel();
      _timer = Timer.periodic(const Duration(seconds: 1), (timer) {
        if (!mounted) {
          timer.cancel();
          return;
        }
        setState(() => _wait--);
        if (_wait <= 0) timer.cancel();
      });
    } on ApiException catch (error) {
      if (mounted) setState(() => _error = error.message);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _reset() async {
    if (_busy || !_form.currentState!.validate() || _challenge == null) return;
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      await ApiService.resetPassword(_challenge!, _code.text, _password.text);
      if (mounted) {
        _timer?.cancel();
        setState(() => _done = true);
      }
    } on ApiException catch (error) {
      if (mounted) setState(() => _error = error.message);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(title: const Text('Khôi phục mật khẩu')),
    body: AuthFrame(
      title: _done ? 'Mật khẩu đã được cập nhật' : 'Lấy lại tài khoản',
      subtitle: _done
          ? 'Hãy dùng mật khẩu mới để đăng nhập lại.'
          : _challenge == null
          ? 'Nhận mã xác nhận qua email đã đăng ký.'
          : 'Nếu email thuộc tài khoản đang hoạt động, mã sẽ được gửi đến hộp thư. Mã dùng một lần, có hiệu lực 10 phút.',
      child: _done
          ? FilledButton(
              onPressed: () => Navigator.of(context).pop(true),
              child: const Text('Về đăng nhập'),
            )
          : Form(
              key: _form,
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  if (_error != null)
                    StatusCard(message: _error!, icon: Icons.info_outline),
                  TextFormField(
                    controller: _email,
                    enabled: !_busy && _challenge == null,
                    keyboardType: TextInputType.emailAddress,
                    autocorrect: false,
                    maxLength: 150,
                    decoration: const InputDecoration(
                      labelText: 'Email đã đăng ký',
                      prefixIcon: Icon(Icons.mail_outline),
                    ),
                  ),
                  if (_challenge != null) ...[
                    const SizedBox(height: 12),
                    TextFormField(
                      controller: _code,
                      enabled: !_busy,
                      keyboardType: TextInputType.number,
                      autofillHints: const [AutofillHints.oneTimeCode],
                      inputFormatters: [FilteringTextInputFormatter.digitsOnly],
                      maxLength: 6,
                      decoration: const InputDecoration(
                        labelText: 'Mã xác nhận 6 số',
                      ),
                      validator: (value) =>
                          RegExp(r'^[0-9]{6}$').hasMatch(value ?? '')
                          ? null
                          : 'Nhập đủ mã xác nhận 6 số.',
                    ),
                    const SizedBox(height: 12),
                    TextFormField(
                      controller: _password,
                      enabled: !_busy,
                      obscureText: !_showPassword,
                      autofillHints: const [AutofillHints.newPassword],
                      maxLength: 128,
                      enableSuggestions: false,
                      autocorrect: false,
                      decoration: InputDecoration(
                        labelText: 'Mật khẩu mới',
                        helperText: 'Từ 8 đến 128 ký tự.',
                        suffixIcon: IconButton(
                          tooltip: _showPassword
                              ? 'Ẩn mật khẩu'
                              : 'Hiện mật khẩu',
                          onPressed: () =>
                              setState(() => _showPassword = !_showPassword),
                          icon: Icon(
                            _showPassword
                                ? Icons.visibility_off_outlined
                                : Icons.visibility_outlined,
                          ),
                        ),
                      ),
                      validator: (value) => (value?.length ?? 0) >= 8
                          ? null
                          : 'Mật khẩu cần ít nhất 8 ký tự.',
                    ),
                    const SizedBox(height: 16),
                    FilledButton(
                      onPressed: _busy ? null : _reset,
                      child: Text(_busy ? 'Đang xử lý…' : 'Đặt mật khẩu mới'),
                    ),
                    TextButton(
                      onPressed: _busy || _wait > 0 ? null : _send,
                      child: Text(
                        _wait > 0 ? 'Gửi lại sau $_wait giây' : 'Gửi lại mã',
                      ),
                    ),
                    TextButton(
                      onPressed: _busy
                          ? null
                          : () => setState(() {
                              _challenge = null;
                              _error = null;
                              _wait = 0;
                              _timer?.cancel();
                            }),
                      child: const Text('Dùng email khác'),
                    ),
                  ] else ...[
                    const SizedBox(height: 16),
                    FilledButton(
                      onPressed: _busy ? null : _send,
                      child: Text(
                        _busy ? 'Đang gửi yêu cầu…' : 'Gửi mã xác nhận',
                      ),
                    ),
                  ],
                ],
              ),
            ),
    ),
  );
}
