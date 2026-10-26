// Regenerate every platform icon from the same painter used by Flutter.
// Run from mobile-app: flutter test --no-pub tool/generate_brand_icons.dart
import 'dart:io';
import 'dart:typed_data';
import 'dart:ui' as ui;

import 'package:flutter/animation.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:moodify_application/core/colors.dart';
import 'package:moodify_application/widgets/moodify_mark.dart';

void main() {
  testWidgets('generate Moodify platform icons', (tester) async {
    if (!File('pubspec.yaml').existsSync()) {
      throw StateError('Run this tool from mobile-app.');
    }
    await tester.runAsync(() async {
      Future<Uint8List> render(
        int size, {
        bool transparent = false,
        double artworkScale = .82,
      }) async {
        final recorder = ui.PictureRecorder();
        final canvas = ui.Canvas(recorder);
        if (!transparent) {
          canvas.drawRect(
            ui.Rect.fromLTWH(0, 0, size.toDouble(), size.toDouble()),
            ui.Paint()..color = AppColors.background,
          );
        }
        MoodifyMarkPainter(
          const AlwaysStoppedAnimation(0),
          artworkScale: artworkScale,
          tile: !transparent,
        ).paint(canvas, ui.Size.square(size.toDouble()));
        final picture = recorder.endRecording();
        final image = await picture.toImage(size, size);
        final bytes = await image.toByteData(format: ui.ImageByteFormat.png);
        image.dispose();
        picture.dispose();
        return bytes!.buffer.asUint8List();
      }

      Future<void> write(String path, List<int> bytes) async {
        final file = File(path);
        await file.parent.create(recursive: true);
        await file.writeAsBytes(bytes);
      }

      await write('assets/branding/moodify-icon.png', await render(1024));
      const densities = {
        'mdpi': 48,
        'hdpi': 72,
        'xhdpi': 96,
        'xxhdpi': 144,
        'xxxhdpi': 192,
      };
      const res = 'android/app/src/main/res';
      for (final entry in densities.entries) {
        await write(
          '$res/mipmap-${entry.key}/ic_launcher.png',
          await render(entry.value),
        );
      }
      await write(
        '$res/drawable-nodpi/moodify_icon_foreground.png',
        await render(432, transparent: true, artworkScale: .66),
      );
      await write(
        '$res/drawable-nodpi/splash_mark.png',
        await render(288, transparent: true, artworkScale: .8),
      );
      for (final size in [192, 512]) {
        await write('web/icons/Icon-$size.png', await render(size));
        await write(
          'web/icons/Icon-maskable-$size.png',
          await render(size, artworkScale: .66),
        );
      }
      await write('web/favicon.png', await render(48));

      // Windows supports PNG-compressed frames in an ICO container.
      const sizes = [16, 24, 32, 48, 64, 128, 256];
      final frames = <Uint8List>[];
      for (final size in sizes) {
        frames.add(await render(size));
      }
      final header = Uint8List(6 + sizes.length * 16);
      final data = ByteData.sublistView(header);
      data.setUint16(2, 1, Endian.little);
      data.setUint16(4, sizes.length, Endian.little);
      var offset = header.length;
      for (var i = 0; i < sizes.length; i++) {
        final entry = 6 + i * 16;
        data.setUint8(entry, sizes[i] == 256 ? 0 : sizes[i]);
        data.setUint8(entry + 1, sizes[i] == 256 ? 0 : sizes[i]);
        data.setUint16(entry + 4, 1, Endian.little);
        data.setUint16(entry + 6, 32, Endian.little);
        data.setUint32(entry + 8, frames[i].length, Endian.little);
        data.setUint32(entry + 12, offset, Endian.little);
        offset += frames[i].length;
      }
      final ico = BytesBuilder()..add(header);
      for (final frame in frames) {
        ico.add(frame);
      }
      await write('windows/runner/resources/app_icon.ico', ico.takeBytes());
    });
  });
}
