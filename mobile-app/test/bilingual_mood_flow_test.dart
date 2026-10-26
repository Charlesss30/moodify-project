import 'dart:async';
import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:moodify_application/core/api_service.dart';
import 'package:moodify_application/models/mobile_models.dart';
import 'package:moodify_application/screens/mood_discovery_screen.dart';
import 'package:moodify_application/services/mood_analysis/mood_analyzer.dart';
import 'package:moodify_application/services/mood_analysis/mood_language.dart';

Map<String, dynamic> prediction(String language, {bool accepted = true}) => {
  'status': 'matched',
  'language': language,
  'modelVersion': language == 'vi'
      ? 'phobert_vsmec_v2'
      : 'roberta_va_regression',
  'valence': 0.734,
  'arousal': -0.219,
  'mood': {'tamTrangID': 5, 'tenTamTrang': 'Happy'},
  if (language == 'vi') ...{
    'emotion': 'Enjoyment',
    'confidence': accepted ? 0.94 : 0.42,
    'accepted': accepted,
    'emotionCandidates': [
      {'label': 'Enjoyment', 'score': accepted ? 0.94 : 0.42},
      {'label': 'Other', 'score': 0.03},
    ],
  } else
    'moodCandidates': [
      {'moodId': 5, 'moodName': 'Happy'},
    ],
};

http.Response jsonResponse(Object data, [int status = 200]) => http.Response(
  jsonEncode(data),
  status,
  headers: {'content-type': 'application/json; charset=utf-8'},
);

void installGateway({
  required Future<http.Response> Function(http.Request) analyze,
  void Function(Map<String, dynamic>)? recommend,
}) {
  ApiService.clientFactory = () => MockClient((request) async {
    if (request.url.path.endsWith('/MoodMapping')) {
      return jsonResponse([
        {
          'tamTrangID': 5,
          'tenTamTrang': 'Happy',
          'minValence': 0,
          'maxValence': 1,
          'minArousal': 0,
          'maxArousal': 1,
        },
      ]);
    }
    if (request.url.path.endsWith('/MoodAnalysis/valence-arousal')) {
      return analyze(request);
    }
    if (request.url.path.endsWith('/Recommendation')) {
      recommend?.call(
        jsonDecode(utf8.decode(request.bodyBytes)) as Map<String, dynamic>,
      );
      return jsonResponse({
        'mood': 'Happy',
        'algorithm': 'hybrid-feedback-v1',
        'items': [],
      });
    }
    // A call to the old GoEmotions endpoint is a routing regression.
    throw StateError('Unexpected endpoint: ${request.url.path}');
  });
}

Future<void> openDiscovery(WidgetTester tester) async {
  tester.view.physicalSize = const Size(390, 1100);
  tester.view.devicePixelRatio = 1;
  addTearDown(tester.view.resetPhysicalSize);
  addTearDown(tester.view.resetDevicePixelRatio);
  await tester.pumpWidget(const MaterialApp(home: MoodDiscoveryScreen()));
  await tester.pumpAndSettle();
}

Future<void> analyzeText(WidgetTester tester, String text) async {
  await tester.enterText(find.byType(TextField), text);
  await tester.pump();
  final button = find.text('Phân tích tâm trạng');
  await tester.ensureVisible(button);
  await tester.pumpAndSettle();
  await tester.tap(button);
  await tester.pumpAndSettle();
  final details = find.text('Thông tin phân tích');
  if (details.evaluate().isNotEmpty &&
      find.textContaining('Phiên bản:').evaluate().isEmpty) {
    await tester.ensureVisible(details);
    await tester.pumpAndSettle();
    await tester.tap(details);
    await tester.pumpAndSettle();
  }
}

