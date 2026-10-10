import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:moodify_application/services/playback/youtube_embed.dart';

void main() {
  test('YouTube IDs accept canonical links and reject unsafe URLs', () {
    expect(
      youtubeVideoId('https://www.youtube.com/watch?v=12345678901'),
      '12345678901',
    );
    expect(
      youtubeVideoId('https://youtu.be/12345678901?si=abc'),
      '12345678901',
    );
    for (final url in [
      'http://youtu.be/12345678901',
      'https://youtube.com.evil.test/watch?v=12345678901',
      'https://user@youtube.com/watch?v=12345678901',
      'https://www.youtube.com/watch?v=short',
      'https://www.youtube.com/watch?v=12345678901&v=abcdefghijk',
    ]) {
      expect(youtubeVideoId(url), isNull);
    }
  });
  testWidgets(
    'inline player has safe dimensions and survives background/return',
    (tester) async {
      await tester.pumpWidget(
        const MaterialApp(
          home: Scaffold(
            body: SizedBox(
              width: 300,
              child: YoutubePlayer(videoId: '12345678901'),
            ),
          ),
        ),
      );
      await tester.pumpAndSettle();
      expect(
        tester.getSize(find.byType(YoutubePlayer)).height,
        greaterThanOrEqualTo(200),
      );
      tester.binding.handleAppLifecycleStateChanged(
        AppLifecycleState.paused,
      );
      await tester.pump();
      tester.binding.handleAppLifecycleStateChanged(
        AppLifecycleState.resumed,
      );
      await tester.pump();
      await tester.pumpWidget(const SizedBox());
      expect(tester.takeException(), isNull);
    },
  );
}

