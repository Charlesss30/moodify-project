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
import 'package:moodify_application/screens/history_screen.dart';
import 'package:moodify_application/screens/library_screen.dart';
import 'package:moodify_application/screens/login_screen.dart';
import 'package:moodify_application/screens/mood_discovery_screen.dart';
import 'package:moodify_application/screens/register_screen.dart';
import 'package:moodify_application/screens/content_detail_screen.dart';
import 'package:moodify_application/screens/profile_screen.dart';
import 'package:moodify_application/screens/statistics_screen.dart';
import 'package:moodify_application/widgets/daily_trend_chart.dart';

const user = SessionUser(
  accountId: 1,
  fullName: 'An',
  email: 'an@test.invalid',
  role: 'User',
);
http.Response json(Object body, [int status = 200]) => http.Response(
  jsonEncode(body),
  status,
  headers: {'content-type': 'application/json; charset=utf-8'},
);
Map<String, dynamic> statistics(int count) => {
  'summary': {
    'moodEntries': count,
    'interactions': 3,
    'activeDays': 1,
    'moodDays': 1,
    'openedContents': 2,
    'ratings': 0,
    'averageStars': null,
  },
  'previous': {'moodEntries': 2, 'interactions': 0},
  'moods': [],
  'genres': [],
  'hours': [],
  'contentTypes': [],
  'trend': [
    {
      'date': '2026-10-21',
      'valence': .5,
      'arousal': -.2,
      'moodCount': 2,
      'interactions': 3,
    },
    {
      'date': '2026-10-22',
      'valence': null,
      'arousal': null,
      'moodCount': 0,
      'interactions': 0,
    },
  ],
};
void main() {
  tearDown(() {
    ApiService.clientFactory = http.Client.new;
    ApiService.logout();
  });
  testWidgets(
    'tabs defer API work and preserve library search and loaded catalog',
    (tester) async {
      tester.view.physicalSize = const Size(390, 844);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);
      final calls = <String>[];
      ApiService.clientFactory = () => MockClient((request) async {
        calls.add(request.url.path);
        if (request.url.path.endsWith('/Statistics')) {
          return json(statistics(2));
        }
        if (request.url.path.endsWith('/MoodMapping')) {
          return json([
            {'tamTrangID': 1, 'tenTamTrang': 'Happy'},
          ]);
        }
        if (request.url.path.endsWith('/Movies')) {
          return json([
            {
              'noiDungID': 'm1',
              'tieuDe': 'Film',
              'loaiNoiDung': 'Movie',
              'phim': {'director': 'Director fixture'},
            },
          ]);
        }
        throw StateError('Unvisited tab must not request ${request.url.path}');
      });
      await tester.pumpWidget(
        MaterialApp(
          home: AppShell(currentUser: user, onLogoutConfirmed: () {}),
        ),
      );
      await tester.pumpAndSettle();
      expect(calls, ['/api/MoodMapping']);
      await tester.tap(find.text('Để sau'));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Thư viện'));
      await tester.pumpAndSettle();
      expect(calls, ['/api/MoodMapping', '/api/Movies']);
      await tester.enterText(find.byType(TextField), 'Director fixture');
      await tester.testTextInput.receiveAction(TextInputAction.search);
      await tester.pumpAndSettle();
      expect(find.text('Film'), findsOneWidget);
      await tester.tap(find.text('Cá nhân'));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Thư viện'));
      await tester.pumpAndSettle();
      expect(
        tester.widget<TextField>(find.byType(TextField)).controller!.text,
        'Director fixture',
      );
      expect(calls.where((x) => x == '/api/Movies').length, 1);
      expect(tester.takeException(), isNull);
    },
  );
  testWidgets('chart selects missing days without inventing neutral data', (
    tester,
  ) async {
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: SizedBox(
            width: 340,
            child: DailyTrendChart(rows: statistics(2)['trend'] as List),
          ),
        ),
      ),
    );
    expect(find.textContaining('Cảm xúc: +0.50'), findsOneWidget);
    await tester.tap(find.byTooltip('Ngày sau'));
    await tester.pumpAndSettle();
    expect(find.text('Ngày 22/10/2026'), findsOneWidget);
    expect(
      find.text('Chưa ghi nhận tâm trạng trong ngày này.'),
      findsOneWidget,
    );
    expect(find.textContaining('Cảm xúc: +0.50'), findsNothing);
    expect(tester.takeException(), isNull);
  });
  testWidgets('statistics ignores late previous-range response', (
    tester,
  ) async {
    final first = Completer<http.Response>();
    ApiService.clientFactory = () => MockClient(
      (r) async => r.url.queryParameters['days'] == '30'
          ? first.future
          : json(statistics(7)),
    );
    await tester.pumpWidget(const MaterialApp(home: StatisticsScreen()));
    await tester.pump();
    await tester.tap(find.text('7 ngày'));
    await tester.pumpAndSettle();
    expect(find.text('7 lượt ghi nhận tâm trạng'), findsOneWidget);
    first.complete(json(statistics(30)));
    await tester.pumpAndSettle();
    expect(find.text('30 lượt ghi nhận tâm trạng'), findsNothing);
    expect(tester.takeException(), isNull);
  });
  testWidgets(
    'email change requires password, preserves edits on rejection and submits exact profile',
    (tester) async {
      final sent = <Map<String, dynamic>>[];
      SessionUser? updated;
      ApiService.clientFactory = () => MockClient((r) async {
        if (r.url.path.endsWith('/Statistics')) return json(statistics(2));
        expect(r.url.path, '/api/Account/profile');
        sent.add(jsonDecode(r.body));
        if (sent.length == 1) {
          return json({'message': 'Mật khẩu hiện tại chưa đúng.'}, 400);
        }
        return json({
          'taiKhoanID': 1,
          'tenDangNhap': 'An mới',
          'email': 'new@test.invalid',
          'vaiTro': 'User',
        });
      });
      await tester.pumpWidget(
        MaterialApp(
          home: ProfileScreen(
            user: user,
            onUserUpdated: (u) => updated = u,
            onLogoutConfirmed: () {},
          ),
        ),
      );
      await tester.ensureVisible(find.text('Thông tin tài khoản'));
      await tester.tap(find.text('Thông tin tài khoản'));
      await tester.pumpAndSettle();
      await tester.enterText(find.byType(TextFormField).at(0), 'An mới');
      await tester.enterText(
        find.byType(TextFormField).at(1),
        'new@test.invalid',
      );
      await tester.pumpAndSettle();
      await tester.tap(find.text('Lưu'));
      await tester.pumpAndSettle();
      expect(sent, isEmpty);
      await tester.enterText(find.byType(TextFormField).at(2), 'wrong');
      await tester.tap(find.text('Lưu'));
      await tester.pumpAndSettle();
      expect(find.text('Mật khẩu hiện tại chưa đúng.'), findsOneWidget);
      expect(find.text('An mới'), findsOneWidget);
      await tester.enterText(find.byType(TextFormField).at(2), 'correct');
      await tester.tap(find.text('Lưu'));
      await tester.pumpAndSettle();
      expect(sent.last, {
        'tenDangNhap': 'An mới',
        'email': 'new@test.invalid',
        'currentPassword': 'correct',
      });
      expect(updated?.email, 'new@test.invalid');
      expect(tester.takeException(), isNull);
    },
  );
  testWidgets(
    'covering music detail detaches player then restores it without extra preview request',
    (tester) async {
      var previews = 0;
      ApiService.clientFactory = () => MockClient((r) async {
        if (r.url.path.endsWith('/Interactions')) return json({});
        if (r.url.path.endsWith('/preview')) {
          previews++;
          return json({
            'mode': 'embed',
            'embedUrl':
                'https://open.spotify.com/embed/track/11dFghVXANMlKmJXsNCbNl',
          });
        }
        return json({
          'noiDungID': 's1',
          'tieuDe': 'Song',
          'loaiNoiDung': 'Song',
        });
      });
      final navigator = GlobalKey<NavigatorState>();
      await tester.pumpWidget(
        MaterialApp(
          navigatorKey: navigator,
          navigatorObservers: [appRouteObserver],
          home: const ContentDetailScreen(contentId: 's1', type: 'Music'),
        ),
      );
      await tester.pumpAndSettle();
      expect(
        find.byKey(const ValueKey('spotify-inline-player')),
        findsOneWidget,
      );
      navigator.currentState!.push(
        MaterialPageRoute<void>(
          builder: (_) => const Scaffold(body: Text('Other route')),
        ),
      );
      await tester.pumpAndSettle();
      expect(
        find.byKey(
          const ValueKey('spotify-inline-player'),
          skipOffstage: false,
        ),
        findsNothing,
      );
      navigator.currentState!.pop();
      await tester.pumpAndSettle();
      expect(
        find.byKey(const ValueKey('spotify-inline-player')),
        findsOneWidget,
      );
      expect(previews, 1);
      await tester.pumpWidget(const SizedBox.shrink());
      expect(tester.takeException(), isNull);
    },
  );
  testWidgets(
    'phone-width layouts tolerate large text and a visible keyboard',
    (tester) async {
      tester.view.physicalSize = const Size(320, 740);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);
      ApiService.clientFactory = () => MockClient((r) async {
        if (r.url.path.endsWith('/Statistics')) return json(statistics(3));
        if (r.url.path.endsWith('/MoodMapping')) {
          return json([
            {'tamTrangID': 1, 'tenTamTrang': 'Happy'},
          ]);
        }
        if (r.url.path.endsWith('/History')) {
          return json({'page': 1, 'total': 0, 'pageSize': 50, 'items': []});
        }
        if (r.url.path.endsWith('/Movies')) {
          return json([
            {
              'noiDungID': 'm1',
              'tieuDe': 'Tên phim đủ dài để cần xuống dòng ở màn hình nhỏ',
              'loaiNoiDung': 'Movie',
            },
          ]);
        }
        return json({});
      });
      final screens = <Widget>[
        LoginScreen(onLoginSuccess: (_) {}, onNavigateToRegister: () {}),
        RegisterScreen(onRegisterSuccess: () {}, onNavigateToLogin: () {}),
        const MoodDiscoveryScreen(),
        const LibraryScreen(),
        const HistoryScreen(),
        const StatisticsScreen(),
        ProfileScreen(
          user: user,
          onUserUpdated: (_) {},
          onLogoutConfirmed: () {},
        ),
      ];
      for (final screen in screens) {
        await tester.pumpWidget(
          MaterialApp(
            builder: (context, child) => MediaQuery(
              data: MediaQuery.of(context).copyWith(
                textScaler: TextScaler.linear(1.4),
                disableAnimations: true,
              ),
              child: child!,
            ),
            home: screen,
          ),
        );
        await tester.pumpAndSettle();
        expect(
          tester.takeException(),
          isNull,
          reason: '${screen.runtimeType} on a 320px screen',
        );
        await tester.pumpWidget(const SizedBox.shrink());
      }
      await tester.pumpWidget(
        MaterialApp(
          home: ProfileScreen(
            user: user,
            onUserUpdated: (_) {},
            onLogoutConfirmed: () {},
          ),
        ),
      );
      await tester.pumpAndSettle();
      await tester.ensureVisible(find.text('Thông tin tài khoản'));
      await tester.tap(find.text('Thông tin tài khoản'));
      await tester.pumpAndSettle();
      await tester.enterText(
        find.byType(TextFormField).at(1),
        'changed@test.invalid',
      );
      tester.view.viewInsets = const FakeViewPadding(bottom: 300);
      addTearDown(tester.view.resetViewInsets);
      await tester.pumpAndSettle();
      await tester.ensureVisible(find.text('Lưu'));
      expect(
        tester.takeException(),
        isNull,
        reason: 'Account dialog must fit above the keyboard',
      );
      await tester.pumpWidget(const SizedBox.shrink());
    },
  );
  testWidgets(
    'activity history is visible and loaded only after choosing its tab',
    (tester) async {
      var activities = 0;
      ApiService.clientFactory = () => MockClient((r) async {
        if (r.url.path.endsWith('/Interactions')) {
          activities++;
          return json({'page': 1, 'total': 0, 'items': []});
        }
        return json({
          'page': 1,
          'pageSize': 50,
          'total': 1,
          'items': [
            {
              'lichSuID': 1,
              'tamTrangID': 1,
              'tenTamTrang': 'Happy',
              'thoiGian': DateTime.now().toUtc().toIso8601String(),
              'valence': .4,
              'arousal': .2,
              'recommendedItems': [],
            },
          ],
        });
      });
      await tester.pumpWidget(const MaterialApp(home: HistoryScreen()));
      await tester.pumpAndSettle();
      expect(activities, 0);
      expect(find.text('Biểu đồ các bản ghi đã tải'), findsOneWidget);
      await tester.tap(find.text('Hoạt động'));
      await tester.pumpAndSettle();
      expect(activities, 1);
      expect(find.text('Hoạt động xem, nghe'), findsOneWidget);
      await tester.tap(find.text('Tâm trạng').first);
      await tester.pumpAndSettle();
      await tester.tap(find.text('Hoạt động'));
      await tester.pumpAndSettle();
      expect(activities, 1);
      expect(tester.takeException(), isNull);
    },
  );
  testWidgets(
    'failed history refresh retains content and retry re-requests first page',
    (tester) async {
      final pages = <String?>[];
      var count = 0;
      ApiService.clientFactory = () => MockClient((r) async {
        pages.add(r.url.queryParameters['page']);
        count++;
        if (count == 2) {
          return json({'message': 'Tạm thời chưa tải được.'}, 503);
        }
        return json({
          'page': 1,
          'pageSize': 50,
          'total': 1,
          'items': [
            {
              'lichSuID': 1,
              'tamTrangID': 1,
              'tenTamTrang': 'Happy',
              'thoiGian': DateTime.now().toUtc().toIso8601String(),
              'valence': .4,
              'arousal': .2,
              'recommendedItems': [],
            },
          ],
        });
      });
      await tester.pumpWidget(const MaterialApp(home: HistoryScreen()));
      await tester.pumpAndSettle();
      await tester.tap(find.byTooltip('Tải lại'));
      await tester.pumpAndSettle();
      expect(find.text('Vui vẻ'), findsOneWidget);
      await tester.tap(find.text('Thử lại'));
      await tester.pumpAndSettle();
      expect(pages, ['1', '1', '1']);
      expect(tester.takeException(), isNull);
    },
  );

  testWidgets(
    'history expansion state stays independent from scroll offsets and other entries',
    (tester) async {
      tester.view.physicalSize = const Size(390, 844);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);
      ApiService.clientFactory = () => MockClient((request) async {
        if (request.url.path.endsWith('/Interactions')) {
          return json({'page': 1, 'total': 0, 'items': []});
        }
        return json({
          'page': 1,
          'pageSize': 50,
          'total': 24,
          'items': List.generate(
            24,
            (index) => {
              'lichSuID': index + 1,
              'tamTrangID': 1,
              'tenTamTrang': 'Happy',
              'thoiGian': DateTime.now()
                  .subtract(Duration(minutes: index))
                  .toUtc()
                  .toIso8601String(),
              'valence': .4,
              'arousal': .2,
              'recommendedItems': [
                {
                  'noiDungID': 'film-$index',
                  'tieuDe': 'Film $index',
                  'loaiNoiDung': 'Movie',
                },
              ],
            },
          ),
        });
      });
      await tester.pumpWidget(const MaterialApp(home: HistoryScreen()));
      await tester.pumpAndSettle();
      final scroll = find.byKey(const PageStorageKey('mood-history-scroll'));
      await tester.drag(scroll, const Offset(0, -700));
      await tester.pumpAndSettle();
      await tester.drag(scroll, const Offset(0, -700));
      await tester.pumpAndSettle();
      expect(tester.takeException(), isNull);
      final visible = find.byType(ExpansionTile).hitTestable().first;
      final tileKey = tester.widget<ExpansionTile>(visible).key!;
      await tester.tap(visible);
      await tester.pumpAndSettle();
      expect(
        find.descendant(
          of: find.byKey(tileKey),
          matching: find.byType(ListTile),
        ),
        findsNWidgets(2),
      );
      final position = tester
          .state<ScrollableState>(
            find
                .descendant(of: scroll, matching: find.byType(Scrollable))
                .first,
          )
          .position;
      final offset = position.pixels;
      position.jumpTo(0);
      await tester.pumpAndSettle();
      expect(find.text('Film 0'), findsNothing);
      position.jumpTo(offset);
      await tester.pumpAndSettle();
      expect(
        find.descendant(
          of: find.byKey(tileKey),
          matching: find.byType(ListTile),
        ),
        findsNWidgets(2),
      );
      await tester.tap(find.text('Hoạt động'));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Tâm trạng').first);
      await tester.pumpAndSettle();
      expect(position.pixels, closeTo(offset, 1));
      expect(tester.takeException(), isNull);
    },
  );

  testWidgets(
    'profile handles an unreadable avatar and validates account name before requesting',
    (tester) async {
      var requests = 0;
      ApiService.clientFactory = () => MockClient((request) async {
        if (request.url.path.endsWith('/Statistics')) {
          return json(statistics(2));
        }
        requests++;
        return json({});
      });
      await tester.pumpWidget(
        MaterialApp(
          home: ProfileScreen(
            user: user.copyWith(
              avatarDataUrl: 'data:image/png;base64,AQIDBA==',
            ),
            onUserUpdated: (_) {},
            onLogoutConfirmed: () {},
          ),
        ),
      );
      // Image decoding runs outside the fake test clock.
      await tester.runAsync(() async {
        await Future<void>.delayed(const Duration(milliseconds: 100));
      });
      await tester.pumpAndSettle();
      expect(find.text('A'), findsOneWidget);
      expect(tester.takeException(), isNull);
      expect(find.text('Vai trò'), findsNothing);
      expect(find.text('Máy chủ API'), findsNothing);
      expect(find.text('Cá nhân hoá'), findsNothing);
      await tester.ensureVisible(find.text('Thông tin tài khoản'));
      await tester.tap(find.text('Thông tin tài khoản'));
      await tester.pumpAndSettle();
      expect(find.byIcon(Icons.add_a_photo_outlined), findsOneWidget);
      await tester.enterText(find.byType(TextFormField).at(0), 'A');
      await tester.tap(find.text('Lưu'));
      await tester.pumpAndSettle();
      expect(
        find.text('Tên người dùng cần từ 2 đến 100 ký tự.'),
        findsOneWidget,
      );
      expect(requests, 0);
      await tester.tap(find.text('Huỷ'));
      await tester.pumpAndSettle();
      expect(tester.takeException(), isNull);
    },
  );
  testWidgets(
    'password dialog validates, toggles visibility, blocks duplicate saves and retains errors',
    (tester) async {
      final response = Completer<http.Response>();
      final sent = <Map<String, dynamic>>[];
      var loggedOut = false;
      ApiService.clientFactory = () => MockClient((request) async {
        if (request.url.path.endsWith('/Statistics')) {
          return json(statistics(2));
        }
        expect(request.url.path, '/api/Account/password');
        sent.add(jsonDecode(request.body));
        if (sent.length == 1) return response.future;
        return json({'message': 'Đã đổi mật khẩu thành công.'});
      });
      await tester.pumpWidget(
        MaterialApp(
          home: ProfileScreen(
            user: user,
            onUserUpdated: (_) {},
            onLogoutConfirmed: () => loggedOut = true,
          ),
        ),
      );
      await tester.ensureVisible(find.text('Đổi mật khẩu'));
      await tester.tap(find.text('Đổi mật khẩu'));
      await tester.pumpAndSettle();
      final fields = find.byType(TextFormField);
      final save = find.widgetWithText(FilledButton, 'Đổi mật khẩu');
      await tester.enterText(fields.at(0), 'old-secret-123');
      await tester.enterText(fields.at(1), 'old-secret-123');
      await tester.enterText(fields.at(2), 'old-secret-123');
      await tester.tap(save);
      await tester.pumpAndSettle();
      expect(
        find.text('Mật khẩu mới cần khác mật khẩu hiện tại.'),
        findsOneWidget,
      );
      expect(sent, isEmpty);
      await tester.enterText(fields.at(1), 'new-secret-123');
      await tester.tap(save);
      await tester.pumpAndSettle();
      expect(find.text('Hai lần nhập mật khẩu chưa khớp.'), findsOneWidget);
      expect(sent, isEmpty);
      await tester.enterText(fields.at(2), 'new-secret-123');
      await tester.tap(find.byTooltip('Hiện mật khẩu').first);
      await tester.pumpAndSettle();
      expect(
        tester.widget<TextField>(find.byType(TextField).at(0)).obscureText,
        isFalse,
      );
      expect(
        tester.widget<TextField>(find.byType(TextField).at(1)).obscureText,
        isTrue,
      );
      await tester.tap(save);
      await tester.pump();
      expect(sent, [
        {'currentPassword': 'old-secret-123', 'newPassword': 'new-secret-123'},
      ]);
      expect(
        tester
            .widget<FilledButton>(
              find.widgetWithText(FilledButton, 'Đang lưu…'),
            )
            .onPressed,
        isNull,
      );
      response.complete(json({'message': 'Mật khẩu hiện tại chưa đúng.'}, 400));
      await tester.pumpAndSettle();
      expect(find.text('Mật khẩu hiện tại chưa đúng.'), findsOneWidget);
      expect(
        tester.widget<TextField>(find.byType(TextField).at(1)).controller!.text,
        'new-secret-123',
      );
      await tester.tap(save);
      await tester.pumpAndSettle();
      expect(find.byType(TextFormField), findsNothing);
      expect(find.text('Đăng xuất khỏi Moodify?'), findsOneWidget);
      await tester.tap(find.text('Ở lại'));
      await tester.pumpAndSettle();
      expect(loggedOut, isFalse);
      expect(sent.length, 2);
      expect(tester.takeException(), isNull);
    },
  );
}
