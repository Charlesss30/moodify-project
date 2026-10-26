import 'dart:async';
import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:moodify_application/core/api_service.dart';
import 'package:moodify_application/models/mobile_models.dart';
import 'package:moodify_application/screens/discovery_content_screen.dart';
import 'package:moodify_application/screens/statistics_screen.dart';
import 'package:moodify_application/widgets/daily_trend_chart.dart';

http.Response response(Object body) => http.Response(
  jsonEncode(body),
  200,
  headers: {'content-type': 'application/json; charset=utf-8'},
);

Map<String, Object?> statistics(int? year) => {
  'days': year == null ? 30 : 366,
  'year': year,
  'summary': {
    'moodEntries': 3,
    'interactions': 2,
    'activeDays': 1,
    'ratings': 0,
    'averageStars': null,
  },
  'previous': {'moodEntries': 1, 'interactions': 0},
  'trend': [
    {
      'date': '${year ?? 2026}-01-01',
      'moodCount': 1,
      'interactions': 1,
      'valence': .5,
      'arousal': .1,
    },
    {
      'date': '${year ?? 2026}-02-01',
      'moodCount': 2,
      'interactions': 1,
      'valence': .2,
      'arousal': -.1,
    },
  ],
  'moods': [],
  'genres': [],
  'contentTypes': [],
  'hours': [],
};

void main() {
  tearDown(() {
    ApiService.clientFactory = http.Client.new;
    ApiService.logout();
  });

  test(
    'movie cast, release year and runtime stay distinct from music seconds',
    () {
      final movie = ContentItem.fromJson({
        'noiDungID': 'm',
        'tieuDe': 'Movie',
        'loaiNoiDung': 'Movie',
        'phim': {
          'releaseYear': 2020,
          'runtimeMinutes': 125,
          'cast': [' Actor A ', 'Actor B', 'Actor A'],
        },
      });
      expect(movie.releaseYear, 2020);
      expect(movie.runtimeMinutes, 125);
      expect(movie.durationSeconds, isNull);
      expect(movie.cast, ['Actor A', 'Actor B']);
    },
  );

  testWidgets(
    'calendar year selector requests year API and shows monthly chart',
    (tester) async {
      tester.view.physicalSize = const Size(390, 1100);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);
      final requests = <Uri>[];
      ApiService.clientFactory = () => MockClient((request) async {
        requests.add(request.url);
        return response(
          statistics(int.tryParse(request.url.queryParameters['year'] ?? '')),
        );
      });
      await tester.pumpWidget(const MaterialApp(home: StatisticsScreen()));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Theo năm'));
      await tester.pumpAndSettle();
      expect(requests.last.queryParameters['year'], '${DateTime.now().year}');
      expect(requests.last.queryParameters.containsKey('days'), isFalse);
      await tester.tap(find.byType(DropdownButtonFormField<int>));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Năm 2024').last);
      await tester.pumpAndSettle();
      expect(requests.last.queryParameters['year'], '2024');
      await tester.scrollUntilVisible(find.byType(DailyTrendChart), 200);
      await tester.pumpAndSettle();
      expect(
        tester.widget<DailyTrendChart>(find.byType(DailyTrendChart)).monthly,
        isTrue,
      );
      expect(find.text('Tháng 2/2024'), findsOneWidget);
      expect(tester.takeException(), isNull);
    },
  );

  testWidgets('monthly chart labels empty months without inventing zero mood', (
    tester,
  ) async {
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: DailyTrendChart(
            monthly: true,
            rows: const [
              {
                'date': '2024-01-01',
                'moodCount': 0,
                'interactions': 0,
                'valence': null,
                'arousal': null,
              },
              {
                'date': '2024-02-01',
                'moodCount': 0,
                'interactions': 0,
                'valence': null,
                'arousal': null,
              },
            ],
          ),
        ),
      ),
    );
    expect(find.text('Tháng 2/2024'), findsOneWidget);
    expect(
      find.text('Chưa ghi nhận tâm trạng trong tháng này.'),
      findsOneWidget,
    );
    await tester.tap(find.byTooltip('Tháng trước'));
    await tester.pump();
    expect(find.text('Tháng 1/2024'), findsOneWidget);
    expect(tester.takeException(), isNull);
  });

  testWidgets('discovery explains empty popularity and filters updated music', (
    tester,
  ) async {
    final requests = <Uri>[];
    ApiService.clientFactory = () => MockClient((request) async {
      requests.add(request.url);
      return response({
        'items': request.url.queryParameters['mode'] == 'popular'
            ? []
            : [
                {
                  'content': {
                    'noiDungID': 's',
                    'tieuDe': 'New song',
                    'loaiNoiDung': 'Song',
                  },
                  'updatedAt': '2026-10-25T00:00:00Z',
                },
              ],
      });
    });
    await tester.pumpWidget(const MaterialApp(home: DiscoveryContentScreen()));
    await tester.pumpAndSettle();
    expect(find.textContaining('Chưa có lượt quan tâm'), findsOneWidget);
    await tester.tap(find.text('Vừa cập nhật'));
    await tester.pumpAndSettle();
    expect(find.text('New song'), findsOneWidget);
    await tester.tap(find.text('Nhạc'));
    await tester.pumpAndSettle();
    expect(requests.last.queryParameters['type'], 'Music');
    expect(requests.last.queryParameters['mode'], 'updated');
    expect(tester.takeException(), isNull);
  });

  testWidgets('late popularity response cannot replace updated results', (
    tester,
  ) async {
    final delayed = Completer<http.Response>();
    ApiService.clientFactory = () => MockClient((request) async {
      if (request.url.queryParameters['mode'] == 'popular') {
        return delayed.future;
      }
      return response({
        'items': [
          {
            'content': {
              'noiDungID': 'fresh',
              'tieuDe': 'Fresh movie',
              'loaiNoiDung': 'Movie',
            },
          },
        ],
      });
    });
    await tester.pumpWidget(const MaterialApp(home: DiscoveryContentScreen()));
    await tester.pump();
    await tester.tap(find.text('Vừa cập nhật'));
    await tester.pumpAndSettle();
    delayed.complete(
      response({
        'items': [
          {
            'content': {
              'noiDungID': 'old',
              'tieuDe': 'Old popularity',
              'loaiNoiDung': 'Movie',
            },
          },
        ],
      }),
    );
    await tester.pumpAndSettle();
    expect(find.text('Fresh movie'), findsOneWidget);
    expect(find.text('Old popularity'), findsNothing);
    expect(tester.takeException(), isNull);
  });
}