void main() {
  tearDown(() {
    ApiService.clientFactory = http.Client.new;
    ApiService.logout();
  });

  test(
    'auto routing recognizes accents, unaccented Vietnamese and English',
    () {
      for (final text in [
        'Hôm nay tôi cảm thấy rất vui',
        'TÔI ĐANG BUỒN',
        'toi dang rat buon',
        'hom nay minh vui qua',
        '😭😭😭',
      ]) {
        expect(detectMoodLanguage(text), 'vi', reason: text);
      }
      for (final text in [
        'I feel happy today.',
        'I am terrified about tomorrow.',
        'Exhausted.',
        'My interview is scheduled for Monday; I have a really good feeling.',
      ]) {
        expect(detectMoodLanguage(text), 'en', reason: text);
      }
    },
  );

  test(
    'VA coordinates survive mood hydration; EN has no invented confidence',
    () {
      final result =
          MoodAnalysisResult.fromVaApi(
            prediction('en'),
            expectedLanguage: 'en',
          ).withMappedMood(
            const Mood(
              id: 5,
              name: 'Happy',
              minValence: 0,
              maxValence: 1,
              minArousal: 0,
              maxArousal: 1,
            ),
          );
      expect(result.valence, 0.734);
      expect(result.arousal, -0.219);
      expect(result.confidence, isNull);
      expect(result.language, 'en');
      expect(result.usesVaModel, isTrue);
    },
  );

  test('rejects wrong language and invalid VA values from gateway', () {
    expect(
      () => MoodAnalysisResult.fromVaApi(
        prediction('vi'),
        expectedLanguage: 'en',
      ),
      throwsFormatException,
    );
    for (final badValue in [double.nan, double.infinity, 1.01, '0.5']) {
      expect(
        () => MoodAnalysisResult.fromVaApi({
          ...prediction('en'),
          'valence': badValue,
        }, expectedLanguage: 'en'),
        throwsFormatException,
      );
    }
    expect(
      () => MoodAnalysisResult.fromVaApi({
        ...prediction('vi'),
        'accepted': 0.4,
      }, expectedLanguage: 'vi'),
      throwsFormatException,
    );
  });

  for (final language in ['vi', 'en']) {
    testWidgets('$language uses deployed gateway and forwards exact model VA', (
      tester,
    ) async {
      final analyses = <Map<String, dynamic>>[];
      final recommendations = <Map<String, dynamic>>[];
      installGateway(
        analyze: (request) async {
          expect(request.headers['content-type'], contains('utf-8'));
          final body = jsonDecode(
            utf8.decode(request.bodyBytes),
          ) as Map<String, dynamic>;
          analyses.add(body);
          return jsonResponse(prediction(body['language'] as String));
        },
        recommend: recommendations.add,
      );
      await openDiscovery(tester);
      final text = language == 'vi'
          ? 'Hôm nay tôi thấy rất vui.'
          : 'I feel happy today.';
      await analyzeText(tester, text);
      expect(analyses.single, {'text': text, 'language': language});
      final version = language == 'vi'
          ? 'phobert_vsmec_v2'
          : 'roberta_va_regression';
      expect(find.text('Phiên bản: $version'), findsOneWidget);
      final confirm = find.text('Xác nhận và nhận gợi ý');
      await tester.ensureVisible(confirm);
      await tester.pumpAndSettle();
      await tester.tap(confirm);
      await tester.pumpAndSettle();
      expect(recommendations.single['source'], 'ai_va');
      expect(recommendations.single['valence'], 0.734);
      expect(recommendations.single['arousal'], -0.219);
      expect(recommendations.single['modelVersion'], version);
      expect(recommendations.single['analysisId'], isNull);
      expect(tester.takeException(), isNull);
    });
  }

  testWidgets(
    'low-confidence Vietnamese requires manual choice before recommending',
    (tester) async {
      final recommendations = <Map<String, dynamic>>[];
      installGateway(
        analyze: (_) async => jsonResponse(prediction('vi', accepted: false)),
        recommend: recommendations.add,
      );
      await openDiscovery(tester);
      await analyzeText(tester, 'Tôi vừa vui vừa lo lắng.');
      expect(find.text('Chưa đủ tin cậy'), findsOneWidget);
      final confirm = find.widgetWithText(
        FilledButton,
        'Xác nhận và nhận gợi ý',
      );
      expect(tester.widget<FilledButton>(confirm).onPressed, isNull);
      final mood = find.byType(DropdownButtonFormField<int>);
      await tester.ensureVisible(mood);
      await tester.pumpAndSettle();
      await tester.tap(mood);
      await tester.pumpAndSettle();
      await tester.tap(find.text('😊  Vui vẻ').last);
      await tester.pumpAndSettle();
      await tester.ensureVisible(confirm);
      await tester.pumpAndSettle();
      await tester.tap(confirm);
      await tester.pumpAndSettle();
      expect(recommendations.single['source'], 'manual');
      expect(recommendations.single.containsKey('valence'), isFalse);
      expect(recommendations.single.containsKey('modelVersion'), isFalse);
      expect(tester.takeException(), isNull);
    },
  );

  testWidgets(
    'model hint follows text automatically without a language picker',
    (tester) async {
      final sentLanguages = <String>[];
      installGateway(
        analyze: (request) async {
          final body = jsonDecode(
            utf8.decode(request.bodyBytes),
          ) as Map<String, dynamic>;
          final language = body['language'] as String;
          sentLanguages.add(language);
          return jsonResponse(prediction(language));
        },
      );
      await openDiscovery(tester);
      expect(find.byType(DropdownButtonFormField<int>), findsOneWidget);
      expect(find.text('Ngôn ngữ mô tả'), findsNothing);
      expect(find.text('Tự nhận diện Việt / Anh'), findsNothing);
      await analyzeText(tester, 'Tôi thấy vui');
      expect(find.text('Model: PhoBERT · Tiếng Việt'), findsOneWidget);
      expect(sentLanguages, ['vi']);

      await tester.enterText(find.byType(TextField), 'I feel calm today.');
      await tester.pumpAndSettle();
      expect(find.text('Model: RoBERTa VA · English'), findsOneWidget);
      expect(find.text('Phiên bản: phobert_vsmec_v2'), findsNothing);
      expect(
        tester
            .widget<FilledButton>(
              find.widgetWithText(FilledButton, 'Xác nhận và nhận gợi ý'),
            )
            .onPressed,
        isNull,
      );
      await analyzeText(tester, 'I feel calm today.');
      expect(sentLanguages, ['vi', 'en']);
      expect(find.text('Phiên bản: roberta_va_regression'), findsOneWidget);
      expect(tester.takeException(), isNull);
    },
  );

  testWidgets(
    'late Vietnamese result cannot replace a newer English analysis',
    (tester) async {
      final delayed = Completer<http.Response>();
      installGateway(
        analyze: (request) async {
          final body = jsonDecode(
            utf8.decode(request.bodyBytes),
          ) as Map<String, dynamic>;
          if (body['language'] == 'vi') return delayed.future;
          return jsonResponse(prediction('en'));
        },
      );
      await openDiscovery(tester);
      await tester.enterText(find.byType(TextField), 'Tôi thấy vui');
      await tester.pump();
      await tester.ensureVisible(find.text('Phân tích tâm trạng'));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Phân tích tâm trạng'));
      await tester.pump();
      await analyzeText(tester, 'I feel calm today.');
      expect(find.text('Phiên bản: roberta_va_regression'), findsOneWidget);
      delayed.complete(jsonResponse(prediction('vi')));
      await tester.pumpAndSettle();
      expect(find.text('Phiên bản: phobert_vsmec_v2'), findsNothing);
      expect(find.text('Phiên bản: roberta_va_regression'), findsOneWidget);
      expect(tester.takeException(), isNull);
    },
  );

  testWidgets(
    'unavailable model shows error and preserves manual mood selection',
    (tester) async {
      installGateway(
        analyze: (_) async => jsonResponse({
          'message': 'Vietnamese emotion model is unavailable.',
        }, 503),
      );
      await openDiscovery(tester);
      await analyzeText(tester, 'Tôi thấy buồn');
      expect(
        find.text(
          'Model tiếng Việt chưa sẵn sàng. Hãy thử lại sau hoặc chọn tâm trạng thủ công.',
        ),
        findsOneWidget,
      );
      expect(find.byType(DropdownButtonFormField<int>), findsOneWidget);
      expect(find.textContaining('Phiên bản:'), findsNothing);
      expect(tester.takeException(), isNull);
    },
  );
}
