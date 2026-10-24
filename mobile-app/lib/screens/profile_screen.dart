import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:image_picker/image_picker.dart';

import '../core/api_service.dart';
import '../core/colors.dart';
import 'statistics_screen.dart';
import 'my_ratings_screen.dart';
import 'saved_content_screen.dart';
import '../widgets/profile_weekly_overview.dart';
import '../models/session_user.dart';

class ProfileScreen extends StatefulWidget {
  const ProfileScreen({
    super.key,
    required this.user,
    required this.onUserUpdated,
    required this.onLogoutConfirmed,
    this.onOpenSaved,
    this.active = true,
  });
  final SessionUser user;
  final ValueChanged<SessionUser> onUserUpdated;
  final VoidCallback onLogoutConfirmed;
  final VoidCallback? onOpenSaved;
  final bool active;
  @override
  State<ProfileScreen> createState() => _ProfileScreenState();
}

class _ProfileScreenState extends State<ProfileScreen> {
  late SessionUser _user;
  late ImageProvider? _avatar;
  @override
  void initState() {
    super.initState();
    _user = widget.user;
    _avatar = _avatarImage(_user.avatarDataUrl);
  }

  @override
  void didUpdateWidget(covariant ProfileScreen oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.user != widget.user) {
      if (_user.avatarDataUrl != widget.user.avatarDataUrl) {
        _avatar = _avatarImage(widget.user.avatarDataUrl);
      }
      _user = widget.user;
    }
  }

  Future<void> _editAccount() async {
    final result = await showDialog<SessionUser>(
      context: context,
      builder: (_) => _EditAccountDialog(user: _user),
    );
    if (result != null && mounted) {
      setState(() {
        _user = result;
        _avatar = _avatarImage(result.avatarDataUrl);
      });
      widget.onUserUpdated(result);
      _notice('Đã cập nhật thông tin tài khoản.');
    }
  }

  Future<void> _changePassword() async {
    final changed = await showDialog<bool>(
      context: context,
      builder: (_) => const _ChangePasswordDialog(),
    );
    if (changed == true && mounted) {
      _notice('Đã đổi mật khẩu thành công.');
      await _confirmLogout();
    }
  }

  void _notice(String text) =>
      ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(text)));
  Future<void> _confirmLogout() async {
    final ok = await showDialog<bool>(
      context: context,
      builder: (c) => AlertDialog(
        title: const Text('Đăng xuất khỏi Moodify?'),
        content: const Text('Phiên hiện tại sẽ kết thúc trên thiết bị này.'),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(c, false),
            child: const Text('Ở lại'),
          ),
          FilledButton.tonal(
            onPressed: () => Navigator.pop(c, true),
            child: const Text('Đăng xuất'),
          ),
        ],
      ),
    );
    if (ok == true) widget.onLogoutConfirmed();
  }

  Widget _row(
    IconData icon,
    String title,
    String subtitle,
    VoidCallback action,
  ) => ListTile(
    contentPadding: const EdgeInsets.symmetric(horizontal: 16, vertical: 7),
    leading: Container(
      width: 40,
      height: 40,
      decoration: BoxDecoration(
        color: AppColors.accentPrimary.withValues(alpha: .12),
        borderRadius: BorderRadius.circular(12),
      ),
      child: Icon(icon, color: Theme.of(context).colorScheme.primary, size: 22),
    ),
    title: Text(title, style: Theme.of(context).textTheme.titleSmall),
    subtitle: Text(subtitle, style: Theme.of(context).textTheme.bodySmall),
    trailing: const Icon(Icons.chevron_right, size: 20),
    onTap: action,
  );
  void _openStatistics() => Navigator.of(context)
      .push(MaterialPageRoute<void>(builder: (_) => const StatisticsScreen()));

  void _openSaved() {
    if (widget.onOpenSaved != null) {
      widget.onOpenSaved!();
    } else {
      Navigator.of(context).push(
        MaterialPageRoute<void>(builder: (_) => const SavedContentScreen()),
      );
    }
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(title: const Text('Cá nhân')),
    body: ListView(
      key: const PageStorageKey('profile-scroll'),
      padding: const EdgeInsets.fromLTRB(20, 4, 20, 28),
      children: [
        Card(
          child: Padding(
            padding: const EdgeInsets.all(20),
            child: Row(
              children: [
                _AccountAvatar(
                  radius: 34,
                  image: _avatar,
                  fallback: Text(
                    _user.fullName.isNotEmpty
                        ? _user.fullName.characters.first.toUpperCase()
                        : 'M',
                    style: TextStyle(
                      fontSize: 26,
                      color: Theme.of(context).colorScheme.primary,
                    ),
                  ),
                ),
                const SizedBox(width: 16),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        _user.fullName.isEmpty
                            ? 'Người dùng Moodify'
                            : _user.fullName,
                        maxLines: 2,
                        overflow: TextOverflow.ellipsis,
                        style: Theme.of(context).textTheme.titleLarge,
                      ),
                      const SizedBox(height: 4),
                      Text(
                        _user.email,
                        maxLines: 2,
                        overflow: TextOverflow.ellipsis,
                        style: Theme.of(context).textTheme.bodySmall,
                      ),
                    ],
                  ),
                ),
              ],
            ),
          ),
        ),
        ProfileWeeklyOverview(
          key: ValueKey(_user.accountId),
          active: widget.active,
          onOpenStatistics: _openStatistics,
        ),
        const SizedBox(height: 12),
        Text(
          'TÀI KHOẢN',
          style: Theme.of(context).textTheme.labelSmall
              ?.copyWith(letterSpacing: 1.8, color: AppColors.textSecondary),
        ),
        const SizedBox(height: 10),
        Card(
          child: Column(
            children: [
              _row(
                Icons.person_outline,
                'Thông tin tài khoản',
                'Tên người dùng, ảnh đại diện và email',
                _editAccount,
              ),
              const Divider(indent: 16, endIndent: 16),
              _row(
                Icons.lock_outline,
                'Đổi mật khẩu',
                'Bảo vệ tài khoản của bạn',
                _changePassword,
              ),
            ],
          ),
        ),
        const SizedBox(height: 12),
        Text(
          'HÀNH TRÌNH CỦA BẠN',
          style: Theme.of(context).textTheme.labelSmall
              ?.copyWith(letterSpacing: 1.8, color: AppColors.textSecondary),
        ),
        const SizedBox(height: 10),
        Card(
          child: Column(
            children: [
              _row(
                Icons.rate_review_outlined,
                'Đánh giá của tôi',
                'Xem lại số sao và những cảm nhận đã chia sẻ',
                () => Navigator.of(context).push(
                  MaterialPageRoute<void>(
                    builder: (_) => const MyRatingsScreen(),
                  ),
                ),
              ),
              const Divider(indent: 16, endIndent: 16),
              _row(
                Icons.bookmark_outline,
                'Nội dung đã lưu',
                'Phim muốn xem, nhạc muốn nghe lại',
                _openSaved,
              ),
            ],
          ),
        ),
        const SizedBox(height: 24),
        OutlinedButton.icon(
          onPressed: _confirmLogout,
          icon: const Icon(Icons.logout),
          label: const Text('Đăng xuất'),
        ),
      ],
    ),
  );
}

