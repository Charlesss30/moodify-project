import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:moodify_application/core/api_service.dart';
import 'package:moodify_application/widgets/rating_dialog.dart';

void main() {
  testWidgets(
    'Vietnamese IME composing text stays intact and submits as Unicode',
    (tester) async {
      final original = ApiService.clientFactory;
      addTearDown(() => ApiService.clientFactory = original);
      String? sent;
      ApiService.clientFactory = () => MockClient((r) async {
        sent = jsonDecode(utf8.decode(r.bodyBytes))['nhanXet'] as String;
        return http.Response('{}', 200);
      });
      await tester.pumpWidget(
        const MaterialApp(
          home: Scaffold(
            body: RatingDialog(contentId: 'song', title: 'Nhạc'),
          ),
        ),
      );
      final field = find.byType(TextField);
      expect(
        tester.widget<TextField>(field).hintLocales,
        contains(const Locale('vi', 'VN')),
      );
      await tester.showKeyboard(field);
      const text = 'Hôm nay tôi thấy rất vui, bài hát dễ thương!';
      tester.testTextInput.updateEditingValue(
        const TextEditingValue(
          text: text,
          selection: TextSelection.collapsed(offset: text.length),
          composing: TextRange(start: text.length - 7, end: text.length),
        ),
      );
      await tester.pump();
      final value = tester.widget<TextField>(field).controller!.value;
      expect(value.text, text);
      expect(value.composing, const TextRange(start: text.length - 7, end: text.length));
      tester.testTextInput.updateEditingValue(
        const TextEditingValue(
          text: text,
          selection: TextSelection.collapsed(offset: text.length),
        ),
      );
      await tester.pump();
      await tester.tap(find.text('Gửi đánh giá'));
      await tester.pumpAndSettle();
      expect(sent, text);
    },
  );
}
