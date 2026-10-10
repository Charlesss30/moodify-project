import 'activity_history_screen.dart';

import 'package:flutter/material.dart';

import '../core/api_service.dart';
import '../core/colors.dart';
import '../models/mobile_models.dart';

class HistoryScreen extends StatefulWidget {
  const HistoryScreen({super.key});

  @override
  State<HistoryScreen> createState() => _HistoryScreenState();
}

class _HistoryScreenState extends State<HistoryScreen> {
  final List<MoodHistoryEntry> _entries = [];
  int _page = 0;
  int _total = 0;
  int _rangeDays = 30;
  bool _loading = true;
  bool _loadingMore = false;
  String? _error;

  @override
  void initState() {
    super.initState();
    ApiService.historyRevision.addListener(_refreshHistory);
    _loadFirstPage();
  }

  int _generation = 0;
  void _refreshHistory() {
    if (mounted) _loadFirstPage();
  }

  @override
  void dispose() {
    ApiService.historyRevision.removeListener(_refreshHistory);
    super.dispose();
  }

  Future<void> _loadFirstPage() async {
    final generation = ++_generation;
    setState(() {
      _loading = true;
      _loadingMore = false;
      _error = null;
      _entries.clear();
      _page = 0;
    });
    try {
      final result = await ApiService.history(1);
      if (!mounted || generation != _generation) return;
      setState(() {
        _entries.addAll(result.items);
        _page = result.page;
        _total = result.total;
      });
    } on ApiException catch (error) {
      if (mounted && generation == _generation) {
        setState(() => _error = error.message);
      }
    } finally {
      if (mounted && generation == _generation) {
        setState(() => _loading = false);
      }
    }
  }

  Future<void> _loadMore() async {
    if (_loading || _loadingMore || _entries.length >= _total) return;
    final generation = _generation;
    setState(() {
      _loadingMore = true;
      _error = null;
    });
    try {
      final result = await ApiService.history(_page + 1);
      if (!mounted || generation != _generation) return;
      setState(() {
        _entries.addAll(result.items);
        _page = result.page;
        _total = result.total;
      });
    } on ApiException catch (error) {
      if (mounted && generation == _generation) {
        setState(() => _error = error.message);
      }
    } finally {
      if (mounted && generation == _generation) {
        setState(() => _loadingMore = false);
      }
    }
  }

  List<MoodHistoryEntry> get _filtered {
    if (_rangeDays == 0) return _entries;
    final since = DateTime.now().subtract(Duration(days: _rangeDays));
    return _entries
        .where((entry) => entry.createdAt?.isAfter(since) ?? false)
        .toList();
  }

  @override
  Widget build(BuildContext context) {
    final entries = _filtered;
    return Scaffold(
      appBar: AppBar(
        title: const Text('Lịch sử tâm trạng'),
        actions: [
          IconButton(
            tooltip: 'L?ch s? t??ng t?c',
            icon: const Icon(Icons.touch_app),
            onPressed: () => Navigator.of(context).push(
              MaterialPageRoute<void>(
                builder: (_) => const ActivityHistoryScreen(),
              ),
            ),
          ),
          IconButton(
            tooltip: 'Tải lại',
            onPressed: _loading ? null : _loadFirstPage,
            icon: const Icon(Icons.refresh),
          ),
        ],
      ),
      body: _loading && _entries.isEmpty
          ? const Center(child: CircularProgressIndicator())
          : ListView(
              padding: const EdgeInsets.fromLTRB(18, 8, 18, 24),
              children: [
                if (_error != null)
                  _HistoryError(message: _error!, onRetry: _loadFirstPage),
                SegmentedButton<int>(
                  segments: const [
                    ButtonSegment(value: 7, label: Text('7 ngày')),
                    ButtonSegment(value: 30, label: Text('30 ngày')),
                    ButtonSegment(value: 90, label: Text('90 ngày')),
                    ButtonSegment(value: 0, label: Text('Tất cả')),
                  ],
                  selected: {_rangeDays},
                  onSelectionChanged: (values) =>
                      setState(() => _rangeDays = values.first),
                ),
                const SizedBox(height: 18),
                Text(
                  'Tích cực (tím) / Năng lượng (xanh) · −1 đến 1',
                  style: Theme.of(context).textTheme.titleMedium,
                ),
                const SizedBox(height: 4),
                Text(
                  'Biểu đồ dùng ${entries.length} bản ghi đã tải (${_entries.length} / $_total).',
                  style: const TextStyle(
                    fontSize: 12,
                    color: AppColors.textSecondary,
                  ),
                ),
                const SizedBox(height: 8),
                if (entries.isEmpty)
                  const _HistoryEmpty(
                    message: 'Chưa có bản ghi trong khoảng thời gian này.',
                  )
                else
                  Container(
                    height: 190,
                    margin: const EdgeInsets.only(bottom: 18),
                    padding: const EdgeInsets.fromLTRB(8, 14, 16, 12),
                    decoration: BoxDecoration(
                      color: AppColors.surface,
                      borderRadius: BorderRadius.circular(18),
                      border: Border.all(
                        color: Colors.white.withValues(alpha: .06),
                      ),
                    ),
                    child: CustomPaint(
                      painter: _MoodChartPainter(entries),
                      child: const SizedBox.expand(),
                    ),
                  ),
                if (!_loading && _entries.isEmpty && _error == null)
                  const _HistoryEmpty(
                    message: 'Bạn chưa có lịch sử yêu cầu gợi ý.',
                  ),
                for (final entry in entries) _HistoryTile(entry: entry),
                if (_entries.length < _total)
                  Padding(
                    padding: const EdgeInsets.only(top: 8),
                    child: OutlinedButton(
                      onPressed: _loadingMore ? null : _loadMore,
                      child: _loadingMore
                          ? const SizedBox.square(
                              dimension: 18,
                              child: CircularProgressIndicator(strokeWidth: 2),
                            )
                          : const Text('Tải thêm lịch sử'),
                    ),
                  ),
              ],
            ),
    );
  }
}