class _EditAccountDialog extends StatefulWidget {
  const _EditAccountDialog({required this.user});
  final SessionUser user;
  @override
  State<_EditAccountDialog> createState() => _EditAccountDialogState();
}

class _EditAccountDialogState extends State<_EditAccountDialog> {
  final _form = GlobalKey<FormState>();
  late final _name = TextEditingController(text: widget.user.fullName);
  late final _email = TextEditingController(text: widget.user.email);
  final _password = TextEditingController();
  String? _avatarDataUrl, _error;
  late ImageProvider? _avatar = _avatarImage(widget.user.avatarDataUrl);
  bool _busy = false, _picking = false, _showPassword = false;
  bool get _emailChanged =>
      _email.text.trim().toLowerCase() != widget.user.email.toLowerCase();
  @override
  void dispose() {
    _name.dispose();
    _email.dispose();
    _password.dispose();
    super.dispose();
  }

  Future<void> _pickAvatar(ImageSource source) async {
    setState(() {
      _picking = true;
      _error = null;
    });
    try {
      final image = await ImagePicker().pickImage(
        source: source,
        maxWidth: 512,
        maxHeight: 512,
        imageQuality: 82,
      );
      if (image == null) return;
      final bytes = await image.readAsBytes();
      if (!mounted) return;
      setState(() {
        _avatarDataUrl =
            'data:${image.mimeType ?? 'image/jpeg'};base64,${base64Encode(bytes)}';
        _avatar = MemoryImage(bytes);
      });
    } on PlatformException {
      if (mounted) {
        setState(
          () => _error = 'Chưa mở được ảnh. Hãy kiểm tra quyền camera hoặc thư viện trên thiết bị.',
        );
      }
    } catch (_) {
      if (mounted) {
        setState(() => _error = 'Chưa đọc được ảnh này. Hãy chọn ảnh khác.');
      }
    } finally {
      if (mounted) setState(() => _picking = false);
    }
  }

