import 'dart:math' as math;

import 'package:flutter/material.dart';

import '../core/colors.dart';

/// The static brand mark and the startup animation share this same drawing.
class MoodifyMark extends StatelessWidget {
  const MoodifyMark({super.key, this.size = 60});
  final double size;

  @override
  Widget build(BuildContext context) => Semantics(
    label: 'Moodify',
    image: true,
    child: RepaintBoundary(
      child: CustomPaint(
        size: Size.square(size),
        painter: MoodifyMarkPainter(
          const AlwaysStoppedAnimation(0),
          artworkScale: .82,
          tile: true,
        ),
      ),
    ),
  );
}

class MoodifyMarkPainter extends CustomPainter {
  MoodifyMarkPainter(this.motion, {this.artworkScale = 1, this.tile = false})
    : super(repaint: motion);
  final Animation<double> motion;
  final double artworkScale;
  final bool tile;

  @override
  void paint(Canvas canvas, Size size) {
    canvas.save();
    canvas.scale(size.width / 320);
    if (tile) {
      final tileShape = RRect.fromRectAndRadius(
        const Rect.fromLTWH(0, 0, 320, 320),
        const Radius.circular(76),
      );
      canvas.drawRRect(tileShape, Paint()..color = AppColors.background);
      canvas.drawRRect(
        tileShape.deflate(1),
        Paint()
          ..style = PaintingStyle.stroke
          ..strokeWidth = 1.2
          ..color = const Color(0xFF30283F),
      );
    }
    canvas.translate(160, 160);
    canvas.scale(artworkScale);
    canvas.translate(-160, -160);
    final phase = motion.value;
    _film(canvas, const Offset(159, 185), .42, 330, 48, phase, false);
    _film(canvas, const Offset(162, 170), -.43, 360, 64, phase, true);

    const center = Offset(151, 139);
    final shadow = Paint()
      ..shader = const RadialGradient(
        colors: [Color(0x8A000000), Color(0x00000000)],
      ).createShader(Rect.fromCircle(center: center, radius: 108));
    canvas.drawCircle(center + const Offset(0, 8), 108, shadow);

    canvas.save();
    canvas.translate(center.dx, center.dy);
    canvas.rotate(phase * math.pi * 2);
    const disc = Rect.fromLTWH(-88, -88, 176, 176);
    canvas.drawCircle(
      Offset.zero,
      88,
      Paint()
        ..shader = const LinearGradient(
          begin: Alignment.topLeft,
          end: Alignment.bottomRight,
          colors: [Color(0xFF444055), Color(0xFF101118), Color(0xFF302D42)],
        ).createShader(disc),
    );
    canvas.drawCircle(
      Offset.zero,
      85,
      Paint()..color = const Color(0xFF101219),
    );
    for (var i = 0; i < 14; i++) {
      canvas.drawCircle(
        Offset.zero,
        37 + i * 3.25,
        Paint()
          ..style = PaintingStyle.stroke
          ..strokeWidth = i.isEven ? .8 : .5
          ..color = Color.fromRGBO(193, 184, 221, i.isEven ? .13 : .075),
      );
    }
    canvas.drawCircle(
      Offset.zero,
      84,
      Paint()
        ..shader = const SweepGradient(
          colors: [
            Color(0x00101018),
            Color(0x24DAD1F9),
            Color(0x00101018),
            Color(0x00101018),
            Color(0x16A6A4D8),
            Color(0x00101018),
          ],
          stops: [0, .12, .25, .52, .65, .78],
        ).createShader(disc),
    );
    canvas.drawCircle(
      Offset.zero,
      32,
      Paint()
        ..shader = const LinearGradient(
          begin: Alignment.topLeft,
          end: Alignment.bottomRight,
          colors: [
            Color(0xFFB99CFF),
            AppColors.accentPrimary,
            Color(0xFF5854BD),
          ],
        ).createShader(const Rect.fromLTWH(-32, -32, 64, 64)),
    );
    canvas.drawCircle(
      Offset.zero,
      26,
      Paint()
        ..style = PaintingStyle.stroke
        ..strokeWidth = .7
        ..color = const Color(0x45FFFFFF),
    );
    final bars = [7.0, 13.0, 18.0, 13.0, 7.0];
    for (var i = 0; i < bars.length; i++) {
      canvas.drawRRect(
        RRect.fromRectAndRadius(
          Rect.fromCenter(
            center: Offset((i - 2) * 5.0, -9),
            width: 2.8,
            height: bars[i],
          ),
          const Radius.circular(2),
        ),
        Paint()..color = const Color(0xE6FFFFFF),
      );
    }
    canvas.drawCircle(Offset.zero, 4, Paint()..color = AppColors.background);
    canvas.drawCircle(
      Offset.zero,
      4.2,
      Paint()
        ..style = PaintingStyle.stroke
        ..strokeWidth = 1
        ..color = const Color(0x55FFFFFF),
    );
    canvas.restore();

    // A small tonearm makes the record readable even at phone sizes.
    final arm = Path()
      ..moveTo(249, 82)
      ..lineTo(249, 145)
      ..lineTo(219, 175);
    canvas.drawPath(
      arm,
      Paint()
        ..color = const Color(0xFF9B91B9)
        ..style = PaintingStyle.stroke
        ..strokeWidth = 3.4
        ..strokeCap = StrokeCap.round
        ..strokeJoin = StrokeJoin.round,
    );
    canvas.drawCircle(
      const Offset(249, 82),
      9,
      Paint()..color = const Color(0xFF302A43),
    );
    canvas.drawCircle(
      const Offset(249, 82),
      4,
      Paint()..color = const Color(0xFFB99CFF),
    );
    canvas.save();
    canvas.translate(218, 176);
    canvas.rotate(.72);
    canvas.drawRRect(
      RRect.fromRectAndRadius(
        const Rect.fromLTWH(-5, -8, 10, 17),
        const Radius.circular(3),
      ),
      Paint()..color = const Color(0xFFD2C2F5),
    );
    canvas.restore();
    canvas.restore();
  }

