import 'dart:async';
import 'dart:math' as math;

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../core/colors.dart';
import 'moodify_mark.dart';

/// A one-time opening sequence that covers optional service initialization.
class AppStartup extends StatefulWidget {
  const AppStartup({super.key, required this.initialize, required this.child});

  final Future<void> Function() initialize;
  final Widget child;

  @override
  State<AppStartup> createState() => _AppStartupState();
}

class _AppStartupState extends State<AppStartup> with TickerProviderStateMixin {
  late final AnimationController _motion = AnimationController(
    vsync: this,
    duration: const Duration(seconds: 8),
  );
  late final AnimationController _reveal =
      AnimationController(
        vsync: this,
        duration: const Duration(milliseconds: 650),
      )..addStatusListener((status) {
        if (status == AnimationStatus.completed && mounted) {
          _motion.stop();
          setState(() => _finished = true);
        }
      });

  bool _started = false;
  bool _ready = false;
  bool _finished = false;
  bool _reduceMotion = false;

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    _reduceMotion =
        MediaQuery.disableAnimationsOf(context) ||
        MediaQuery.accessibleNavigationOf(context);
    if (_reduceMotion) {
      _motion.stop();
      if (_ready && !_finished) {
        _reveal.stop();
        _finished = true;
      }
    }
    if (!_started) {
      _started = true;
      if (!_reduceMotion) _motion.repeat();
      unawaited(_start());
    }
  }

  Future<void> _start() async {
    // Start both together: the animation never adds a second wait after boot.
    final minimum = Future<void>.delayed(
      _reduceMotion ? Duration.zero : const Duration(milliseconds: 1450),
    );
    final services = Future<void>.sync(widget.initialize)
        .timeout(const Duration(seconds: 8))
        .catchError((Object _) {
          // Optional Firebase services must not lock users out of the core app.
        });
    await Future.wait([minimum, services]);
    if (!mounted) return;
    setState(() {
      _ready = true;
      if (_reduceMotion) _finished = true;
    });
    if (!_finished) {
      WidgetsBinding.instance.addPostFrameCallback((_) {
        if (mounted && !_finished) _reveal.forward();
      });
    }
  }

  @override
  void dispose() {
    _motion.dispose();
    _reveal.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => Stack(
    fit: StackFit.expand,
    children: [
      if (_ready)
        IgnorePointer(
          ignoring: !_finished,
          child: ExcludeSemantics(
            excluding: !_finished,
            child: TickerMode(enabled: _finished, child: widget.child),
          ),
        ),
      if (!_finished)
        Positioned.fill(
          child: AnnotatedRegion<SystemUiOverlayStyle>(
            value: SystemUiOverlayStyle.light.copyWith(
              statusBarColor: Colors.transparent,
              systemNavigationBarColor: AppColors.background,
            ),
            child: AnimatedBuilder(
              animation: _reveal,
              child: _OpeningScene(
                motion: _motion,
                reduceMotion: _reduceMotion,
              ),
              builder: (context, child) {
                final progress = _reveal.value;
                final fade = const Interval(
                  .12,
                  1,
                  curve: Curves.easeInOut,
                ).transform(progress);
                return Opacity(
                  opacity: 1 - fade,
                  child: Transform.scale(
                    scale: 1 + Curves.easeInCubic.transform(progress) * .85,
                    alignment: const Alignment(0, -.18),
                    child: child,
                  ),
                );
              },
            ),
          ),
        ),
    ],
  );
}

class _OpeningScene extends StatelessWidget {
  const _OpeningScene({required this.motion, required this.reduceMotion});
  final Animation<double> motion;
  final bool reduceMotion;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Semantics(
      label: 'Moodify đang khởi động',
      liveRegion: true,
      child: ExcludeSemantics(
        child: Material(
          color: AppColors.background,
          child: DecoratedBox(
            decoration: const BoxDecoration(
              gradient: RadialGradient(
                center: Alignment(0, -.25),
                radius: .9,
                colors: [
                  Color(0xFF211B36),
                  Color(0xFF12131D),
                  AppColors.background,
                ],
                stops: [0, .55, 1],
              ),
            ),
            child: SafeArea(
              child: LayoutBuilder(
                builder: (context, constraints) {
                  final artworkSize = math
                      .min(
                        310.0,
                        math.min(
                          constraints.maxWidth - 40,
                          constraints.maxHeight * .46,
                        ),
                      )
                      .clamp(100.0, 310.0);
                  return Center(
                    child: SingleChildScrollView(
                      padding: const EdgeInsets.symmetric(
                        horizontal: 24,
                        vertical: 20,
                      ),
                      child: TweenAnimationBuilder<double>(
                        tween: Tween(begin: 0, end: 1),
                        duration: reduceMotion
                            ? Duration.zero
                            : const Duration(milliseconds: 650),
                        curve: Curves.easeOutCubic,
                        child: Column(
                          mainAxisSize: MainAxisSize.min,
                          children: [
                            RepaintBoundary(
                              child: CustomPaint(
                                size: Size.square(artworkSize),
                                painter: MoodifyMarkPainter(motion),
                                isComplex: false,
                                willChange: !reduceMotion,
                              ),
                            ),
                            const SizedBox(height: 12),
                            Text(
                              'MOODIFY',
                              textAlign: TextAlign.center,
                              style: theme.textTheme.headlineSmall?.copyWith(
                                fontSize: 27,
                                letterSpacing: 6,
                                fontWeight: FontWeight.w700,
                                color: AppColors.textPrimary,
                              ),
                            ),
                            const SizedBox(height: 12),
                            Text(
                              'Phim và nhạc cho cảm xúc của bạn.',
                              textAlign: TextAlign.center,
                              style: theme.textTheme.bodySmall?.copyWith(
                                color: const Color(0xFFA7A1B9),
                              ),
                            ),
                            const SizedBox(height: 30),
                            RepaintBoundary(
                              child: CustomPaint(
                                size: const Size(56, 12),
                                painter: _LoadingDotsPainter(motion),
                              ),
                            ),
                          ],
                        ),
                        builder: (context, value, child) => Opacity(
                          opacity: value,
                          child: Transform.translate(
                            offset: Offset(0, 12 * (1 - value)),
                            child: child,
                          ),
                        ),
                      ),
                    ),
                  );
                },
              ),
            ),
          ),
        ),
      ),
    );
  }
}

class _LoadingDotsPainter extends CustomPainter {
  _LoadingDotsPainter(this.motion) : super(repaint: motion);
  final Animation<double> motion;

  @override
  void paint(Canvas canvas, Size size) {
    for (var i = 0; i < 3; i++) {
      final wave = (math.sin(motion.value * math.pi * 8 - i * .8) + 1) / 2;
      canvas.drawCircle(
        Offset(size.width / 2 + (i - 1) * 14, size.height / 2),
        2.5 + wave * .5,
        Paint()
          ..color = const Color(0xFFB99CFF).withValues(alpha: .3 + wave * .6),
      );
    }
  }

  @override
  bool shouldRepaint(_LoadingDotsPainter oldDelegate) =>
      oldDelegate.motion != motion;
}