  Future<void> _chooseAvatar() async {
    FocusManager.instance.primaryFocus?.unfocus();
    final source = await showModalBottomSheet<ImageSource>(
      context: context,
      builder: (context) => SafeArea(
        child: Wrap(
          children: [
            ListTile(
              leading: const Icon(Icons.photo_library_outlined),
              title: const Text('Chọn ảnh từ máy'),
              onTap: () => Navigator.pop(context, ImageSource.gallery),
            ),
            ListTile(
              leading: const Icon(Icons.photo_camera_outlined),
              title: const Text('Chụp ảnh mới'),
              onTap: () => Navigator.pop(context, ImageSource.camera),
            ),
          ],
        ),
      ),
    );
    if (source != null && mounted) await _pickAvatar(source);
  }

  Future<void> _save() async {
    if (_busy || _picking || !_form.currentState!.validate()) return;
    FocusManager.instance.primaryFocus?.unfocus();
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      final updated = await ApiService.updateAccountProfile(
        username: _name.text.trim(),
        email: _email.text.trim(),
        currentPassword: _emailChanged ? _password.text : null,
        avatarDataUrl: _avatarDataUrl,
      );
      if (mounted) {
        Navigator.pop(
          context,
          widget.user.copyWith(
            fullName: updated.fullName,
            email: updated.email,
            avatarDataUrl: updated.avatarDataUrl,
          ),
        );
      }
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
  Widget build(BuildContext context) => PopScope(
    canPop: !_busy && !_picking,
    child: AlertDialog(
      title: const Text('Thông tin tài khoản'),
      content: SizedBox(
        width: double.maxFinite,
        child: SingleChildScrollView(
          child: Form(
            key: _form,
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                GestureDetector(
                  onTap: _busy || _picking ? null : _chooseAvatar,
                  child: _AccountAvatar(
                    radius: 38,
                    image: _avatar,
                    loading: _picking,
                    fallback: const Icon(Icons.add_a_photo_outlined),
                  ),
                ),
                TextButton.icon(
                  onPressed: _busy || _picking ? null : _chooseAvatar,
                  icon: const Icon(Icons.photo_camera_outlined),
                  label: const Text('Thay ảnh đại diện'),
                ),
                const SizedBox(height: 10),
                TextFormField(
                  controller: _name,
                  enabled: !_busy && !_picking,
                  autofillHints: const [AutofillHints.username],
                  textInputAction: TextInputAction.next,
                  hintLocales: const [Locale('vi', 'VN'), Locale('en')],
                  decoration: const InputDecoration(
                    labelText: 'Tên người dùng',
                  ),
                  validator: (v) =>
                      (v?.trim().length ?? 0) < 2 ||
                          (v?.trim().length ?? 0) > 100
                      ? 'Tên người dùng cần từ 2 đến 100 ký tự.'
                      : null,
                ),
                const SizedBox(height: 14),
                TextFormField(
                  controller: _email,
                  enabled: !_busy && !_picking,
                  autofillHints: const [AutofillHints.email],
                  keyboardType: TextInputType.emailAddress,
                  textInputAction: _emailChanged
                      ? TextInputAction.next
                      : TextInputAction.done,
                  onFieldSubmitted: (_) {
                    if (!_emailChanged) {
                      _save();
                    }
                  },
                  autocorrect: false,
                  onChanged: (_) => setState(() {}),
                  decoration: const InputDecoration(labelText: 'Email'),
                  validator: (v) =>
                      (v?.trim().length ?? 0) <= 150 &&
                          RegExp(r'^\S+@\S+\.\S+$').hasMatch(v?.trim() ?? '')
                      ? null
                      : 'Nhập email hợp lệ.',
                ),
                if (_emailChanged) ...[
                  const SizedBox(height: 14),
                  TextFormField(
                    controller: _password,
                    enabled: !_busy && !_picking,
                    autofillHints: const [AutofillHints.password],
                    textInputAction: TextInputAction.done,
                    obscureText: !_showPassword,
                    enableSuggestions: false,
                    autocorrect: false,
                    onFieldSubmitted: (_) => _save(),
                    decoration: InputDecoration(
                      labelText: 'Mật khẩu hiện tại',
                      floatingLabelBehavior: FloatingLabelBehavior.always,
                      helperText: 'Xác nhận mật khẩu để thay đổi email.',
                      helperMaxLines: 2,
                      suffixIcon: IconButton(
                        tooltip: _showPassword
                            ? 'Ẩn mật khẩu'
                            : 'Hiện mật khẩu',
                        onPressed: _busy || _picking
                            ? null
                            : () => setState(
                                () => _showPassword = !_showPassword,
                              ),
                        icon: Icon(
                          _showPassword
                              ? Icons.visibility_off_outlined
                              : Icons.visibility_outlined,
                        ),
                      ),
                    ),
                    validator: (v) => v == null || v.isEmpty
                        ? 'Nhập mật khẩu hiện tại để xác nhận.'
                        : null,
                  ),
                ],
                if (_error != null)
                  Padding(
                    padding: const EdgeInsets.only(top: 12),
                    child: Text(
                      _error!,
                      style: TextStyle(
                        color: Theme.of(context).colorScheme.error,
                      ),
                    ),
                  ),
              ],
            ),
          ),
        ),
      ),
      actions: [
        TextButton(
          onPressed: _busy || _picking ? null : () => Navigator.pop(context),
          child: const Text('Huỷ'),
        ),
        FilledButton(
          onPressed: _busy || _picking ? null : _save,
          child: Text(_busy ? 'Đang lưu…' : 'Lưu'),
        ),
      ],
    ),
  );
}

