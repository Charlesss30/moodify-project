class SessionUser {
  const SessionUser({
    required this.accountId,
    required this.fullName,
    required this.email,
    required this.role,
  });

  final int? accountId;
  final String fullName;
  final String email;
  final String role;

  factory SessionUser.fromJson(Map<String, dynamic> json) => SessionUser(
    accountId: _asInt(json['taiKhoanID']),
    fullName: (json['tenDangNhap'] ?? '').toString(),
    email: (json['email'] ?? '').toString(),
    role: (json['vaiTro'] ?? 'User').toString(),
  );

  static int? _asInt(dynamic value) =>
      value is num ? value.toInt() : int.tryParse(value?.toString() ?? '');
}
