import 'dart:async';
import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:moodify_application/core/api_service.dart';
import 'package:moodify_application/core/navigation.dart';
import 'package:moodify_application/models/session_user.dart';
import 'package:moodify_application/screens/app_shell.dart';
import 'package:moodify_application/screens/content_detail_screen.dart';
import 'package:moodify_application/screens/my_ratings_screen.dart';
import 'package:moodify_application/screens/saved_content_screen.dart';
import 'package:moodify_application/widgets/profile_weekly_overview.dart';

http.Response response(Object? data, [int status = 200]) => http.Response(
  data == null ? '' : jsonEncode(data),
  status,
  headers: {'content-type': 'application/json; charset=utf-8'},
);
Map<String, dynamic> content(
  String id,
  String title, [
  String type = 'Movie',
]) => {'noiDungID': id, 'tieuDe': title, 'loaiNoiDung': type};
Map<String, dynamic> entry(
  String id,
  String title, {
  String type = 'Movie',
  bool available = true,
}) => {
  'contentId': id,
  'createdAt': '2026-10-24T04:00:00Z',
  'available': available,
  'content': content(id, title, type),
};
Map<String, dynamic> weekly([int count = 3]) => {
  'summary': {'moodDays': 2, 'openedContents': count, 'ratings': 4},
};
const user = SessionUser(
  accountId: 1,
  fullName: 'An',
  email: 'an@test.invalid',
  role: 'User',
);
void phone(WidgetTester tester, [double width = 390]) {
  tester.view.physicalSize = Size(width, 844);
  tester.view.devicePixelRatio = 1;
  addTearDown(tester.view.resetPhysicalSize);
  addTearDown(tester.view.resetDevicePixelRatio);
}