class _HistoryTile extends StatelessWidget {
  const _HistoryTile({required this.entry});
  final MoodHistoryEntry entry;

  @override
  Widget build(BuildContext context) {
    final recommendations = entry.recommendedItems;
    return Card(
      margin: const EdgeInsets.only(top: 10),
      child: Padding(
        padding: const EdgeInsets.all(14),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                const Icon(Icons.mood_outlined, color: AppColors.accentPrimary),
                const SizedBox(width: 8),
                Expanded(
                  child: Text(
                    entry.moodName.isEmpty
                        ? 'Tâm trạng #${entry.moodId}'
                        : entry.moodName,
                    style: const TextStyle(fontWeight: FontWeight.w700),
                  ),
                ),
                Text(
                  _date(entry.createdAt),
                  style: const TextStyle(
                    fontSize: 11,
                    color: AppColors.textSecondary,
                  ),
                ),
              ],
            ),
            const SizedBox(height: 8),
            Text(
              'Valence ${entry.valence.toStringAsFixed(2)} · Arousal ${entry.arousal.toStringAsFixed(2)}',
              style: const TextStyle(
                color: AppColors.textSecondary,
                fontSize: 12,
              ),
            ),
            const SizedBox(height: 10),
            if (recommendations == null)
              const Text(
                'Bản ghi cũ chưa lưu danh sách nội dung được gợi ý.',
                style: TextStyle(fontSize: 12, color: AppColors.textSecondary),
              )
            else if (recommendations.isEmpty)
              const Text(
                'Lần yêu cầu này không có nội dung phù hợp.',
                style: TextStyle(fontSize: 12, color: AppColors.textSecondary),
              )
            else
              ...recommendations.map(
                (item) => Padding(
                  padding: const EdgeInsets.only(top: 6),
                  child: Row(
                    children: [
                      Icon(
                        item.isMusic ? Icons.music_note : Icons.movie_outlined,
                        size: 16,
                        color: AppColors.accentPrimary,
                      ),
                      const SizedBox(width: 8),
                      Expanded(
                        child: Text(
                          item.title,
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                        ),
                      ),
                    ],
                  ),
                ),
              ),
          ],
        ),
      ),
    );
  }

  String _date(DateTime? value) => value == null
      ? 'Không rõ ngày'
      : '${value.day.toString().padLeft(2, '0')}/${value.month.toString().padLeft(2, '0')}/${value.year}';
}

class _HistoryEmpty extends StatelessWidget {
  const _HistoryEmpty({required this.message});
  final String message;

  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.symmetric(vertical: 26),
    child: Center(
      child: Text(
        message,
        textAlign: TextAlign.center,
        style: const TextStyle(color: AppColors.textSecondary),
      ),
    ),
  );
}

class _HistoryError extends StatelessWidget {
  const _HistoryError({required this.message, required this.onRetry});
  final String message;
  final VoidCallback onRetry;

  @override
  Widget build(BuildContext context) => Card(
    child: ListTile(
      leading: const Icon(Icons.cloud_off, color: AppColors.warning),
      title: Text(message),
      trailing: IconButton(onPressed: onRetry, icon: const Icon(Icons.refresh)),
    ),
  );
}

class _MoodChartPainter extends CustomPainter {
  _MoodChartPainter(this.entries);
  final List<MoodHistoryEntry> entries;

  @override
  void paint(Canvas canvas, Size size) {
    const left = 6.0;
    const right = 2.0;
    const top = 4.0;
    const bottom = 6.0;
    final chart = Rect.fromLTRB(
      left,
      top,
      size.width - right,
      size.height - bottom,
    );
    final gridPaint = Paint()
      ..color = Colors.white.withValues(alpha: .08)
      ..strokeWidth = 1;
    for (var index = 0; index <= 4; index++) {
      final y = chart.top + chart.height * index / 4;
      canvas.drawLine(Offset(chart.left, y), Offset(chart.right, y), gridPaint);
    }
    final ordered = entries.where((entry) => entry.createdAt != null).toList()
      ..sort((a, b) => a.createdAt!.compareTo(b.createdAt!));
    if (ordered.isEmpty) return;
    for (final series in [
      (AppColors.accentPrimary, (MoodHistoryEntry entry) => entry.valence),
      (AppColors.accentSecondary, (MoodHistoryEntry entry) => entry.arousal),
    ]) {
      final paint = Paint()
        ..color = series.$1
        ..strokeWidth = 2.5
        ..style = PaintingStyle.stroke
        ..strokeCap = StrokeCap.round;
      final path = Path();
      for (var index = 0; index < ordered.length; index++) {
        final start = ordered.first.createdAt!.millisecondsSinceEpoch;
        final span = ordered.last.createdAt!.millisecondsSinceEpoch - start;
        final x = span == 0
            ? chart.center.dx
            : chart.left +
                  chart.width *
                      (ordered[index].createdAt!.millisecondsSinceEpoch -
                          start) /
                      span;
        final value = series.$2(ordered[index]).clamp(-1, 1);
        final y = chart.bottom - (value + 1) / 2 * chart.height;
        canvas.drawCircle(Offset(x, y), 3, Paint()..color = series.$1);
        if (index == 0) {
          path.moveTo(x, y);
        } else {
          path.lineTo(x, y);
        }
      }
      canvas.drawPath(path, paint);
    }
  }

  @override
  bool shouldRepaint(covariant _MoodChartPainter oldDelegate) =>
      oldDelegate.entries != entries;
}
