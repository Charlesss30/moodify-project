
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';
import 'package:moodify_application/services/notifications/system_notifications.dart';

void main() {
  IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'Android posts one system notification and opens its destination',
    (tester) async {
      var opened = '';
      await tester.pumpWidget(
        const MaterialApp(
          home: Scaffold(
            body: Center(child: Text('Moodify system notification check')),
          ),
        ),
      );
      await SystemNotifications.initialize(onOpen: (screen) => opened = screen);
      debugPrint('MOODIFY_NOTIFICATION_PERMISSION_READY');
      await tester.runAsync(
        () => Future<void>.delayed(const Duration(seconds: 6)),
      );
      for (var i = 0; i < 2; i++) {
        expect(
          await SystemNotifications.show(
            title: 'Moodify · Kiểm thử hệ thống',
            body: 'Chạm để mở Thống kê',
            screen: 'statistics',
            messageId: 'system-notification-smoke',
          ),
          isTrue,
        );
      }
      debugPrint('MOODIFY_NOTIFICATION_POSTED');
      final deadline = DateTime.now().add(const Duration(seconds: 45));
      while (opened.isEmpty && DateTime.now().isBefore(deadline)) {
        await tester.pump(const Duration(milliseconds: 100));
        await tester.runAsync(
          () => Future<void>.delayed(const Duration(milliseconds: 100)),
        );
      }
      expect(opened, 'statistics');
      await SystemNotifications.cancelAll();
      debugPrint('MOODIFY_NOTIFICATION_TAP_PASSED');
    },
  );
}