void main() {
  tearDown(() {
    ApiService.clientFactory = http.Client.new;
    ApiService.logout();
  });

  testWidgets(
    'detail saves only after success, blocks repeats, retries and restores server state',
    (tester) async {
      phone(tester);
      var saved = false, writes = 0;
      final first = Completer<http.Response>();
      final revision = ApiService.savedRevision.value;
      ApiService.clientFactory = () => MockClient((r) async {
        if (r.url.path.startsWith('/api/SavedContent/')) {
          if (r.method == 'GET') return response({'saved': saved});
          writes++;
          if (writes == 1) return first.future;
          saved = r.method == 'PUT';
          return response(
            r.method == 'DELETE' ? null : {'saved': saved},
            r.method == 'DELETE' ? 204 : 200,
          );
        }
        if (r.url.path.endsWith('/Interactions')) return response({});
        if (r.url.path.endsWith('/Movies/m1')) {
          return response(content('m1', 'Test movie'));
        }
        throw StateError('Unexpected request ${r.method} ${r.url}');
      });
      Widget detail(Key key) => MaterialApp(
        home: ContentDetailScreen(key: key, contentId: 'm1', type: 'Movie'),
      );
      await tester.pumpWidget(detail(const ValueKey(1)));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Lưu để xem sau'));
      await tester.pump();
      expect(writes, 1);
      expect(
        tester
            .widget<OutlinedButton>(
              find.widgetWithText(OutlinedButton, 'Lưu để xem sau'),
            )
            .onPressed,
        isNull,
      );
      expect(ApiService.savedRevision.value, revision);
      first.complete(response({'message': 'Chưa lưu được, thử lại.'}, 503));
      await tester.pumpAndSettle();
      expect(find.text('Chưa lưu được, thử lại.'), findsOneWidget);
      expect(find.text('Đã lưu'), findsNothing);
      await tester.tap(find.text('Lưu để xem sau'));
      await tester.pumpAndSettle();
      expect(find.text('Đã lưu'), findsOneWidget);
      expect(ApiService.savedRevision.value, revision + 1);
      await tester.pumpWidget(detail(const ValueKey(2)));
      await tester.pumpAndSettle();
      expect(find.text('Đã lưu'), findsOneWidget);
      await tester.tap(find.text('Đã lưu'));
      await tester.pumpAndSettle();
      expect(saved, isFalse);
      expect(find.text('Lưu để xem sau'), findsOneWidget);
      expect(ApiService.savedRevision.value, revision + 2);
      expect(tester.takeException(), isNull);
    },
  );

  testWidgets(
    'saved filters ignore stale responses and failed removal keeps the item',
    (tester) async {
      phone(tester);
      final movie = Completer<http.Response>();
      var deletes = 0, removed = false;
      final reads = <String?>[];
      ApiService.clientFactory = () => MockClient((r) async {
        if (r.method == 'DELETE') {
          deletes++;
          if (deletes == 1) {
            return response({'message': 'Chưa bỏ lưu được.'}, 503);
          }
          removed = true;
          return response(null, 204);
        }
        final type = r.url.queryParameters['type'];
        reads.add(type);
        if (type == 'Movie') return movie.future;
        return response({
          'page': 1,
          'pageSize': 12,
          'total': removed ? 0 : 1,
          'items': removed ? [] : [entry('s1', 'Bài hát đã lưu', type: 'Song')],
        });
      });
      await tester.pumpWidget(const MaterialApp(home: SavedContentScreen()));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Phim'));
      await tester.pump();
      await tester.tap(find.text('Nhạc'));
      await tester.pumpAndSettle();
      expect(find.text('Bài hát đã lưu'), findsOneWidget);
      movie.complete(
        response({
          'page': 1,
          'total': 1,
          'items': [entry('m1', 'Phim phản hồi chậm')],
        }),
      );
      await tester.pumpAndSettle();
      expect(find.text('Phim phản hồi chậm'), findsNothing);
      await tester.tap(find.byTooltip('Bỏ lưu Bài hát đã lưu'));
      await tester.pumpAndSettle();
      expect(find.text('Chưa bỏ lưu được.'), findsOneWidget);
      expect(find.text('Bài hát đã lưu'), findsOneWidget);
      await tester.tap(find.byTooltip('Bỏ lưu Bài hát đã lưu'));
      await tester.pumpAndSettle();
      expect(find.text('Bài hát đã lưu'), findsNothing);
      expect(find.text('Chưa có nội dung đã lưu'), findsOneWidget);
      expect(reads, [null, 'Movie', 'Music', 'Music']);
      expect(tester.takeException(), isNull);
    },
  );

  testWidgets(
    'ratings keep metadata, retry the same page and open original detail provenance',
    (tester) async {
      phone(tester);
      final pages = <String?>[];
      var failed = false;
      ApiService.clientFactory = () => MockClient((r) async {
        if (r.url.path.endsWith('/History/ratings')) {
          final page = r.url.queryParameters['page'];
          pages.add(page);
          if (page == '2' && !failed) {
            failed = true;
            return response({'message': 'Chưa tải được trang tiếp.'}, 503);
          }
          return response({
            'page': int.parse(page!),
            'pageSize': 50,
            'total': 2,
            'items': [
              {
                'danhGiaID': page == '1' ? 1 : 2,
                'noiDungID': page == '1' ? 'm1' : 'gone',
                'soSao': 4,
                'nhanXet': page == '1'
                    ? 'Một trải nghiệm đáng nhớ.'
                    : 'Nhận xét cũ',
                'thoiGian': '2026-10-23T04:00:00Z',
                'recommendationId': 'original-session',
                'available': page == '1',
                'content': content(
                  page == '1' ? 'm1' : 'gone',
                  page == '1' ? 'Phim của tôi' : 'Phim đã gỡ',
                ),
              },
            ],
          });
        }
        if (r.url.path.startsWith('/api/SavedContent/')) {
          return response({'saved': false});
        }
        if (r.url.path.endsWith('/Movies/m1')) {
          return response(content('m1', 'Phim của tôi'));
        }
        if (r.url.path.endsWith('/Interactions')) return response({});
        throw StateError('Unexpected ${r.url}');
      });
      await tester.pumpWidget(const MaterialApp(home: MyRatingsScreen()));
      await tester.pumpAndSettle();
      expect(find.text('Một trải nghiệm đáng nhớ.'), findsOneWidget);
      expect(find.byIcon(Icons.star_rounded), findsNWidgets(4));
      await tester.tap(find.text('Tải thêm đánh giá'));
      await tester.pumpAndSettle();
      expect(find.text('Phim của tôi'), findsOneWidget);
      await tester.tap(find.text('Thử lại'));
      await tester.pumpAndSettle();
      expect(pages, ['1', '2', '2']);
      expect(find.text('Nội dung hiện không còn khả dụng.'), findsOneWidget);
      await tester.tap(find.text('Phim của tôi'));
      await tester.pumpAndSettle();
      final screen = tester.widget<ContentDetailScreen>(
        find.byType(ContentDetailScreen),
      );
      expect(screen.contentId, 'm1');
      expect(screen.recommendationId, 'original-session');
      expect(tester.takeException(), isNull);
    },
  );

  testWidgets(
    'weekly summary defers hidden work and refreshes after returning from a route',
    (tester) async {
      phone(tester);
      var active = true, reads = 0;
      final requestDays = <String?>[];
      late StateSetter update;
      final navigator = GlobalKey<NavigatorState>();
      ApiService.clientFactory = () => MockClient((r) async {
        requestDays.add(r.url.queryParameters['days']);
        reads++;
        return response(weekly(reads + 2));
      });
      await tester.pumpWidget(
        MaterialApp(
          navigatorKey: navigator,
          navigatorObservers: [appRouteObserver],
          home: StatefulBuilder(
            builder: (_, set) {
              update = set;
              return Scaffold(
                body: ProfileWeeklyOverview(
                  active: active,
                  onOpenStatistics: () {},
                ),
              );
            },
          ),
        ),
      );
      await tester.pumpAndSettle();
      expect(reads, 1);
      update(() => active = false);
      await tester.pumpAndSettle();
      ApiService.activityRevision.value++;
      await tester.pumpAndSettle();
      expect(reads, 1);
      update(() => active = true);
      await tester.pumpAndSettle();
      expect(reads, 2);
      navigator.currentState!.push(
        MaterialPageRoute<void>(
          builder: (_) => const Scaffold(body: Text('Other route')),
        ),
      );
      await tester.pumpAndSettle();
      ApiService.activityRevision.value++;
      await tester.pumpAndSettle();
      expect(reads, 2);
      navigator.currentState!.pop();
      await tester.pumpAndSettle();
      expect(reads, 3);
      expect(find.text('5'), findsOneWidget);
      expect(requestDays, everyElement('7'));
      expect(tester.takeException(), isNull);
    },
  );

  testWidgets(
    'profile opens saved Library tab without fetching unvisited catalog',
    (tester) async {
      phone(tester);
      final calls = <String>[];
      ApiService.clientFactory = () => MockClient((r) async {
        calls.add(r.url.path);
        if (r.url.path.endsWith('/MoodMapping')) {
          return response([
            {'tamTrangID': 1, 'tenTamTrang': 'Happy'},
          ]);
        }
        if (r.url.path.endsWith('/Statistics')) return response(weekly());
        if (r.url.path.endsWith('/SavedContent')) {
          return response({'page': 1, 'total': 0, 'items': []});
        }
        if (r.url.path.endsWith('/Movies')) return response([]);
        throw StateError('Unexpected ${r.url}');
      });
      await tester.pumpWidget(
        MaterialApp(
          home: AppShell(currentUser: user, onLogoutConfirmed: () {}),
        ),
      );
      await tester.pumpAndSettle();
      await tester.tap(find.text('Để sau'));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Cá nhân'));
      await tester.pumpAndSettle();
      await tester.scrollUntilVisible(
        find.text('Nội dung đã lưu'),
        240,
        scrollable: find
            .descendant(
              of: find.byKey(const PageStorageKey('profile-scroll')),
              matching: find.byType(Scrollable),
            )
            .first,
      );
      await Scrollable.ensureVisible(
        tester.element(find.text('Nội dung đã lưu')),
        alignment: 0.3,
      );
      await tester.pumpAndSettle();
      await tester.tap(find.text('Nội dung đã lưu'));
      await tester.pumpAndSettle();
      expect(
        tester.widget<NavigationBar>(find.byType(NavigationBar)).selectedIndex,
        2,
      );
      expect(find.text('Chưa có nội dung đã lưu'), findsOneWidget);
      expect(calls.where((x) => x == '/api/Movies'), isEmpty);
      await tester.tap(find.byTooltip('Tải lại thư viện'));
      await tester.pumpAndSettle();
      expect(calls.where((x) => x == '/api/SavedContent').length, 2);
      await tester.tap(find.text('Kho nội dung'));
      await tester.pumpAndSettle();
      expect(calls.where((x) => x == '/api/Movies').length, 1);
      expect(tester.takeException(), isNull);
    },
  );

  testWidgets(
    'personal lists and overview fit a narrow phone with large text',
    (tester) async {
      phone(tester, 320);
      ApiService.clientFactory = () => MockClient((r) async {
        if (r.url.path.endsWith('/Statistics')) return response(weekly());
        return response({
          'page': 1,
          'total': 1,
          'items': [
            {
              ...entry(
                'gone',
                'Một tên nội dung khá dài để kiểm tra bố cục',
                available: false,
              ),
              'danhGiaID': 1,
              'noiDungID': 'gone',
              'soSao': 5,
              'nhanXet': 'Một nhận xét có dấu tiếng Việt.',
            },
          ],
        });
      });
      for (final screen in <Widget>[
        const SavedContentScreen(),
        const MyRatingsScreen(),
        Scaffold(body: ProfileWeeklyOverview(onOpenStatistics: () {})),
      ]) {
        await tester.pumpWidget(
          MaterialApp(
            builder: (_, child) => MediaQuery(
              data: const MediaQueryData(textScaler: TextScaler.linear(1.4)),
              child: child!,
            ),
            home: screen,
          ),
        );
        await tester.pumpAndSettle();
        expect(tester.takeException(), isNull);
        await tester.pumpWidget(const SizedBox.shrink());
      }
    },
  );

  test(
    'malformed saved state is a readable API error rather than a type cast',
    () async {
      ApiService.clientFactory = () =>
          MockClient((_) async => response({'saved': 0.5}));
      await expectLater(ApiService.isSaved('m1'), throwsA(isA<ApiException>()));
    },
  );
}
