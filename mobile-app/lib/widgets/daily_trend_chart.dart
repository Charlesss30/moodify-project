import 'dart:math' as math;

import 'package:flutter/material.dart';

import '../core/colors.dart';
import '../core/presentation.dart';

/// A bounded chart: selecting a day never performs an API call or fills data gaps.
class DailyTrendChart extends StatefulWidget {
  const DailyTrendChart({super.key, required this.rows, this.activity = false});
  final List<dynamic> rows;
  final bool activity;
  @override
  State<DailyTrendChart> createState() => _DailyTrendChartState();
}

class _DailyTrendChartState extends State<DailyTrendChart> {
  int selected = 0;
  @override
  void initState() {
    super.initState();
    _selectLatest();
  }

  @override
  void didUpdateWidget(covariant DailyTrendChart oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.rows != widget.rows) _selectLatest();
  }

  void _selectLatest() {
    selected = widget.rows.lastIndexWhere(
      (r) => widget.activity
          ? (r['interactions'] as num? ?? 0) > 0
          : r['valence'] != null || r['arousal'] != null,
    );
    if (selected < 0) selected = math.max(0, widget.rows.length - 1);
  }

  void _select(int value) =>
      setState(() => selected = value.clamp(0, widget.rows.length - 1));
  @override
  Widget build(BuildContext context) {
    final rows = widget.rows;
    if (rows.isEmpty) return const Text('Chưa có dữ liệu trong khoảng này.');
    final row = rows[selected];
    final textStyle = Theme.of(context).textTheme.bodySmall!;
    final hasMood = row['valence'] is num || row['arousal'] is num;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        if (!widget.activity) ...[
          const Wrap(
            spacing: 18,
            runSpacing: 8,
            children: [
              _Legend(
                color: _TrendPainter.moodColor,
                title: 'Cảm xúc',
                detail: 'Tiêu cực ↔ tích cực',
              ),
              _Legend(
                color: _TrendPainter.energyColor,
                title: 'Năng lượng',
                detail: 'Thấp ↔ cao',
              ),
            ],
          ),
          const SizedBox(height: 12),
          Text(
            'Trên: tích cực / nhiều năng lượng. Dưới: tiêu cực / ít năng lượng. Mốc 0 là trung tính.',
            style: textStyle,
          ),
        ],
        const SizedBox(height: 8),
        Semantics(
          label:
              'Biểu đồ theo ngày. Đang chọn ${shortDate(row['date'] as String?, year: true)}',
          child: LayoutBuilder(
            builder: (context, constraints) => GestureDetector(
              behavior: HitTestBehavior.opaque,
              onTapDown: (event) {
                final width =
                    constraints.maxWidth -
                    _TrendPainter.left -
                    _TrendPainter.right;
                if (width <= 0) return;
                _select(
                  ((event.localPosition.dx - _TrendPainter.left) /
                          width *
                          (rows.length - 1))
                      .round(),
                );
              },
              child: SizedBox(
                height: 214,
                width: double.infinity,
                child: RepaintBoundary(
                  child: CustomPaint(
                    painter: _TrendPainter(
                      rows: rows,
                      selected: selected,
                      activity: widget.activity,
                      labelColor: textStyle.color ?? AppColors.textSecondary,
                      fontSize: MediaQuery.textScalerOf(context)
                          .scale(11)
                          .clamp(11, 15),
                    ),
                  ),
                ),
              ),
            ),
          ),
        ),
        Container(
          padding: const EdgeInsets.fromLTRB(12, 8, 12, 12),
          decoration: BoxDecoration(
            color: AppColors.surfaceSecondary,
            borderRadius: BorderRadius.circular(14),
          ),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  Expanded(
                    child: Text(
                      'Ngày ${shortDate(row['date'] as String?, year: true)}',
                      style: Theme.of(context).textTheme.titleSmall,
                    ),
                  ),
                  IconButton(
                    tooltip: 'Ngày trước',
                    visualDensity: VisualDensity.compact,
                    onPressed: selected == 0
                        ? null
                        : () => _select(selected - 1),
                    icon: const Icon(Icons.chevron_left),
                  ),
                  IconButton(
                    tooltip: 'Ngày sau',
                    visualDensity: VisualDensity.compact,
                    onPressed: selected == rows.length - 1
                        ? null
                        : () => _select(selected + 1),
                    icon: const Icon(Icons.chevron_right),
                  ),
                ],
              ),
              if (widget.activity)
                Text('${row['interactions'] ?? 0} tương tác', style: textStyle)
              else if (!hasMood)
                Text(
                  'Chưa ghi nhận tâm trạng trong ngày này.',
                  style: textStyle,
                )
              else ...[
                if (row['moodCount'] is num)
                  Text(
                    '${row['moodCount']} lượt ghi nhận · trung bình trong ngày',
                    style: textStyle,
                  ),
                const SizedBox(height: 4),
                Text(
                  'Cảm xúc: ${_value(row['valence'])} · Năng lượng: ${_value(row['arousal'])}',
                  style: textStyle,
                ),
              ],
            ],
          ),
        ),
        const SizedBox(height: 10),
        Text(
          widget.activity
              ? 'Chạm vào cột để xem số lượt mở nội dung trong ngày.'
              : 'Chạm vào biểu đồ để xem từng ngày. Đường ngắt khi thiếu bản ghi, không tự tính thành điểm 0.',
          style: textStyle,
        ),
      ],
    );
  }

  String _value(dynamic value) => value is num
      ? '${value > 0 ? '+' : ''}${value.toStringAsFixed(2)}'
      : 'Chưa có';
}

