import 'package:flutter/material.dart';

import '../core/api_service.dart';
import '../core/presentation.dart';
import '../core/colors.dart';
import '../models/mobile_models.dart';
import '../widgets/app_widgets.dart';
import 'activity_history_screen.dart';
import 'content_detail_screen.dart';

class HistoryScreen extends StatefulWidget {
  const HistoryScreen({super.key});
  @override
  State<HistoryScreen> createState() => _HistoryScreenState();
}

class _HistoryScreenState extends State<HistoryScreen> {
  int _tab = 0;
  bool _activityVisited = false;
  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(title: const Text('Lịch sử')),
    body: Column(
      children: [
        Padding(
          padding: const EdgeInsets.fromLTRB(20, 0, 20, 16),
          child: SizedBox(
            width: double.infinity,
            child: SegmentedButton<int>(
              segments: const [
                ButtonSegment(value: 0, label: Text('Tâm trạng')),
                ButtonSegment(value: 1, label: Text('Hoạt động')),
              ],
              selected: {_tab},
              onSelectionChanged: (v) => setState(() {
                _tab = v.first;
                if (_tab == 1) _activityVisited = true;
              }),
            ),
          ),
        ),
        Expanded(
          child: IndexedStack(
            index: _tab,
            children: [
              const _MoodHistoryPanel(),
              _activityVisited
                  ? const ActivityHistoryScreen(embedded: true)
                  : const SizedBox.shrink(),
            ],
          ),
        ),
      ],
    ),
  );
}

class _MoodHistoryPanel extends StatefulWidget {
  const _MoodHistoryPanel();
  @override
  State<_MoodHistoryPanel> createState() => _MoodHistoryPanelState();
}

class _MoodHistoryPanelState extends State<_MoodHistoryPanel> {
  final List<MoodHistoryEntry> _entries = [];
  int _page = 0, _total = 0, _range = 30, _generation = 0;
  bool _loading = true, _loadingMore = false;
  String? _error;
  bool _retryReset = true;
  @override
  void initState() {
    super.initState();
    ApiService.historyRevision.addListener(_reload);
    _load(reset: true);
  }

  void _reload() {
    if (mounted) _load(reset: true);
  }

  @override
  void dispose() {
    ApiService.historyRevision.removeListener(_reload);
    super.dispose();
  }

