import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:moodify_application/core/api_service.dart';
import 'package:moodify_application/models/session_user.dart';
import 'package:moodify_application/screens/welcome_tab.dart';
import 'package:moodify_application/widgets/rating_dialog.dart';

void main() {
  final originalFactory = ApiService.clientFactory;
  tearDown(() {
    ApiService.clientFactory = originalFactory;
    ApiService.logout();
  });
  const user = SessionUser(
    accountId: 1,
    fullName: 'Test',
    email: 'test@example.com',
    role: 'User',
  );
  http.Response response(Object body, [int status = 200]) => http.Response(
    jsonEncode(body),
    status,
    headers: {'content-type': 'application/json; charset=utf-8'},
  );
  testWidgets(
    'quiz uses server mood ID and one balanced request; rebuild preserves home',
    (tester) async {
      tester.view.physicalSize = const Size(900, 1800);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);
      var requests = 0, discoveries = 0;
      ApiService.clientFactory = () => MockClient((r) async {
        if (r.url.path.endsWith('/MoodMapping')) {
          return response([
            {'tamTrangID': 77, 'tenTamTrang': 'Happy'},
          ]);
        }
        expect(r.url.path, endsWith('/Recommendation'));
        final body = jsonDecode(r.body);
        expect(body['tamTrangID'], 77);
        expect(body['balanced'], true);
        expect(body['limit'], 6);
        requests++;
        return response({
          'mood': 'Happy',
          'items': [
            {
              'content': {
                'noiDungID': 's1',
                'tieuDe': 'Home song',
                'loaiNoiDung': 'Song',
              },
              'match': 80,
            },
            {
              'content': {
                'noiDungID': 'm1',
                'tieuDe': 'Home movie',
                'loaiNoiDung': 'Movie',
              },
              'match': 90,
            },
          ],
        });
      });
      Widget app() => MaterialApp(
        home: Scaffold(
          body: WelcomeTab(user: user, onDiscover: () => discoveries++),
        ),
      );
      await tester.pumpWidget(app());
      await tester.pumpAndSettle();
      expect(find.byType(AlertDialog), findsOneWidget);
      expect(requests, 0);
      await tester.tap(find.byType(ActionChip));
      await tester.pumpAndSettle();
      expect(requests, 1);
      expect(find.text('Home song'), findsOneWidget);
      expect(find.text('Home movie'), findsOneWidget);
      expect(find.text('Đánh giá'), findsNothing);
      await tester.pumpWidget(app());
      await tester.pumpAndSettle();
      expect(find.byType(AlertDialog), findsNothing);
      expect(requests, 1);
      await tester.tap(find.text('Khám phá chi tiết tâm trạng'));
      expect(discoveries, 1);
      expect(tester.takeException(), isNull);
    },
  );
  testWidgets('skipping quiz does not save history', (tester) async {
    var recommends = 0;
    ApiService.clientFactory = () => MockClient((r) async {
      if (r.url.path.endsWith('/Recommendation')) recommends++;
      return response([
        {'tamTrangID': 77, 'tenTamTrang': 'Happy'},
      ]);
    });
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: WelcomeTab(user: user, onDiscover: () {}),
        ),
      ),
    );
    await tester.pumpAndSettle();
    await tester.tap(find.text('Để sau'));
    await tester.pumpAndSettle();
    expect(recommends, 0);
    expect(find.byType(AlertDialog), findsNothing);
  });
  testWidgets(
    'rating keeps comment on API error and submits from detail dialog',
    (tester) async {
      var requests = 0;
      int? result;
      ApiService.clientFactory = () => MockClient((r) async {
        expect(r.url.path, endsWith('/History/ratings'));
        final body = jsonDecode(r.body);
        expect(body['noiDungID'], 'song1');
        expect(body['soSao'], 3);
        expect(body['nhanXet'], 'My review');
        requests++;
        return requests == 1
            ? response({'message': 'Retry review'}, 500)
            : response({});
      });
      await tester.pumpWidget(
        MaterialApp(
          home: Scaffold(
            body: Builder(
              builder: (context) => TextButton(
                onPressed: () async {
                  result = await showDialog<int>(
                    context: context,
                    barrierDismissible: false,
                    builder: (_) =>
                        const RatingDialog(contentId: 'song1', title: 'Song'),
                  );
                },
                child: const Text('Open detail rating'),
              ),
            ),
          ),
        ),
      );
      await tester.tap(find.text('Open detail rating'));
      await tester.pumpAndSettle();
      await tester.tap(find.byTooltip('3 sao'));
      await tester.enterText(find.byType(TextField), 'My review');
      await tester.tap(find.text('Gửi đánh giá'));
      await tester.pumpAndSettle();
      expect(find.byType(AlertDialog), findsOneWidget);
      expect(find.text('My review'), findsOneWidget);
      await tester.tap(find.text('Gửi đánh giá'));
      await tester.pumpAndSettle();
      expect(result, 3);
      expect(requests, 2);
      expect(find.byType(AlertDialog), findsNothing);
    },
  );
}
