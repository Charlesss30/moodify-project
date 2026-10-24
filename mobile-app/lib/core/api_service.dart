import 'dart:async';
import 'dart:convert';

import 'package:flutter/foundation.dart';
import 'package:http/http.dart' as http;

import '../models/mobile_models.dart';
import '../models/personal_models.dart';
import '../models/session_user.dart';
import '../services/mood_analysis/mood_analyzer.dart';

class ApiException implements Exception {
  const ApiException(this.message, {this.statusCode});

  final String message;
  final int? statusCode;

  @override
  String toString() => message;
}

class ApiService {
  static String? _token;
  static int _sessionVersion = 0;
  static final historyRevision = ValueNotifier<int>(0);
  static final activityRevision = ValueNotifier<int>(0);
  static final ratingsRevision = ValueNotifier<int>(0);
  static final savedRevision = ValueNotifier<int>(0);
  @visibleForTesting
  static http.Client Function() clientFactory = http.Client.new;
  @visibleForTesting
  static Duration requestTimeout = const Duration(seconds: 20);
  static bool _sessionExpired = false;
  static VoidCallback? onUnauthorized;

  static void logout() {
    _token = null;
    _sessionVersion++;
    _sessionExpired = false;
  }

  static void expireSession() {
    _token = null;
    _sessionVersion++;
    _sessionExpired = true;
  }

  static String get baseUrl {
    const configured = String.fromEnvironment('API_BASE_URL');
    if (configured.trim().isNotEmpty) {
      return configured.trim().replaceFirst(RegExp(r'/$'), '');
    }
    if (kIsWeb) return 'http://localhost:5170';
    if (defaultTargetPlatform == TargetPlatform.android) {
      return 'http://10.0.2.2:5170';
    }
    return 'http://localhost:5170';
  }

  static Future<dynamic> request(
    String path, {
    String method = 'GET',
    Map<String, dynamic>? body,
    bool authenticated = true,
  }) async {
    final request = http.Request(method, Uri.parse('$baseUrl/api$path'));
    final sessionVersion = _sessionVersion;
    final client = clientFactory();
    final hadSession = authenticated && _token != null;
    request.headers['Accept'] = 'application/json';
    if (body != null) {
      request.headers['Content-Type'] = 'application/json';
      request.body = jsonEncode(body);
    }
    if (authenticated && _token != null) {
      request.headers['Authorization'] = 'Bearer $_token';
    }
    try {
      final response = await (() async => http.Response.fromStream(
        await client.send(request),
      ))().timeout(requestTimeout);
      if (authenticated && sessionVersion != _sessionVersion) {
        throw const ApiException('Phiên đăng nhập đã thay đổi.');
      }
      dynamic data;
      try {
        data = _decode(response.body);
      } on FormatException {
        if (response.statusCode >= 200 && response.statusCode < 300) {
          throw const ApiException('Máy chủ trả về dữ liệu không hợp lệ.');
        }
      }
      if (response.statusCode == 401) {
        if (hadSession) {
          _token = null;
          _sessionVersion++;
          if (!_sessionExpired) {
            _sessionExpired = true;
            onUnauthorized?.call();
          }
          throw const ApiException(
            'Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.',
            statusCode: 401,
          );
        }
        throw ApiException(
          _message(data) ?? 'Thông tin đăng nhập chưa chính xác.',
          statusCode: 401,
        );
      }
      if (response.statusCode == 403) {
        throw ApiException(
          _message(data) ?? 'Tài khoản không có quyền thực hiện thao tác này.',
          statusCode: 403,
        );
      }
      if (response.statusCode < 200 || response.statusCode >= 300) {
        throw ApiException(
          _message(data) ?? 'Yêu cầu thất bại (HTTP ${response.statusCode}).',
          statusCode: response.statusCode,
        );
      }
      return data;
    } on ApiException {
      rethrow;
    } on TimeoutException {
      throw const ApiException('Máy chủ phản hồi quá lâu. Hãy thử lại.');
    } on http.ClientException {
      throw const ApiException(
        'Chưa kết nối được dịch vụ. Hãy kiểm tra mạng và thử lại.',
      );
    } on FormatException {
      throw const ApiException('Máy chủ trả về dữ liệu không hợp lệ.');
    } finally {
      client.close();
    }
  }

  static dynamic _decode(String body) {
    if (body.trim().isEmpty) return null;
    return jsonDecode(body);
  }

  static String? _message(dynamic data) {
    if (data is! Map) return null;
    final message = data['message'] ?? data['title'];
    if (message is String && message.trim().isNotEmpty) return message;
    final errors = data['errors'];
    if (errors is Map) {
      for (final value in errors.values) {
        if (value is List && value.isNotEmpty && value.first is String) {
          return value.first as String;
        }
      }
    }
    return null;
  }

  static Map<String, dynamic> _object(dynamic value) =>
      value is Map ? Map<String, dynamic>.from(value) : <String, dynamic>{};

  static Future<SessionUser> login({
    required String emailOrUsername,
    required String password,
  }) async {
    final response = _object(
      await request(
        '/Auth/login',
        method: 'POST',
        authenticated: false,
        body: {'identifier': emailOrUsername.trim(), 'matKhau': password},
      ),
    );
    final token = response['token'];
    if (token is! String || token.isEmpty) {
      throw const ApiException('Máy chủ không trả về phiên đăng nhập hợp lệ.');
    }
    _token = token;
    _sessionVersion++;
    _sessionExpired = false;
    return SessionUser.fromJson(_object(response['user']));
  }

  static Future<void> register({
    required String fullName,
    required String email,
    required String password,
  }) async {
    await request(
      '/Auth/register',
      method: 'POST',
      authenticated: false,
      body: {
        'tenDangNhap': fullName.trim(),
        'email': email.trim(),
        'matKhau': password,
      },
    );
  }