class _AccountAvatar extends StatelessWidget {
  const _AccountAvatar({
    required this.image,
    required this.radius,
    required this.fallback,
    this.loading = false,
  });

  final ImageProvider? image;
  final double radius;
  final Widget fallback;
  final bool loading;

  @override
  Widget build(BuildContext context) => CircleAvatar(
    radius: radius,
    backgroundColor: AppColors.surfaceSecondary,
    child: loading
        ? const CircularProgressIndicator(strokeWidth: 2)
        : image == null
        ? fallback
        : ClipOval(
            child: Image(
              image: image!,
              width: radius * 2,
              height: radius * 2,
              fit: BoxFit.cover,
              errorBuilder: (context, error, stackTrace) =>
                  Center(child: fallback),
            ),
          ),
  );
}

ImageProvider? _avatarImage(String? dataUrl) {
  if (dataUrl == null || !dataUrl.startsWith('data:image/')) return null;
  final separator = dataUrl.indexOf(',');
  if (separator < 0) return null;
  try {
    return MemoryImage(base64Decode(dataUrl.substring(separator + 1)));
  } on FormatException {
    return null;
  }
}

class _ChangePasswordDialog extends StatefulWidget {
  const _ChangePasswordDialog();
  @override
  State<_ChangePasswordDialog> createState() => _ChangePasswordDialogState();
}

class _ChangePasswordDialogState extends State<_ChangePasswordDialog> {
  final _form = GlobalKey<FormState>();
  final _current = TextEditingController(),
      _next = TextEditingController(),
      _confirm = TextEditingController();
  bool _busy = false;
  bool _showCurrent = false, _showNext = false, _showConfirm = false;
  String? _error;
  @override
  void dispose() {
    _current.dispose();
    _next.dispose();
    _confirm.dispose();
    super.dispose();
  }

