import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:moodify_application/core/api_service.dart';
import 'package:moodify_application/screens/statistics_screen.dart';

void main() {
  tearDown(() {
    ApiService.clientFactory = http.Client.new;
    ApiService.logout();
  });
  testWidgets(
    'Statistics uses aggregate endpoint, switches date range and displays empty days',
    (tester) async {
      final days = <String?>[];
      ApiService.clientFactory = () => MockClient((request) async {
        expect(request.url.path, '/api/Statistics');
        days.add(request.url.queryParameters['days']);
        return http.Response(
          jsonEncode({
            'summary': {
              'moodEntries': 61,
              'interactions': 0,
              'activeDays': 0,
              'ratings': 0,
              'averageStars': null,
            },
            'previous': {'moodEntries': 4, 'interactions': 2},
            'moods': [
              {'name': 'Happy', 'count': 61},
            ],
            'trend': [
              {
                'date': '2026-10-15',
                'valence': null,
                'arousal': null,
                'interactions': 0,
              },
              {
                'date': '2026-10-16',
                'valence': 0.4,
                'arousal': 0.2,
                'interactions': 0,
              },
            ],
            'contentTypes': [],
            'genres': [],
            'hours': [],
          }),
          200,
          headers: {'content-type': 'application/json; charset=utf-8'},
        );
      });
      await tester.pumpWidget(const MaterialApp(home: StatisticsScreen()));
      await tester.pumpAndSettle();
      expect(find.textContaining('61 lượt ghi nhận'), findsOneWidget);
      expect(find.textContaining('Kỳ trước: 4'), findsOneWidget);
      await tester.tap(find.text('7 ngày'));
      await tester.pumpAndSettle();
      expect(days, ['30', '7']);
      expect(tester.takeException(), isNull);
    },
  );
  testWidgets('Statistics API error provides retry, not fake zero totals', (
    tester,
  ) async {
    var calls = 0;
    ApiService.clientFactory = () => MockClient((_) async {
      calls++;
      return http.Response('{"message":"Unavailable"}', 503);
    });
    await tester.pumpWidget(const MaterialApp(home: StatisticsScreen()));
    await tester.pumpAndSettle();
    expect(find.textContaining('Thử lại'), findsOneWidget);
    expect(find.textContaining('0 lượt ghi nhận'), findsNothing);
    await tester.tap(find.textContaining('Thử lại'));
    await tester.pumpAndSettle();
    expect(calls, 2);
  });
}
