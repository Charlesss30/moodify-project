import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:moodify_application/core/api_service.dart';
import 'package:moodify_application/screens/mood_discovery_screen.dart';

void main() {
  testWidgets(
    'AI filter change requires a fresh decision and never replays old analysis',
    (tester) async {
      final factory = ApiService.clientFactory;
      addTearDown(() {
        ApiService.clientFactory = factory;
        ApiService.logout();
      });
      tester.view.physicalSize = const Size(1200, 2200);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);
      final requests = <Map<String, dynamic>>[];
      ApiService.clientFactory = () => MockClient((r) async {
        Object data;
        if (r.url.path.endsWith('/MoodMapping')) {
          data = [
            {
              'tamTrangID': 5,
              'tenTamTrang': 'Happy',
              'minValence': 0,
              'maxValence': 1,
              'minArousal': 0,
              'maxArousal': 1,
            },
          ];
        } else if (r.url.path.endsWith('/MoodAnalysis/valence-arousal')) {
          data = {
            'status': 'matched',
            'language': 'en',
            'modelVersion': 'roberta_va_test',
            'valence': 0.73,
            'arousal': 0.24,
            'mood': {'tamTrangID': 5, 'tenTamTrang': 'Happy'},
            'moodCandidates': [],
          };
        } else {
          requests.add(jsonDecode(r.body) as Map<String, dynamic>);
          data = {'mood': 'Happy', 'items': []};
        }
        return http.Response(
          jsonEncode(data),
          200,
          headers: {'content-type': 'application/json; charset=utf-8'},
        );
      });
      await tester.pumpWidget(const MaterialApp(home: MoodDiscoveryScreen()));
      await tester.pumpAndSettle();
      expect(find.byType(SwitchListTile), findsNothing);
      await tester.enterText(
        find.byType(TextField).first,
        'I feel happy today.',
      );
      await tester.tap(find.text('Phân tích tâm trạng'));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Xác nhận và nhận gợi ý'));
      await tester.pumpAndSettle();
      expect(requests.single['analysisId'], isNull);
      expect(requests.single['source'], 'ai_va');
      expect(requests.single['modelVersion'], 'roberta_va_test');
      expect(requests.single['valence'], 0.73);
      expect(requests.single['arousal'], 0.24);
      await tester.tap(find.text('Nhạc'));
      await tester.pumpAndSettle();
      expect(find.textContaining('Đã đổi loại nội dung'), findsOneWidget);
      final button = tester.widget<FilledButton>(
        find.widgetWithText(FilledButton, 'Xác nhận và nhận gợi ý'),
      );
      expect(button.onPressed, isNull);
      await tester.tap(find.byType(DropdownButtonFormField<int>));
      await tester.pumpAndSettle();
      await tester.tap(find.text('😊  Vui vẻ').last);
      await tester.pumpAndSettle();
      await tester.tap(find.text('Xác nhận và nhận gợi ý'));
      await tester.pumpAndSettle();
      expect(requests.last['analysisId'], isNull);
      expect(requests.last['source'], 'manual');
      expect(requests.last.containsKey('modelVersion'), isFalse);
      expect(requests.last.containsKey('valence'), isFalse);
      expect(requests.last['loaiNoiDung'], 'Music');
      expect(tester.takeException(), isNull);
    },
  );
}
