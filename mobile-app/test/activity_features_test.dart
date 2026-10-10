import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:moodify_application/core/api_service.dart';
import 'package:moodify_application/screens/activity_history_screen.dart';
import 'package:moodify_application/screens/content_detail_screen.dart';

void main() {
  final original = ApiService.clientFactory;
  tearDown(() {
    ApiService.clientFactory = original;
    ApiService.logout();
  });
  http.Response json(Object body, [int status = 200]) => http.Response(
    jsonEncode(body),
    status,
    headers: {'content-type': 'application/json; charset=utf-8'},
  );
  testWidgets('activity history loads next page and preserves titles', (
    tester,
  ) async {
    final pages = <int>[];
    ApiService.clientFactory = () => MockClient((r) async {
      final page = int.parse(r.url.queryParameters['page']!);
      pages.add(page);
      return json({
        'items': [
          {
            'contentId': 'm$page',
            'title': 'Movie $page',
            'contentType': 'Movie',
            'action': 'detail_open',
            'createdAt': '2026-10-07T08:00:00Z',
          },
        ],
        'page': page,
        'pageSize': 1,
        'total': 2,
      });
    });
    await tester.pumpWidget(const MaterialApp(home: ActivityHistoryScreen()));
    await tester.pumpAndSettle();
    expect(find.text('Movie 1'), findsOneWidget);
    await tester.tap(find.text('Tải thêm'));
    await tester.pumpAndSettle();
    expect(find.text('Movie 2'), findsOneWidget);
    expect(pages, [1, 2]);
    expect(find.text('Tải thêm'), findsNothing);
  });
  testWidgets(
    'failed telemetry leaves detail and trailer available and links session',
    (tester) async {
      final events = <Map<String, dynamic>>[];
      ApiService.clientFactory = () => MockClient((r) async {
        if (r.url.path.endsWith('/Interactions')) {
          events.add(jsonDecode(r.body));
          return json({'message': 'Unavailable'}, 503);
        }
        return json({
          'noiDungID': 'm1',
          'tieuDe': 'Film detail',
          'loaiNoiDung': 'Movie',
          'phim': {
            'director': 'Director fixture',
            'trailerUrl': 'https://www.youtube.com/watch?v=12345678901',
          },
        });
      });
      await tester.pumpWidget(
        const MaterialApp(
          home: ContentDetailScreen(
            contentId: 'm1',
            type: 'Movie',
            recommendationId: 'session1',
          ),
        ),
      );
      await tester.pumpAndSettle();
      await tester.scrollUntilVisible(find.text('Film detail'), 200);
      expect(find.text('Film detail'), findsOneWidget);
      await tester.scrollUntilVisible(find.text('Mở trên YouTube'), 200);
      expect(find.text('Mở trên YouTube'), findsOneWidget);
      expect(events.single['action'], 'detail_open');
      expect(events.single['recommendationId'], 'session1');
      expect(tester.takeException(), isNull);
    },
  );
  test(
    'mobile requests hybrid while preserving explicit mood fallback',
    () async {
      final modes = <String>[];
      ApiService.clientFactory = () => MockClient((r) async {
        modes.add(jsonDecode(r.body)['mode']);
        return json({'items': []});
      });
      await ApiService.recommend(1, null);
      await ApiService.recommend(1, null, mode: 'mood');
      expect(modes, ['hybrid', 'mood']);
    },
  );
}