  static Future<SessionUser> me() async =>
      SessionUser.fromJson(_object(await request('/Auth/me')));
  static Future<SessionUser> updateAccountProfile({
    required String username,
    required String email,
    String? currentPassword,
    String? avatarDataUrl,
  }) async {
    final body = <String, dynamic>{'tenDangNhap': username, 'email': email};
    if (currentPassword != null) body['currentPassword'] = currentPassword;
    if (avatarDataUrl != null) body['avatarDataUrl'] = avatarDataUrl;
    return SessionUser.fromJson(
      _object(await request('/Account/profile', method: 'PUT', body: body)),
    );
  }

  static Future<void> changePassword({
    required String currentPassword,
    required String newPassword,
  }) async {
    await request(
      '/Account/password',
      method: 'PUT',
      body: {'currentPassword': currentPassword, 'newPassword': newPassword},
    );
  }

  static Future<List<Mood>> moods() async {
    final response = await request('/MoodMapping');
    if (response is! List) {
      throw const ApiException('Danh sách tâm trạng không hợp lệ.');
    }
    return response
        .map((item) => Mood.fromJson(_object(item)))
        .toList(growable: false);
  }

  static Future<RecommendationResult> recommend(
    int moodId,
    String? contentType, {
    String source = 'manual',
    String mode = 'hybrid',
    String? analysisId,
    int limit = 12,
    bool balanced = false,
  }) async {
    final result = RecommendationResult.fromJson(
      _object(
        await request(
          '/Recommendation',
          method: 'POST',
          body: {
            'tamTrangID': moodId,
            'loaiNoiDung': contentType,
            'limit': limit,
            'balanced': balanced,
            'source': source,
            'mode': mode,
            'analysisId': analysisId,
          },
        ),
      ),
    );
    historyRevision.value++;
    activityRevision.value++;
    return result;
  }

  static Future<MoodAnalysisResult> analyzeMood(
    String text, {
    required String language,
  }) async {
    final response = _object(
      await request(
        '/MoodAnalysis',
        method: 'POST',
        body: {'text': text, 'language': language},
      ),
    );
    return MoodAnalysisResult.fromApi(response);
  }

  static Future<List<ContentItem>> catalog(String type) async {
    final response = await request(type == 'Music' ? '/Music' : '/Movies');
    if (response is! List) {
      throw const ApiException('Danh mục nội dung không hợp lệ.');
    }
    return response
        .map((item) => ContentItem.fromJson(_object(item)))
        .toList(growable: false);
  }

  static Future<ContentItem> content(
    String type,
    String id,
  ) async => ContentItem.fromJson(
    _object(
      await request(
        '/${type == 'Music' ? 'Music' : 'Movies'}/${Uri.encodeComponent(id)}',
      ),
    ),
  );

  static Future<Map<String, dynamic>> musicPreview(String id) async =>
      _object(await request('/Music/${Uri.encodeComponent(id)}/preview'));

  static Future<PagedResult<MoodHistoryEntry>> history(int page) async =>
      PagedResult<MoodHistoryEntry>.fromJson(
        _object(await request('/History?page=$page')),
        MoodHistoryEntry.fromJson,
      );

  static Future<void> interaction(
    String contentId,
    String action,
    String eventId, {
    String? recommendationId,
  }) async {
    await request(
      '/Interactions',
      method: 'POST',
      body: {
        'contentId': contentId,
        'action': action,
        'eventId': eventId,
        'recommendationId': recommendationId,
      },
    );
    activityRevision.value++;
  }

  static Future<Map<String, dynamic>> interactions(int page) async =>
      _object(await request('/Interactions?page=$page'));

  static Future<void> rate(
    String contentId,
    int stars,
    String comment, {
    String? recommendationId,
  }) async {
    await request(
      '/History/ratings',
      method: 'POST',
      body: {
        'noiDungID': contentId,
        'soSao': stars,
        'recommendationId': recommendationId,
        if (comment.trim().isNotEmpty) 'nhanXet': comment.trim(),
      },
    );
    ratingsRevision.value++;
    activityRevision.value++;
  }

  static Future<PagedResult<PersonalRating>> ownRatings(int page) async =>
      PagedResult<PersonalRating>.fromJson(
        _object(await request('/History/ratings?page=$page')),
        PersonalRating.fromJson,
      );

  static Future<PagedResult<SavedEntry>> savedContents(
    int page, {
    String? type,
  }) async => PagedResult<SavedEntry>.fromJson(
    _object(
      await request(
        '/SavedContent?page=$page${type == null ? '' : '&type=${Uri.encodeQueryComponent(type)}'}',
      ),
    ),
    SavedEntry.fromJson,
  );

  static Future<bool> isSaved(String id) async {
    final data = _object(
      await request('/SavedContent/${Uri.encodeComponent(id)}'),
    );
    if (data['saved'] is! bool) {
      throw const ApiException('Chưa kiểm tra được mục đã lưu. Hãy thử lại.');
    }
    return data['saved'] as bool;
  }

  static Future<void> setSaved(String id, bool saved) async {
    await request(
      '/SavedContent/${Uri.encodeComponent(id)}',
      method: saved ? 'PUT' : 'DELETE',
    );
    savedRevision.value++;
  }

  static Future<WeeklySummary> weeklySummary() async {
    final data = _object(
      await request(
        '/Statistics?days=7&offsetMinutes=${DateTime.now().timeZoneOffset.inMinutes}',
      ),
    );
    try {
      return WeeklySummary.fromJson(data);
    } on FormatException {
      throw const ApiException('Chưa tải được tổng quan của bạn. Hãy thử lại.');
    }
  }
}