  void _film(
    Canvas canvas,
    Offset center,
    double angle,
    double width,
    double height,
    double phase,
    bool front,
  ) {
    canvas.save();
    canvas.translate(center.dx, center.dy);
    canvas.rotate(angle);
    final rect = Rect.fromCenter(
      center: Offset.zero,
      width: width,
      height: height,
    );
    final ribbon = RRect.fromRectAndRadius(rect, const Radius.circular(7));
    canvas.clipRRect(ribbon);
    canvas.drawRRect(
      ribbon,
      Paint()
        ..shader = LinearGradient(
          begin: Alignment.topCenter,
          end: Alignment.bottomCenter,
          colors: front
              ? [const Color(0xFF756091), const Color(0xFF382C55)]
              : [const Color(0xFF273B46), const Color(0xFF1C2535)],
        ).createShader(rect),
    );
    final shift = phase * 56 * (front ? 1 : -1);
    for (var i = -1; i < 29; i++) {
      final x = -width / 2 + i * 14 + shift;
      for (final y in [-height / 2 + 4, height / 2 - 10]) {
        canvas.drawRRect(
          RRect.fromRectAndRadius(
            Rect.fromLTWH(x, y, 8, 6),
            const Radius.circular(1.5),
          ),
          Paint()..color = const Color(0xFF171722),
        );
      }
    }
    for (var i = -1; i < 8; i++) {
      final frame = Rect.fromLTWH(
        -width / 2 + i * 56 + shift,
        -height / 2 + 15,
        49,
        height - 30,
      );
      canvas.drawRRect(
        RRect.fromRectAndRadius(frame, const Radius.circular(3)),
        Paint()
          ..color = front ? const Color(0xFF29213E) : const Color(0xFF19232D),
      );
      canvas.drawRRect(
        RRect.fromRectAndRadius(frame.deflate(.5), const Radius.circular(3)),
        Paint()
          ..style = PaintingStyle.stroke
          ..strokeWidth = .7
          ..color = const Color(0x229A89C9),
      );
    }
    canvas.restore();
  }

  @override
  bool shouldRepaint(MoodifyMarkPainter oldDelegate) =>
      oldDelegate.motion != motion ||
      oldDelegate.artworkScale != artworkScale ||
      oldDelegate.tile != tile;
}
