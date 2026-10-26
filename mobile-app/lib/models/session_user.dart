class SessionUser {
  const SessionUser({
    required this.accountId,
    required this.fullName,
    required this.email,
    required this.role,
    this.avatarDataUrl,
    this.hasPassword = true,
  });

  final int? accountId;
  final String fullName;
  final String email;
  final String role;
  final String? avatarDataUrl;
  final bool hasPassword;

  SessionUser copyWith({
    String? fullName,
    String? email,
    String? avatarDataUrl,
  }) => SessionUser(
    accountId: accountId,
    fullName: fullName ?? this.fullName,
    email: email ?? this.email,
    role: role,
    hasPassword: hasPassword,
    avatarDataUrl: avatarDataUrl ?? this.avatarDataUrl,
  );

  factory SessionUser.fromJson(Map<String, dynamic> json) => SessionUser(
    accountId: _asInt(json['taiKhoanID']),
    fullName: (json['tenDangNhap'] ?? '').toString(),
    email: (json['email'] ?? '').toString(),
    role: (json['vaiTro'] ?? 'User').toString(),
    avatarDataUrl: json['avatarDataUrl']?.toString(),
    hasPassword: json['hasPassword'] != false,
  );

  static int? _asInt(dynamic value) =>
      value is num ? value.toInt() : int.tryParse(value?.toString() ?? '');
}
