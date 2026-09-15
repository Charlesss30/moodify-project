import 'package:flutter/material.dart';
import 'package:lucide_icons_flutter/lucide_icons.dart';
import '../core/colors.dart';
import '../core/mock_database.dart';

class HomeScreen extends StatefulWidget {
  final UserAccount currentUser;
  final VoidCallback onLogoutConfirmed;

  const HomeScreen({
    super.key,
    required this.currentUser,
    required this.onLogoutConfirmed,
  });

  @override
  State<HomeScreen> createState() => _HomeScreenState();
}

class _HomeScreenState extends State<HomeScreen> {
  int _activeTab = 0;
  final _moodController = TextEditingController();

  // UC03 - Dòng 1 & 2: Nhấp chọn đăng xuất -> Hiển thị hộp thoại xác nhận
  void _showLogoutConfirmationDialog() {
    showDialog(
      context: context,
      barrierDismissible: false,
      builder: (dialogContext) {
        return AlertDialog(
          backgroundColor: AppColors.surface,
          shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(24)),
          contentPadding: const EdgeInsets.all(24),
          content: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              Container(
                width: 48,
                height: 48,
                decoration: BoxDecoration(
                  color: AppColors.danger.withOpacity(0.1),
                  shape: BoxShape.circle,
                ),
                child: const Icon(LucideIcons.logOut, color: AppColors.danger, size: 22),
              ),
              const SizedBox(height: 16),
              const Text(
                'Xác nhận đăng xuất',
                style: TextStyle(fontSize: 18, fontWeight: FontWeight.bold, color: AppColors.textPrimary),
              ),
              const SizedBox(height: 8),
              const Text(
                'Bạn có chắc chắn muốn kết thúc phiên làm việc để bảo mật tài khoản cá nhân?',
                textAlign: TextAlign.center,
                style: TextStyle(fontSize: 12, color: AppColors.textSecondary),
              ),
              const SizedBox(height: 24),
              Row(
                children: [
                  // UC03 - Dòng sự kiện phụ: Người dùng chọn hủy -> Đóng hộp thoại
                  Expanded(
                    child: OutlinedButton(
                      onPressed: () => Navigator.pop(dialogContext),
                      style: OutlinedButton.styleFrom(
                        backgroundColor: AppColors.surfaceSecondary,
                        side: BorderSide(color: Colors.white.withOpacity(0.1)),
                        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(14)),
                        padding: const EdgeInsets.symmetric(vertical: 12),
                      ),
                      child: const Text('Hủy', style: TextStyle(color: AppColors.textPrimary, fontSize: 13)),
                    ),
                  ),
                  const SizedBox(width: 12),
                  // UC03 - Dòng 3, 4, 5: Xác nhận -> Hủy phiên -> Điều hướng về trạng thái vãng lai
                  Expanded(
                    child: ElevatedButton(
                      onPressed: () {
                        Navigator.pop(dialogContext); // Đóng modal
                        widget.onLogoutConfirmed();    // Hủy phiên làm việc
                      },
                      style: ElevatedButton.styleFrom(
                        backgroundColor: AppColors.danger,
                        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(14)),
                        padding: const EdgeInsets.symmetric(vertical: 12),
                      ),
                      child: const Text('Đăng xuất', style: TextStyle(color: Colors.white, fontSize: 13, fontWeight: FontWeight.bold)),
                    ),
                  ),
                ],
              )
            ],
          ),
        );
      },
    );
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: AppColors.background,
      body: Stack(
        children: [
          SafeArea(
            child: SingleChildScrollView(
              padding: const EdgeInsets.only(bottom: 90),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  // Header hiển thị tên user và vai trò từ UC02
                  Padding(
                    padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 12),
                    child: Row(
                      mainAxisAlignment: MainAxisAlignment.spaceBetween,
                      children: [
                        Row(
                          children: [
                            const CircleAvatar(
                              radius: 20,
                              backgroundImage: NetworkImage('https://images.unsplash.com/photo-1534528741775-53994a69daeb?w=100&h=100&fit=crop'),
                            ),
                            const SizedBox(width: 12),
                            Column(
                              crossAxisAlignment: CrossAxisAlignment.start,
                              children: [
                                Text('Xin chào [${widget.currentUser.role}],', style: const TextStyle(fontSize: 11, color: AppColors.textSecondary)),
                                Text(widget.currentUser.fullName, style: const TextStyle(fontSize: 16, fontWeight: FontWeight.bold, color: AppColors.textPrimary)),
                              ],
                            )
                          ],
                        ),
                        IconButton(
                          onPressed: _showLogoutConfirmationDialog, // Nút kích hoạt UC03
                          icon: const Icon(LucideIcons.logOut, size: 18, color: AppColors.textSecondary),
                          style: IconButton.styleFrom(backgroundColor: AppColors.surfaceSecondary),
                        ),
                      ],
                    ),
                  ),

                  const Padding(
                    padding: EdgeInsets.symmetric(horizontal: 20, vertical: 8),
                    child: Text('How are you feeling today?', style: TextStyle(fontSize: 20, fontWeight: FontWeight.bold, color: AppColors.textPrimary)),
                  ),

                  // Card AI Mood Analysis
                  Padding(
                    padding: const EdgeInsets.all(20),
                    child: Container(
                      padding: const EdgeInsets.all(16),
                      decoration: BoxDecoration(
                        color: AppColors.surface,
                        borderRadius: BorderRadius.circular(20),
                        border: Border.all(color: AppColors.accentPrimary.withOpacity(0.2)),
                      ),
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Row(
                            children: const [
                              Icon(LucideIcons.sparkles, color: AppColors.accentPrimary, size: 18),
                              SizedBox(width: 8),
                              Text('AI Mood Analysis', style: TextStyle(fontSize: 13, fontWeight: FontWeight.bold, color: AppColors.textPrimary)),
                            ],
                          ),
                          const SizedBox(height: 10),
                          TextField(
                            controller: _moodController,
                            maxLines: 2,
                            style: const TextStyle(color: AppColors.textPrimary, fontSize: 13),
                            decoration: InputDecoration(
                              hintText: "Tell me how you're feeling...",
                              hintStyle: TextStyle(color: AppColors.textSecondary.withOpacity(0.5)),
                              border: InputBorder.none,
                            ),
                          ),
                          Align(
                            alignment: Alignment.centerRight,
                            child: ElevatedButton(
                              onPressed: () {},
                              style: ElevatedButton.styleFrom(
                                backgroundColor: AppColors.accentPrimary,
                                shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(10)),
                                padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 8),
                              ),
                              child: const Text('Analyze Mood', style: TextStyle(color: Colors.white, fontSize: 12, fontWeight: FontWeight.bold)),
                            ),
                          )
                        ],
                      ),
                    ),
                  )
                ],
              ),
            ),
          ),

          // Thanh Bottom Nav
          Positioned(
            left: 0,
            right: 0,
            bottom: 0,
            child: Container(
              padding: const EdgeInsets.symmetric(horizontal: 24, vertical: 14),
              decoration: BoxDecoration(
                color: AppColors.surface.withOpacity(0.9),
                borderRadius: const BorderRadius.vertical(top: Radius.circular(28)),
              ),
              child: Row(
                mainAxisAlignment: MainAxisAlignment.spaceAround,
                children: [
                  _navItem(LucideIcons.home, 'Home', 0),
                  _navItem(LucideIcons.search, 'Discover', 1),
                  _navItem(LucideIcons.clock, 'History', 2),
                  _navItem(LucideIcons.user, 'Profile', 3),
                ],
              ),
            ),
          )
        ],
      ),
    );
  }

  Widget _navItem(IconData icon, String label, int index) {
    final active = _activeTab == index;
    return GestureDetector(
      onTap: () => setState(() => _activeTab = index),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          Icon(icon, size: 20, color: active ? AppColors.accentPrimary : AppColors.textSecondary),
          const SizedBox(height: 2),
          Text(label, style: TextStyle(fontSize: 10, color: active ? AppColors.accentPrimary : AppColors.textSecondary)),
        ],
      ),
    );
  }
}