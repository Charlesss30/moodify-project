import 'dart:convert';

import 'package:flutter/foundation.dart';
import 'package:http/http.dart' as http;

class ApiService {
  static String? _token;
  static void logout() { _token = null; }

  static Future<dynamic> request(String path, {String method = 'GET', Map<String, dynamic>? body}) async {
    final req = http.Request(method, Uri.parse('$baseUrl/api$path'));
    req.headers['Content-Type'] = 'application/json';
    if (_token != null) req.headers['Authorization'] = 'Bearer $_token';
    if (body != null) req.body = jsonEncode(body);
    final response = await http.Response.fromStream(await req.send().timeout(const Duration(seconds: 20)));
    if (response.statusCode == 401) _token = null;
    final data = response.body.isEmpty ? null : jsonDecode(response.body);
    if (response.statusCode < 200 || response.statusCode >= 300) {
      throw Exception(data is Map ? data['message'] ?? 'Yêu cầu thất bại (${response.statusCode}).' : 'Yêu cầu thất bại.');
    }
    return data;
  }
  static Future<dynamic> moods() => request('/MoodMapping');
  static Future<dynamic> recommend(int moodId, String contentType) =>
      request('/Recommendation', method: 'POST', body: {'tamTrangID': moodId, 'loaiNoiDung': contentType});
  static Future<dynamic> history() => request('/History');
  static Future<dynamic> rate(String contentId, int stars, String comment) =>
      request('/History/ratings', method: 'POST', body: {'noiDungID': contentId, 'soSao': stars, 'nhanXet': comment});

  static String get baseUrl {
    if (kIsWeb) {
      return 'http://localhost:5170';
    } else if (defaultTargetPlatform == TargetPlatform.android) {
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
        _token = data['token'] as String?;
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