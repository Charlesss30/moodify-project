import 'package:flutter/material.dart';

import '../core/api_service.dart';
import '../core/colors.dart';
import '../models/session_user.dart';

class ProfileScreen extends StatelessWidget {
  const ProfileScreen({
    super.key,
    required this.user,
    required this.onLogoutConfirmed,
  });

  final SessionUser user;
  final VoidCallback onLogoutConfirmed;

  Future<void> _confirmLogout(BuildContext context) async {
    final shouldLogout = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('Đăng xuất khỏi Moodify?'),
        content: const Text('Phiên hiện tại sẽ kết thúc trên thiết bị này.'),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context, false),
            child: const Text('Ở lại'),
          ),
          FilledButton.tonal(
            onPressed: () => Navigator.pop(context, true),
            child: const Text('Đăng xuất'),
          ),
        ],
      ),
    );
    if (shouldLogout == true) onLogoutConfirmed();
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(title: const Text('Cá nhân')),
    body: ListView(
      padding: const EdgeInsets.all(20),
      children: [
        CircleAvatar(
          radius: 34,
          backgroundColor: AppColors.surfaceSecondary,
          child: Text(
            user.fullName.isNotEmpty
                ? user.fullName.characters.first.toUpperCase()
                : 'M',
            style: const TextStyle(
              fontSize: 24,
              color: AppColors.accentPrimary,
            ),
          ),
        ),
        const SizedBox(height: 14),
        Text(
          user.fullName,
          textAlign: TextAlign.center,
          style: Theme.of(context).textTheme.titleLarge
              ?.copyWith(fontWeight: FontWeight.w700),
        ),
        Text(
          user.email,
          textAlign: TextAlign.center,
          style: const TextStyle(color: AppColors.textSecondary),
        ),
        const SizedBox(height: 28),
        _ProfileRow(
          icon: Icons.verified_user_outlined,
          label: 'Vai trò',
          value: user.role,
        ),
        _ProfileRow(
          icon: Icons.cloud_outlined,
          label: 'Máy chủ API',
          value: ApiService.baseUrl,
        ),
        const Padding(
          padding: EdgeInsets.symmetric(vertical: 12),
          child: Text(
            'Phiên đăng nhập chỉ được giữ trong bộ nhớ ứng dụng.',
            style: TextStyle(color: AppColors.textSecondary, fontSize: 12),
          ),
        ),
        const SizedBox(height: 12),
        ListTile(
          leading: const Icon(Icons.keyboard_outlined),
          title: const Text('Nhập tiếng Việt'),
          subtitle: const Text('Hướng dẫn bật Telex hoặc VNI'),
          onTap: () => showDialog<void>(
            context: context,
            builder: (context) => AlertDialog(
              title: const Text('Bàn phím tiếng Việt'),
              content: const SingleChildScrollView(
                child: Text(
                  'Ứng dụng hỗ trợ nhập tiếng Việt có dấu. Kiểu gõ do bàn phím trên thiết bị cung cấp.\n\nAndroid / Gboard: mở cài đặt bàn phím → Ngôn ngữ → Thêm bàn phím → Tiếng Việt → chọn Telex hoặc VNI nếu có.\n\nTrên giả lập: bật bàn phím ảo trong cài đặt bàn phím vật lý, rồi chọn Tiếng Việt bằng phím quả địa cầu hoặc giữ phím cách. Nếu bàn phím hiện tại không có tiếng Việt, cần cài bàn phím hỗ trợ tiếng Việt.\n\niPhone: Cài đặt → Cài đặt chung → Bàn phím → Thêm bàn phím mới → Tiếng Việt.\n\nVí dụ Telex: “tieengs Vieetj” → “tiếng Việt”; ứng dụng không tự chuyển chữ không dấu thành có dấu.',
                ),
              ),
              actions: [
                TextButton(
                  onPressed: () => Navigator.pop(context),
                  child: const Text('Đã hiểu'),
                ),
              ],
            ),
          ),
        ),
        OutlinedButton.icon(
          onPressed: () => _confirmLogout(context),
          icon: const Icon(Icons.logout),
          label: const Text('Đăng xuất'),
        ),
      ],
    ),
  );
}

class _ProfileRow extends StatelessWidget {
  const _ProfileRow({
    required this.icon,
    required this.label,
    required this.value,
  });

  final IconData icon;
  final String label;
  final String value;

  @override
  Widget build(BuildContext context) => ListTile(
    contentPadding: EdgeInsets.zero,
    leading: Icon(icon, color: AppColors.accentPrimary),
    title: Text(label),
    subtitle: Text(
      value,
      style: const TextStyle(color: AppColors.textSecondary),
    ),
  );
}
