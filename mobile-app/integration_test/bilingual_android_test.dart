import 'dart:convert';
import 'dart:ui' show FrameTiming;

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';
import 'package:moodify_application/main.dart' as app;
import 'package:moodify_application/core/api_service.dart';
import 'package:moodify_application/screens/content_detail_screen.dart';
import 'package:moodify_application/widgets/rating_dialog.dart';

void main() {
  IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets('Android VI/EN actual-model recommendation and feedback', (
    tester,
  ) async {
    const email = String.fromEnvironment('SMOKE_USER');
    const password = String.fromEnvironment('SMOKE_PASSWORD');
    expect(
      email.isNotEmpty && password.isNotEmpty,
      isTrue,
      reason: 'Provide ignored local test configuration.',
    );
    final frames = <FrameTiming>[];
    void recordFrames(List<FrameTiming> value) => frames.addAll(value);
    WidgetsBinding.instance.addTimingsCallback(recordFrames);
    final startup = Stopwatch()..start();
    app.main();

    Future<void> visible(Finder finder, {int seconds = 40}) async {
      final deadline = DateTime.now().add(Duration(seconds: seconds));
      while (finder.evaluate().isEmpty && DateTime.now().isBefore(deadline)) {
        await tester.pump(const Duration(milliseconds: 150));
        await tester.runAsync(
          () => Future<void>.delayed(const Duration(milliseconds: 100)),
        );
      }
      expect(finder, findsWidgets);
    }

    Future<void> tap(Finder finder) async {
      if (finder.evaluate().isEmpty) {
        await tester.scrollUntilVisible(
          finder,
          350,
          scrollable: find.byType(Scrollable).hitTestable().first,
          maxScrolls: 25,
        );
      }
      await visible(finder);
      await tester.ensureVisible(finder.first);
      await tester.pump(const Duration(milliseconds: 200));
      for (
        var attempt = 0;
        attempt < 8 && finder.hitTestable().evaluate().isEmpty;
        attempt++
      ) {
        final scrollable = find.ancestor(
          of: finder.first,
          matching: find.byType(Scrollable),
        );
        final center = tester.getCenter(finder.first);
        final height =
            tester.view.physicalSize.height / tester.view.devicePixelRatio;
        if (scrollable.evaluate().isNotEmpty &&
            (center.dy > height * .75 || center.dy < height * .12)) {
          await tester.drag(
            scrollable.first,
            Offset(0, center.dy > height * .75 ? -220 : 220),
          );
        }
        await tester.pump(const Duration(milliseconds: 200));
      }
      expect(
        finder.hitTestable(),
        findsWidgets,
        reason: 'Target must be reachable through the actual UI.',
      );
      await tester.tap(finder.hitTestable().first);
      await tester.pump(const Duration(milliseconds: 450));
    }

    Future<void> tab(String label) async {
      await tap(
        find.descendant(
          of: find.byType(NavigationBar),
          matching: find.text(label),
        ),
      );
      expect(
        tester.widget<NavigationBar>(find.byType(NavigationBar)).selectedIndex,
        const [
          'Trang chủ',
          'Khám phá',
          'Thư viện',
          'Lịch sử',
          'Cá nhân',
        ].indexOf(label),
      );
    }

    await visible(
      find.widgetWithText(TextFormField, 'Email hoặc tên đăng nhập'),
    );
    await tester.pump(const Duration(seconds: 2));
    await tester.runAsync(
      () => Future<void>.delayed(const Duration(milliseconds: 500)),
    );
    await tester.pump(const Duration(seconds: 1));
    startup.stop();
    await tester.enterText(find.byType(TextFormField).at(0), email);
    await tester.enterText(find.byType(TextFormField).at(1), password);
    await tap(find.text('Đăng nhập'));
    await visible(find.byType(NavigationBar));
    await tester.pump(const Duration(milliseconds: 600));
    if (find.text('Để sau').evaluate().isNotEmpty) {
      await tap(find.text('Để sau'));
    }
    final records = <Map<String, dynamic>>[];

    for (final sample in [
      ('vi', 'Tôi rất vui vì hôm nay được gặp lại bạn thân.'),
      (
        'en',
        'I am thrilled that I got my dream job! I want to celebrate and dance all night.',
      ),
    ]) {
      await tab('Khám phá');
      for (var i = 0; i < 8 && find.byType(TextField).evaluate().isEmpty; i++) {
        await tester.drag(
          find.byType(Scrollable).hitTestable().first,
          const Offset(0, 500),
        );
        await tester.pump(const Duration(milliseconds: 200));
      }
      await visible(find.byType(TextField));
      await tester.ensureVisible(find.byType(TextField).first);
      await tester.pump(const Duration(milliseconds: 200));
      await tester.enterText(find.byType(TextField).first, sample.$2);
      FocusManager.instance.primaryFocus?.unfocus();
      await tester.pump(const Duration(milliseconds: 150));
      final timer = Stopwatch()..start();
      await tap(find.text('Phân tích tâm trạng'));
      await visible(find.text('Tâm trạng gợi ý'));
      timer.stop();
      expect(
        find.text(
          sample.$1 == 'vi'
              ? 'Model: PhoBERT · Tiếng Việt'
              : 'Model: RoBERTa VA · English',
        ),
        findsOneWidget,
      );
      await tap(find.text('Xác nhận và nhận gợi ý'));

      Map<String, dynamic>? history;
      final deadline = DateTime.now().add(const Duration(seconds: 30));
      while (DateTime.now().isBefore(deadline)) {
        final raw = await tester.runAsync(
          () => ApiService.request('/History?page=1'),
        );
        final rows = (raw as Map<String, dynamic>)['items'] as List;
        final matches = rows.where(
          (r) => r['analysisLanguage'] == sample.$1 && r['source'] == 'ai_va',
        );
        if (matches.isNotEmpty) {
          history = Map<String, dynamic>.from(matches.first);
          break;
        }
        await tester.pump(const Duration(milliseconds: 150));
      }
      expect(history, isNotNull);
      expect(history!['analysisId'], isNotNull);
      expect(history['analyzerVersion'], isNotNull);
      final content = (history['recommendedItems'] as List).first;
      final title = content['tieuDe'] as String;
      await tap(find.text(title));
      await visible(find.byType(ContentDetailScreen));
      await tap(find.text('Đánh giá nội dung'));
      await visible(find.byType(RatingDialog));
      await tap(find.byTooltip('5 sao'));
      await tester.enterText(
        find.byType(TextField).last,
        'Synthetic Android integration feedback; not NLP gold.',
      );
      FocusManager.instance.primaryFocus?.unfocus();
      await tap(find.text('Gửi đánh giá'));
      await visible(find.text('Đã lưu đánh giá của bạn.'));
      final ratings = await tester.runAsync(
        () => ApiService.request('/History/ratings?page=1'),
      );
      expect(
        ((ratings as Map<String, dynamic>)['items'] as List).any(
          (r) =>
              r['recommendationId'] == history!['recommendationId'] &&
              r['soSao'] == 5,
        ),
        isTrue,
      );
      final back = find.byTooltip('Quay lại').evaluate().isNotEmpty
          ? find.byTooltip('Quay lại')
          : find.byTooltip('Back');
      if (back.evaluate().isNotEmpty) {
        await tap(back);
      } else {
        expect(
          find.byType(NavigationBar),
          findsOneWidget,
          reason: 'Expected either a visible Back action or an already restored app shell.',
        );
      }
      await tester.pump(const Duration(milliseconds: 500));
      await tab('Lịch sử');
      await visible(find.text('Tâm trạng và gợi ý'));
      records.add({
        'language': sample.$1,
        'modelVersion': history['analyzerVersion'],
        'recommendationId': history['recommendationId'],
        'analysisToResultMs': timer.elapsedMilliseconds,
      });
      expect(tester.takeException(), isNull);
    }
    WidgetsBinding.instance.removeTimingsCallback(recordFrames);
    final totals = frames.map((f) => f.totalSpan.inMicroseconds / 1000).toList()
      ..sort();
    debugPrint(
      'MOODIFY_ANDROID_RESULT=${jsonEncode({'status': 'passed', 'startupToLoginMs': startup.elapsedMilliseconds, 'frames': totals.length, 'frameP95Ms': totals.isEmpty ? null : totals[(totals.length * .95).floor().clamp(0, totals.length - 1)], 'framesOver16ms': totals.where((ms) => ms > 16.67).length, 'records': records, 'scope': 'Android emulator/debug UI, actual models and isolated database; playback/providers excluded'})}',
    );
  }, timeout: const Timeout(Duration(minutes: 6)));
}
