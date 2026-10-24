import 'dart:async';
import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:moodify_application/core/api_service.dart';
import 'package:moodify_application/models/mobile_models.dart';
import 'package:moodify_application/screens/content_detail_screen.dart';
import 'package:moodify_application/screens/mood_discovery_screen.dart';
import 'package:moodify_application/services/playback/spotify_embed.dart';

void main() {
  final originalFactory = ApiService.clientFactory;
  tearDown(() {
    ApiService.clientFactory = originalFactory;
    ApiService.requestTimeout = const Duration(seconds: 20);
    ApiService.onUnauthorized = null;
    ApiService.logout();
  });
  test('timeout includes waiting for response headers', () async {
    ApiService.requestTimeout = const Duration(milliseconds: 20);
    ApiService.clientFactory = () =>
        MockClient((_) => Completer<http.Response>().future);
    await expectLater(
      ApiService.request('/Music'),
      throwsA(
        isA<ApiException>().having(
          (e) => e.message,
          'message',
          contains('quá lâu'),
        ),
      ),
    );
  });
  test('late 401 cannot log out a newer session', () async {
    final delayed = Completer<http.Response>();
    var expired = 0;
    ApiService.onUnauthorized = () => expired++;
    ApiService.clientFactory = () => MockClient((r) async {
      if (r.url.path.endsWith('/login')) {
        return http.Response(
          jsonEncode({
            'token': 'token',
            'user': {'taiKhoanID': 1},
          }),
          200,
        );
      }
      return delayed.future;
    });
    await ApiService.login(emailOrUsername: 'first', password: 'password');
    final old = ApiService.request('/History');
    await Future<void>.delayed(Duration.zero);
    ApiService.logout();
    await ApiService.login(emailOrUsername: 'second', password: 'password');
    final assertion = expectLater(old, throwsA(isA<ApiException>()));
    delayed.complete(http.Response('{}', 401));
    await assertion;
    expect(expired, 0);
  });
  test('multi-genre parsing and source URLs', () {
    final movie = ContentItem.fromJson({
      'phim': {'theLoai': 'Action, Adventure, Action'},
    });
    expect(movie.genres, ['Action', 'Adventure']);
    expect(
      validSpotifyUrl(
        'https://open.spotify.com/embed/track/11dFghVXANMlKmJXsNCbNl',
        embed: true,
      ),
      true,
    );
    expect(
      validSpotifyUrl(
        'https://open.spotify.com.evil.test/embed/track/11dFghVXANMlKmJXsNCbNl',
        embed: true,
      ),
      false,
    );
    expect(validSpotifyUrl('javascript:alert(1)', embed: true), false);
  });
  testWidgets('music detail requests embed and has one Spotify link', (
    tester,
  ) async {
    var previews = 0;
    ApiService.clientFactory = () => MockClient((r) async {
      if (r.url.path.endsWith('/preview')) {
        previews++;
        return http.Response(
          jsonEncode({
            'mode': 'embed',
            'embedUrl':
                'https://open.spotify.com/embed/track/11dFghVXANMlKmJXsNCbNl',
          }),
          200,
        );
      }
      return http.Response(
        jsonEncode({
          'noiDungID': 'track',
          'tieuDe': 'Song fixture',
          'loaiNoiDung': 'Song',
          'nhac': {'tenNgheSi': 'Artist'},
        }),
        200,
      );
    });
    await tester.pumpWidget(
      const MaterialApp(
        home: ContentDetailScreen(contentId: 'track', type: 'Music'),
      ),
    );
    await tester.pumpAndSettle();
    await tester.scrollUntilVisible(find.text('Mở trên Spotify'), 200);
    expect(previews, 1);
    expect(find.byKey(const ValueKey('spotify-inline-player')), findsOneWidget);
    expect(find.text('Mở trên Spotify'), findsOneWidget);
    expect(tester.takeException(), isNull);
  });
  testWidgets('changing content type clears stale recommendations', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(900, 1800);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    var requests = 0;
    ApiService.clientFactory = () => MockClient((r) async {
      if (r.url.path.endsWith('/MoodMapping')) {
        return http.Response(
          jsonEncode([
            {
              'tamTrangID': 5,
              'tenTamTrang': 'Happy',
              'minValence': 0,
              'maxValence': 1,
              'minArousal': 0,
              'maxArousal': 1,
            },
          ]),
          200,
        );
      }
      requests++;
      return http.Response(
        jsonEncode({
          'mood': 'Happy',
          'items': [
            {
              'content': {
                'noiDungID': 'movie',
                'tieuDe': 'Old movie',
                'loaiNoiDung': 'Movie',
              },
              'match': 80,
            },
          ],
        }),
        200,
      );
    });
    await tester.pumpWidget(const MaterialApp(home: MoodDiscoveryScreen()));
    await tester.pumpAndSettle();
    await tester.tap(find.byType(DropdownButtonFormField<int>));
    await tester.pumpAndSettle();
    await tester.tap(find.text('😊  Vui vẻ').last);
    await tester.pumpAndSettle();
    await tester.tap(find.text('Xác nhận và nhận gợi ý'));
    await tester.pumpAndSettle();
    expect(find.text('Old movie'), findsOneWidget);
    expect(requests, 1);
    await tester.tap(find.text('Nhạc'));
    await tester.pumpAndSettle();
    expect(find.text('Old movie'), findsNothing);
    expect(requests, 1);
    expect(tester.takeException(), isNull);
  });
}