  Future<void> _load({bool reset = false}) async {
    if (!reset && (_loading || _loadingMore || _entries.length >= _total)) {
      return;
    }
    final generation = reset ? ++_generation : _generation;
    setState(() {
      _error = null;
      if (reset) {
        _loading = true;
        _loadingMore = false;
      } else {
        _loadingMore = true;
      }
    });
    try {
      final result = await ApiService.history(reset ? 1 : _page + 1);
      if (!mounted || generation != _generation) return;
      setState(() {
        if (reset) _entries.clear();
        _entries.addAll(result.items);
        _page = result.page;
        _total = result.total;
      });
    } on ApiException catch (e) {
      if (mounted && generation == _generation) {
        setState(() {
          _error = e.message;
          _retryReset = reset;
        });
      }
    } finally {
      if (mounted && generation == _generation) {
        setState(() {
          _loading = false;
          _loadingMore = false;
        });
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final since = DateTime.now().subtract(Duration(days: _range));
    final entries = _range == 0
        ? _entries
        : _entries.where((e) => e.createdAt?.isAfter(since) ?? false).toList();
    return RefreshIndicator(
      onRefresh: () => _load(reset: true),
      child: CustomScrollView(
        key: const PageStorageKey('mood-history-scroll'),
        physics: const AlwaysScrollableScrollPhysics(),
        slivers: [
          SliverPadding(
            padding: const EdgeInsets.symmetric(horizontal: 20),
            sliver: SliverToBoxAdapter(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Row(
                    children: [
                      Expanded(
                        child: Text(
                          'Tâm trạng và gợi ý',
                          style: Theme.of(context).textTheme.titleMedium,
                        ),
                      ),
                      IconButton(
                        tooltip: 'Tải lại',
                        onPressed: _loading ? null : () => _load(reset: true),
                        icon: const Icon(Icons.refresh),
                      ),
                    ],
                  ),
                  Text(
                    'Nhìn lại cảm xúc và những nội dung từng được đề xuất.',
                    style: Theme.of(context).textTheme.bodySmall,
                  ),
                  const SizedBox(height: 12),
                  SizedBox(
                    width: double.infinity,
                    child: SegmentedButton<int>(
                      showSelectedIcon: false,
                      segments: const [
                        ButtonSegment(value: 7, label: Text('7 ngày')),
                        ButtonSegment(value: 30, label: Text('30 ngày')),
                        ButtonSegment(value: 90, label: Text('90 ngày')),
                        ButtonSegment(value: 0, label: Text('Tất cả')),
                      ],
                      selected: {_range},
                      onSelectionChanged: (v) =>
                          setState(() => _range = v.first),
                    ),
                  ),
                  const SizedBox(height: 14),
                  Text(
                    '${_entries.length} / $_total bản ghi đã tải. Bộ lọc áp dụng trên các bản ghi này.',
                    style: Theme.of(context).textTheme.bodySmall,
                  ),

                  if (entries.isNotEmpty)
                    Card(
                      child: ExpansionTile(
                        key: const PageStorageKey('mood-history-chart'),
                        title: const Text(
                          'Biểu đồ các bản ghi đã tải',
                          style: TextStyle(fontSize: 14),
                        ),
                        subtitle: const Text(
                          'Tím: cảm xúc · Xanh: năng lượng',
                          style: TextStyle(fontSize: 12),
                        ),
                        childrenPadding: const EdgeInsets.fromLTRB(
                          16,
                          0,
                          16,
                          16,
                        ),
                        children: [
                          const Text(
                            'Thang −1 đến +1. Mỗi chấm là một bản ghi, đặt theo thời gian.',
                            style: TextStyle(fontSize: 12),
                          ),
                          SizedBox(
                            height: 170,
                            width: double.infinity,
                            child: RepaintBoundary(
                              child: CustomPaint(
                                painter: _MoodChartPainter(entries),
                              ),
                            ),
                          ),
                        ],
                      ),
                    ),
                  if (_loading && _entries.isNotEmpty)
                    const LinearProgressIndicator(),
                  if (_error != null)
                    StatusCard(
                      message: _error!,
                      onRetry: () => _load(reset: _retryReset),
                    ),
                  if (_loading && _entries.isEmpty) const LoadingCards(),
                  if (!_loading && entries.isEmpty && _error == null)
                    const StatusCard(
                      icon: Icons.history,
                      title: 'Chưa có lịch sử trong khoảng này',
                      message: 'Các lần nhận gợi ý sẽ được lưu tại đây. Bạn có thể tải thêm bản ghi cũ nếu còn.',
                    ),
                  const SizedBox(height: 12),
                ],
              ),
            ),
          ),
          SliverPadding(
            padding: const EdgeInsets.symmetric(horizontal: 20),
            sliver: SliverList.builder(
              itemCount: entries.length,
              itemBuilder: (context, i) {
                final entry = entries[i];
                final date = shortDate(
                  entry.createdAt?.toIso8601String(),
                  year: true,
                );
                final previous = i > 0
                    ? shortDate(
                        entries[i - 1].createdAt?.toIso8601String(),
                        year: true,
                      )
                    : null;
                return Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    if (date != previous)
                      Padding(
                        padding: const EdgeInsets.only(bottom: 10, top: 6),
                        child: Text(
                          date,
                          style: Theme.of(context).textTheme.labelLarge,
                        ),
                      ),
                    _HistoryTile(entry: entry),
                  ],
                );
              },
            ),
          ),
          if (_entries.length < _total)
            SliverPadding(
              padding: const EdgeInsets.all(20),
              sliver: SliverToBoxAdapter(
                child: OutlinedButton(
                  onPressed: _loading || _loadingMore ? null : () => _load(),
                  child: Text(_loadingMore ? 'Đang tải…' : 'Tải thêm lịch sử'),
                ),
              ),
            ),
          const SliverToBoxAdapter(child: SizedBox(height: 20)),
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
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                Text(
                  moodEmoji(entry.moodName),
                  style: const TextStyle(fontSize: 25),
                ),
                const SizedBox(width: 10),
                Expanded(
                  child: Text(
                    entry.moodName.isEmpty
                        ? 'Tâm trạng #${entry.moodId}'
                        : moodName(entry.moodName),
                    style: Theme.of(context).textTheme.titleMedium,
                  ),
                ),
                Text(
                  timeOfDay(entry.createdAt),
                  style: Theme.of(context).textTheme.bodySmall,
                ),
              ],
            ),
            const SizedBox(height: 8),
            Text(
              moodDescription(entry.valence, entry.arousal),
              style: Theme.of(context).textTheme.bodySmall,
            ),
            const SizedBox(height: 8),
            if (recommendations == null)
              const Text('Bản ghi cũ chưa lưu danh sách nội dung được gợi ý.')
            else if (recommendations.isEmpty)
              const Text('Lần yêu cầu này không có nội dung phù hợp.')
            else
              ExpansionTile(
                // Each entry must store its bool separately from the scroll offset.
                key: PageStorageKey('mood-history-entry-${entry.id}'),
                tilePadding: EdgeInsets.zero,
                childrenPadding: EdgeInsets.zero,
                title: Text(
                  '${recommendations.length} nội dung được gợi ý',
                  style: Theme.of(context).textTheme.bodyMedium,
                ),
                children: [
                  for (final item in recommendations)
                    ListTile(
                      contentPadding: EdgeInsets.zero,
                      leading: ContentArtwork(
                        url: item.imageUrl,
                        music: item.isMusic,
                        width: 38,
                        height: 48,
                        radius: 7,
                      ),
                      title: Text(
                        item.title,
                        maxLines: 2,
                        overflow: TextOverflow.ellipsis,
                      ),
                      subtitle: Text(item.isMusic ? 'Âm nhạc' : 'Phim'),
                      trailing: const Icon(Icons.chevron_right, size: 18),
                      onTap: () => Navigator.of(context).push(
                        MaterialPageRoute<void>(
                          builder: (_) => ContentDetailScreen(
                            contentId: item.id,
                            type: item.type,
                            recommendationId:
                                entry.recommendationId ?? item.recommendationId,
                          ),
                        ),
                      ),
                    ),
                ],
              ),
          ],
        ),
      ),
    );
  }
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
      (const Color(0xFF71D7D2), (MoodHistoryEntry entry) => entry.arousal),
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
