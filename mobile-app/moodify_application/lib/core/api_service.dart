import 'dart:convert';
import 'dart:io';
import 'package:flutter/foundation.dart';
import 'package:http/http.dart' as http;

class ApiService {
  static String get baseUrl {
    if (kIsWeb) {
      return 'http://localhost:5170';
    } else if (Platform.isAndroid) {
      return 'http://10.0.2.2:5170';
    } else {
      return 'http://localhost:5170';
    }
  }

  // Khớp 100% với LoginDto: identifier, matKhau
  static Future<Map<String, dynamic>> login({
    required String emailOrUsername,
    required String password,
  }) async {
    final url = Uri.parse('$baseUrl/api/Auth/login');
    try {
      final response = await http.post(
        url,
        headers: {'Content-Type': 'application/json'},
        body: jsonEncode({
          'identifier': emailOrUsername.trim(),
          'matKhau': password,
        }),
      );

      final data = response.body.isNotEmpty ? jsonDecode(response.body) : {};

      if (response.statusCode >= 200 && response.statusCode < 300) {
        return {'success': true, 'data': data};
      } else {
        return {
          'success': false,
          'message': data['message'] ?? 'Tên đăng nhập hoặc mật khẩu chưa chính xác.',
        };
      }
    } catch (e) {
      return {
        'success': false,
        'message': 'Không thể kết nối máy chủ ($baseUrl).',
      };
    }
  }

  // Khớp 100% với RegisterDto: tenDangNhap, email, matKhau
  static Future<Map<String, dynamic>> register({
    required String fullName,
    required String email,
    required String password,
  }) async {
    final url = Uri.parse('$baseUrl/api/Auth/register');
    try {
      final response = await http.post(
        url,
        headers: {'Content-Type': 'application/json'},
        body: jsonEncode({
          'tenDangNhap': fullName.trim(),
          'email': email.trim(),
          'matKhau': password,
        }),
      );

      final data = response.body.isNotEmpty ? jsonDecode(response.body) : {};

      if (response.statusCode >= 200 && response.statusCode < 300) {
        return {'success': true, 'data': data};
      } else {
        return {
          'success': false,
          'message': data['message'] ?? 'Đăng ký không thành công.',
        };
      }
    } catch (e) {
      return {
        'success': false,
        'message': 'Lỗi kết nối máy chủ ($baseUrl).',
      };
    }
  }
}