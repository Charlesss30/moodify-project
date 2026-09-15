class UserAccount {
  final String fullName;
  final String email;
  final String password; // Giả lập mật khẩu đã mã hóa
  final String role;     // NguoiDung / QuanTriVien / KySuAI
  final bool isLocked;

  UserAccount({
    required this.fullName,
    required this.email,
    required this.password,
    this.role = 'NguoiDung',
    this.isLocked = false,
  });
}

class MockDatabase {
  // Danh sách tài khoản mẫu ban đầu trong CSDL
  static final List<UserAccount> users = [
    UserAccount(
      fullName: 'Alex Morgan',
      email: 'alex.designer@example.com',
      password: 'password123',
      role: 'NguoiDung',
      isLocked: false,
    ),
    UserAccount(
      fullName: 'Blocked User',
      email: 'locked@example.com',
      password: 'password123',
      role: 'NguoiDung',
      isLocked: true, // Tài khoản bị khoá
    ),
  ];

  // UC01 - Dòng 5: Kiểm tra tính duy nhất
  static bool isEmailExists(String email) {
    return users.any((u) => u.email.toLowerCase() == email.toLowerCase().trim());
  }

  // UC01 - Dòng 6: Mã hoá và lưu người dùng mới
  static void registerUser({
    required String fullName,
    required String email,
    required String password,
  }) {
    users.add(UserAccount(
      fullName: fullName,
      email: email.trim(),
      password: password,
      role: 'NguoiDung',
    ));
  }

  // UC02 - Dòng 5 & 6: Truy vấn tài khoản và xác thực mật khẩu
  static UserAccount? authenticate(String identifier, String password) {
    final cleanId = identifier.trim().toLowerCase();
    try {
      return users.firstWhere(
        (u) => (u.email.toLowerCase() == cleanId || u.fullName.toLowerCase() == cleanId) && u.password == password,
      );
    } catch (_) {
      return null;
    }
  }

  // Kiểm tra xem tài khoản có tồn tại nhưng bị khóa hay không
  static bool checkIsLocked(String identifier) {
    final cleanId = identifier.trim().toLowerCase();
    final match = users.where(
      (u) => u.email.toLowerCase() == cleanId || u.fullName.toLowerCase() == cleanId,
    );
    if (match.isNotEmpty) {
      return match.first.isLocked;
    }
    return false;
  }
}