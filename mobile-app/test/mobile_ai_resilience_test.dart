import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:moodify_application/core/api_service.dart';
import 'package:moodify_application/services/mood_analysis/mood_analyzer.dart';
import 'package:moodify_application/services/mood_analysis/mood_language.dart';

import 'bilingual_mood_flow_test.dart' as flow;

class TrackedClient extends http.BaseClient {
  TrackedClient(this.inner);
  final http.Client inner;
  bool closed = false;
  String? path;
  @override
  Future<http.StreamedResponse> send(http.BaseRequest request) {
    path = request.url.path;
    return inner.send(request);
  }

  @override
  void close() {
    closed = true;
    inner.close();
  }
}

Future<void> tap(
  WidgetTester tester,
  String label, {
  bool settle = true,
}) async {
  await tester.ensureVisible(find.text(label));
  await tester.pump();
  await tester.tap(find.text(label));
  await tester.pump();
  if (settle) await tester.pumpAndSettle();
}

void main() {
  tearDown(() {
    ApiService.clientFactory = http.Client.new;
    ApiService.requestTimeout = const Duration(seconds: 20);
    ApiService.logout();
  });
  final routes = jsonDecode(
    File('../tests/ai-cases/language-routing-cases.json').readAsStringSync(),
  ) as List;
  for (final item in routes) {
    test('routing hint agrees with backend fixture: ${item['text']}', () {
      expect(detectMoodLanguage(item['text'] as String), item['language']);
    });
  }
  test(
    'cancelled analysis closes the HTTP client and ignores a late response',
    () async {
      final pending = Completer<http.Response>();
      final client = TrackedClient(MockClient((_) => pending.future));
      ApiService.clientFactory = () => client;
      final cancellation = ApiRequestCancellation();
      final result = ApiService.analyzeMood(
        'Tôi đang buồn',
        cancellation: cancellation,
      );
      final assertion = expectLater(
        result,
        throwsA(isA<ApiRequestCancelled>()),
      );
      cancellation.cancel();
      await assertion;
      expect(client.closed, isTrue);
      pending.complete(flow.jsonResponse(flow.prediction('vi')));
      await Future<void>.delayed(Duration.zero);
    },
  );
  test(
    'timed out analysis closes the HTTP client and can be retried',
    () async {
      final pending = Completer<http.Response>();
      final client = TrackedClient(MockClient((_) => pending.future));
      ApiService.clientFactory = () => client;
      ApiService.requestTimeout = const Duration(milliseconds: 10);
      await expectLater(
        ApiService.analyzeMood('Tôi đang buồn'),
        throwsA(isA<ApiException>()),
      );
      expect(client.closed, isTrue);
      pending.complete(flow.jsonResponse(flow.prediction('vi')));
      ApiService.clientFactory = () =>
          MockClient((_) async => flow.jsonResponse(flow.prediction('en')));
      expect(
        (await ApiService.analyzeMood('I feel happy')).status,
        MoodAnalysisStatus.matched,
      );
    },
  );
  testWidgets(
    'content filter before confirmation keeps the same model decision',
    (tester) async {
      var calls = 0;
      final requests = <Map<String, dynamic>>[];
      flow.installGateway(
        analyze: (_) async {
          calls++;
          return flow.jsonResponse(flow.prediction('en'));
        },
        recommend: requests.add,
      );
      await flow.openDiscovery(tester);
      await flow.analyzeText(tester, 'I feel happy today.');
      await tap(tester, 'Nhạc');
      expect(find.text('Tâm trạng gợi ý'), findsOneWidget);
      await tap(tester, 'Xác nhận và nhận gợi ý');
      expect(calls, 1);
      expect(requests.single['source'], 'ai_va');
      expect(requests.single['loaiNoiDung'], 'Music');
      expect(tester.takeException(), isNull);
    },
  );
  testWidgets(
    'expired receipt requests fresh analysis instead of sending it repeatedly',
    (tester) async {
      var calls = 0;
      final requests = <Map<String, dynamic>>[];
      flow.installGateway(
        analyze: (_) async {
          calls++;
          final data = flow.prediction('en');
          if (calls == 1) data['expiresAt'] = '2000-01-01T00:00:00Z';
          return flow.jsonResponse(data);
        },
        recommend: requests.add,
      );
      await flow.openDiscovery(tester);
      await flow.analyzeText(tester, 'I feel happy today.');
      await tap(tester, 'Xác nhận và nhận gợi ý');
      expect(requests, isEmpty);
      expect(
        find.textContaining('Kết quả phân tích đã hết hiệu lực'),
        findsOneWidget,
      );
      await tap(tester, 'Thử lại');
      expect(calls, 2);
      await tap(tester, 'Xác nhận và nhận gợi ý');
      expect(requests.single['source'], 'ai_va');
      expect(tester.takeException(), isNull);
    },
  );
  testWidgets('server-side receipt change offers a fresh analysis retry', (
    tester,
  ) async {
    var analyses = 0;
    var recommendations = 0;
    flow.installGateway(
      analyze: (_) async {
        analyses++;
        return flow.jsonResponse(flow.prediction('en'));
      },
      recommendAsync: (_) async {
        recommendations++;
        return recommendations == 1
            ? flow.jsonResponse({'message': 'analysis no longer valid'}, 409)
            : flow.jsonResponse({'mood': 'Happy', 'items': []});
      },
    );
    await flow.openDiscovery(tester);
    await flow.analyzeText(tester, 'I feel happy today.');
    await tap(tester, 'Xác nhận và nhận gợi ý');
    expect(
      find.textContaining('Kết quả phân tích đã hết hiệu lực'),
      findsOneWidget,
    );
    await tap(tester, 'Thử lại');
    expect(analyses, 2);
    expect(recommendations, 1);
    await tap(tester, 'Xác nhận và nhận gợi ý');
    expect(recommendations, 2);
    expect(tester.takeException(), isNull);
  });
  testWidgets(
    'late recommendations cannot replace an edited description and newer analysis',
    (tester) async {
      final delayed = Completer<http.Response>();
      var calls = 0;
      flow.installGateway(
        analyze: (_) async => flow.jsonResponse(flow.prediction('en')),
        recommendAsync: (_) async {
          calls++;
          return calls == 1
              ? delayed.future
              : flow.jsonResponse({'mood': 'Happy', 'items': []});
        },
      );
      await flow.openDiscovery(tester);
      await flow.analyzeText(tester, 'I feel happy today.');
      await tap(tester, 'Xác nhận và nhận gợi ý', settle: false);
      await flow.analyzeText(tester, 'I feel happy about my new day.');
      delayed.complete(flow.jsonResponse({'mood': 'Sad', 'items': []}));
      await tester.pumpAndSettle();
      expect(find.text('Gợi ý cho buồn'), findsNothing);
      expect(find.text('Tâm trạng gợi ý'), findsOneWidget);
      await tap(tester, 'Xác nhận và nhận gợi ý');
      expect(calls, 2);
      expect(find.text('Gợi ý cho vui vẻ'), findsOneWidget);
      expect(tester.takeException(), isNull);
    },
  );
  testWidgets(
    'manual selection cancels pending analysis and retains manual provenance',
    (tester) async {
      final delayed = Completer<http.Response>();
      final requests = <Map<String, dynamic>>[];
      flow.installGateway(
        analyze: (_) => delayed.future,
        recommend: requests.add,
      );
      await flow.openDiscovery(tester);
      await tester.enterText(find.byType(TextField), 'Tôi đang vui');
      await tap(tester, 'Phân tích tâm trạng', settle: false);
      await tester.ensureVisible(find.byType(DropdownButtonFormField<int>));
      await tester.tap(find.byType(DropdownButtonFormField<int>));
      await tester.pumpAndSettle();
      await tester.tap(find.text('😊  Vui vẻ').last);
      await tester.pumpAndSettle();
      delayed.complete(flow.jsonResponse(flow.prediction('vi')));
      await tester.pumpAndSettle();
      expect(find.text('Tâm trạng gợi ý'), findsNothing);
      await tap(tester, 'Xác nhận và nhận gợi ý');
      expect(requests.single['source'], 'manual');
      expect(requests.single['analysisId'], isNull);
      expect(tester.takeException(), isNull);
    },
  );
}
