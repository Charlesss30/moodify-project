import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:moodify_application/widgets/app_startup.dart';

void main() {
  Widget host(Future<void> Function() initialize, {bool reduced = false}) =>
      MaterialApp(
        builder: (context, child) => MediaQuery(
          data: MediaQuery.of(context).copyWith(disableAnimations: reduced),
          child: child!,
        ),
        home: AppStartup(
          initialize: initialize,
          child: const Scaffold(body: Text('Application ready')),
        ),
      );

  testWidgets('startup waits for initialization then releases all animation', (
    tester,
  ) async {
    final initialized = Completer<void>();
    await tester.pumpWidget(host(() => initialized.future));
    expect(find.text('MOODIFY'), findsOneWidget);
    expect(find.text('Application ready'), findsNothing);
    await tester.pump(const Duration(seconds: 2));
    expect(find.text('MOODIFY'), findsOneWidget);
    initialized.complete();
    await tester.pump();
    await tester.pumpAndSettle();
    expect(find.text('Application ready'), findsOneWidget);
    expect(find.text('MOODIFY'), findsNothing);
    expect(tester.hasRunningAnimations, isFalse);
  });

  testWidgets('optional service failure still opens the application', (
    tester,
  ) async {
    await tester.pumpWidget(host(() async => throw StateError('unavailable')));
    await tester.pump(const Duration(milliseconds: 1500));
    await tester.pumpAndSettle();
    expect(find.text('Application ready'), findsOneWidget);
    expect(tester.takeException(), isNull);
  });

  testWidgets('reduced motion skips decorative delay and zoom', (tester) async {
    await tester.pumpWidget(host(() async {}, reduced: true));
    await tester.pump();
    await tester.pumpAndSettle();
    expect(find.text('MOODIFY'), findsNothing);
    expect(find.text('Application ready'), findsOneWidget);
    expect(tester.hasRunningAnimations, isFalse);
  });

  testWidgets('a stalled optional service cannot hold startup indefinitely', (
    tester,
  ) async {
    final stalled = Completer<void>();
    await tester.pumpWidget(host(() => stalled.future));
    await tester.pump(const Duration(seconds: 9));
    await tester.pumpAndSettle();
    expect(find.text('Application ready'), findsOneWidget);
    stalled.complete();
    await tester.pump();
    expect(tester.takeException(), isNull);
  });

  testWidgets('rebuilding the root does not replay initialization or opening', (
    tester,
  ) async {
    var starts = 0;
    Future<void> initialize() async => starts++;
    await tester.pumpWidget(host(initialize));
    await tester.pump(const Duration(milliseconds: 1500));
    await tester.pumpAndSettle();
    await tester.pumpWidget(host(initialize));
    await tester.pumpAndSettle();
    expect(starts, 1);
    expect(find.text('MOODIFY'), findsNothing);
  });

  testWidgets('disposing before initialization completes is safe', (
    tester,
  ) async {
    final initialized = Completer<void>();
    await tester.pumpWidget(host(() => initialized.future));
    await tester.pumpWidget(const SizedBox());
    initialized.complete();
    await tester.pump(const Duration(seconds: 2));
    expect(tester.takeException(), isNull);
  });

  testWidgets('opening fits a small screen and enlarged text', (tester) async {
    tester.view.devicePixelRatio = 1;
    tester.view.physicalSize = const Size(320, 480);
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    final initialized = Completer<void>();
    await tester.pumpWidget(
      MaterialApp(
        builder: (context, child) => MediaQuery(
          data: MediaQuery.of(context)
              .copyWith(textScaler: const TextScaler.linear(1.4)),
          child: child!,
        ),
        home: AppStartup(
          initialize: () => initialized.future,
          child: const Scaffold(),
        ),
      ),
    );
    await tester.pump(const Duration(milliseconds: 800));
    expect(tester.takeException(), isNull);
    initialized.complete();
    await tester.pump(const Duration(seconds: 2));
    await tester.pumpAndSettle();
    expect(tester.takeException(), isNull);
  });
}