  Future<void> _save() async {
    if (_busy || !_form.currentState!.validate()) return;
    FocusManager.instance.primaryFocus?.unfocus();
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      await ApiService.changePassword(
        currentPassword: _current.text,
        newPassword: _next.text,
      );
      if (mounted) Navigator.pop(context, true);
    } on ApiException catch (e) {
      if (mounted) {
        setState(() {
          _busy = false;
          _error = e.message;
        });
      }
    }
  }

  Widget _visibilityButton(bool visible, VoidCallback toggle) => IconButton(
    tooltip: visible ? 'Ẩn mật khẩu' : 'Hiện mật khẩu',
    onPressed: _busy ? null : toggle,
    icon: Icon(
      visible ? Icons.visibility_off_outlined : Icons.visibility_outlined,
    ),
  );

  @override
  Widget build(BuildContext context) => PopScope(
    canPop: !_busy,
    child: AlertDialog(
      title: const Text('Đổi mật khẩu'),
      content: SizedBox(
        width: double.maxFinite,
        child: SingleChildScrollView(
          child: Form(
            key: _form,
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                TextFormField(
                  controller: _current,
                  autofillHints: const [AutofillHints.password],
                  obscureText: !_showCurrent,
                  enabled: !_busy,
                  textInputAction: TextInputAction.next,
                  enableSuggestions: false,
                  autocorrect: false,
                  decoration: InputDecoration(
                    labelText: 'Mật khẩu hiện tại',
                    floatingLabelBehavior: FloatingLabelBehavior.always,
                    suffixIcon: _visibilityButton(
                      _showCurrent,
                      () => setState(() => _showCurrent = !_showCurrent),
                    ),
                  ),
                  validator: (v) =>
                      v == null || v.isEmpty ? 'Nhập mật khẩu hiện tại.' : null,
                ),
                const SizedBox(height: 14),
                TextFormField(
                  controller: _next,
                  autofillHints: const [AutofillHints.newPassword],
                  obscureText: !_showNext,
                  enabled: !_busy,
                  textInputAction: TextInputAction.next,
                  enableSuggestions: false,
                  autocorrect: false,
                  decoration: InputDecoration(
                    labelText: 'Mật khẩu mới',
                    floatingLabelBehavior: FloatingLabelBehavior.always,
                    helperText: 'Từ 8 đến 128 ký tự, khác mật khẩu hiện tại.',
                    helperMaxLines: 2,
                    suffixIcon: _visibilityButton(
                      _showNext,
                      () => setState(() => _showNext = !_showNext),
                    ),
                  ),
                  validator: (v) {
                    if ((v?.length ?? 0) < 8 || (v?.length ?? 0) > 128) {
                      return 'Mật khẩu mới cần từ 8 đến 128 ký tự.';
                    }
                    if (v == _current.text) {
                      return 'Mật khẩu mới cần khác mật khẩu hiện tại.';
                    }
                    return null;
                  },
                ),
                const SizedBox(height: 14),
                TextFormField(
                  controller: _confirm,
                  autofillHints: const [AutofillHints.newPassword],
                  obscureText: !_showConfirm,
                  enabled: !_busy,
                  textInputAction: TextInputAction.done,
                  enableSuggestions: false,
                  autocorrect: false,
                  onFieldSubmitted: (_) => _save(),
                  decoration: InputDecoration(
                    labelText: 'Xác nhận mật khẩu mới',
                    floatingLabelBehavior: FloatingLabelBehavior.always,
                    suffixIcon: _visibilityButton(
                      _showConfirm,
                      () => setState(() => _showConfirm = !_showConfirm),
                    ),
                  ),
                  validator: (v) => v != _next.text
                      ? 'Hai lần nhập mật khẩu chưa khớp.'
                      : null,
                ),
                if (_error != null)
                  Padding(
                    padding: const EdgeInsets.only(top: 12),
                    child: Text(
                      _error!,
                      style: TextStyle(
                        color: Theme.of(context).colorScheme.error,
                      ),
                    ),
                  ),
              ],
            ),
          ),
        ),
      ),
      actions: [
        TextButton(
          onPressed: _busy ? null : () => Navigator.pop(context, false),
          child: const Text('Huỷ'),
        ),
        FilledButton(
          onPressed: _busy ? null : _save,
          child: Text(_busy ? 'Đang lưu…' : 'Đổi mật khẩu'),
        ),
      ],
    ),
  );
}