class _Legend extends StatelessWidget {
  const _Legend({
    required this.color,
    required this.title,
    required this.detail,
  });
  final Color color;
  final String title, detail;
  @override
  Widget build(BuildContext context) => Row(
    mainAxisSize: MainAxisSize.min,
    children: [
      Container(
        width: 9,
        height: 9,
        decoration: BoxDecoration(color: color, shape: BoxShape.circle),
      ),
      const SizedBox(width: 7),
      Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(title, style: Theme.of(context).textTheme.labelLarge),
          Text(detail, style: Theme.of(context).textTheme.bodySmall),
        ],
      ),
    ],
  );
}

class _TrendPainter extends CustomPainter {
  _TrendPainter({
    required this.rows,
    required this.selected,
    required this.activity,
    required this.labelColor,
    required this.fontSize,
  });
  static const left = 36.0, right = 12.0, top = 16.0, bottom = 34.0;
  static const moodColor = Color(0xFFD2A7FF), energyColor = Color(0xFF71D7D2);
  final List<dynamic> rows;
  final int selected;
  final bool activity;
  final Color labelColor;
  final double fontSize;
  void label(
    Canvas canvas,
    String text,
    Offset position, {
    TextAlign align = TextAlign.left,
  }) {
    final painter = TextPainter(
      text: TextSpan(
        text: text,
        style: TextStyle(color: labelColor, fontSize: fontSize),
      ),
      textDirection: TextDirection.ltr,
    )..layout();
    final x = align == TextAlign.right
        ? position.dx - painter.width
        : align == TextAlign.center
        ? position.dx - painter.width / 2
        : position.dx;
    painter.paint(canvas, Offset(x, position.dy));
    painter.dispose();
  }

  @override
  void paint(Canvas canvas, Size size) {
    if (rows.isEmpty || size.width <= left + right) return;
    final width = size.width - left - right,
        height = size.height - top - bottom;
    final maxCount = rows.fold<double>(
      1,
      (max, row) =>
          math.max(max, (row['interactions'] as num? ?? 0).toDouble()),
    );
    double x(int i) => rows.length == 1
        ? left + width / 2
        : left + width * i / (rows.length - 1);
    double y(double v) => activity
        ? top + height * (1 - v / maxCount)
        : top + height * (1 - v.clamp(-1, 1)) / 2;
    final ticks = activity
        ? <double>{0, (maxCount / 2).floorToDouble(), maxCount}.toList()
        : [-1.0, 0.0, 1.0];
    for (final tick in ticks) {
      canvas.drawLine(
        Offset(left, y(tick)),
        Offset(left + width, y(tick)),
        Paint()
          ..color = Colors.white.withValues(alpha: tick == 0 ? .18 : .07)
          ..strokeWidth = 1,
      );
      label(
        canvas,
        activity
            ? tick.toInt().toString()
            : tick > 0
            ? '+1'
            : tick < 0
            ? '−1'
            : '0',
        Offset(left - 10, y(tick) - fontSize / 2),
        align: TextAlign.right,
      );
    }
    canvas.drawLine(
      Offset(x(selected), top),
      Offset(x(selected), top + height),
      Paint()
        ..color = Colors.white.withValues(alpha: .2)
        ..strokeWidth = 1,
    );
    if (activity) {
      final barWidth = (width / rows.length * .65).clamp(2.0, 18.0);
      for (var i = 0; i < rows.length; i++) {
        final count = (rows[i]['interactions'] as num? ?? 0).toDouble();
        if (count <= 0) continue;
        canvas.drawRRect(
          RRect.fromRectAndRadius(
            Rect.fromLTRB(
              x(i) - barWidth / 2,
              y(count),
              x(i) + barWidth / 2,
              top + height,
            ),
            const Radius.circular(3),
          ),
          Paint()
            ..color = i == selected
                ? moodColor
                : moodColor.withValues(alpha: .45),
        );
      }
    } else {
      for (final field in ['valence', 'arousal']) {
        final color = field == 'valence' ? moodColor : energyColor;
        Offset? previous;
        for (var i = 0; i < rows.length; i++) {
          final value = rows[i][field] as num?;
          if (value == null) {
            previous = null;
            continue;
          }
          final point = Offset(x(i), y(value.toDouble()));
          if (previous != null) {
            canvas.drawLine(
              previous,
              point,
              Paint()
                ..color = color
                ..strokeWidth = 2
                ..strokeCap = StrokeCap.round,
            );
          }
          canvas.drawCircle(
            point,
            i == selected
                ? 4
                : rows.length > 35
                ? 1.8
                : 2.7,
            Paint()..color = color,
          );
          previous = point;
        }
      }
    }
    final indexes = <int>{
      0,
      if (width > 230) rows.length ~/ 2,
      rows.length - 1,
    }.toList()..sort();
    for (final i in indexes) {
      label(
        canvas,
        shortDate(rows[i]['date'] as String?),
        Offset(x(i), top + height + 10),
        align: i == 0
            ? TextAlign.left
            : i == rows.length - 1
            ? TextAlign.right
            : TextAlign.center,
      );
    }
  }

  @override
  bool shouldRepaint(covariant _TrendPainter old) =>
      old.rows != rows ||
      old.selected != selected ||
      old.activity != activity ||
      old.fontSize != fontSize ||
      old.labelColor != labelColor;
}
