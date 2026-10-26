import 'package:flutter/material.dart';

import '../core/api_service.dart';
import '../core/colors.dart';
import '../core/presentation.dart';
import '../widgets/app_widgets.dart';
import '../widgets/daily_trend_chart.dart';

class StatisticsScreen extends StatefulWidget {
  const StatisticsScreen({super.key});
  @override
  State<StatisticsScreen> createState() => _StatisticsScreenState();
}

class _StatisticsScreenState extends State<StatisticsScreen> {
  int days = 30, generation = 0, _section = 0, _loadedDays = 30;
  int? _year, _loadedYear;
  Map<String, dynamic>? data;
  String? error;
  bool loading = true;
  @override
  void initState() {
    super.initState();
    load();
  }

  Future<void> load() async {
    final current = ++generation;
    setState(() {
      loading = true;
      error = null;
    });
    try {
      final result = await ApiService.request(
        '/Statistics?${_year == null ? 'days=$days' : 'year=$_year'}&offsetMinutes=${DateTime.now().timeZoneOffset.inMinutes}',
      );
      if (mounted && current == generation) {
        setState(() {
          data = Map<String, dynamic>.from(result as Map);
          _loadedDays = (result['days'] as num?)?.toInt() ?? days;
          _loadedYear = _year;
        });
      }
    } catch (e) {
      if (mounted && current == generation) {
        setState(() => error = e.toString());
      }
    } finally {
      if (mounted && current == generation) setState(() => loading = false);
    }
  }

  Widget _panel(String title, Widget child, {String? subtitle}) => Card(
    child: Padding(
      padding: const EdgeInsets.all(18),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(title, style: Theme.of(context).textTheme.titleMedium),
          if (subtitle != null) ...[
            const SizedBox(height: 4),
            Text(subtitle, style: Theme.of(context).textTheme.bodySmall),
          ],
          const SizedBox(height: 16),
          child,
        ],
      ),
    ),
  );
  Widget bars(
    String title,
    List<dynamic> rows, {
    String? subtitle,
    bool translateMood = false,
  }) {
    final max = rows.fold<double>(
      0,
      (v, r) => (r['count'] as num).toDouble() > v
          ? (r['count'] as num).toDouble()
          : v,
    );
    return _panel(
      title,
      Column(
        children: [
          if (max == 0)
            const StatusCard(message: 'Chưa có dữ liệu trong khoảng này.'),
          for (final r in rows.where((r) => (r['count'] as num) > 0))
            Padding(
              padding: const EdgeInsets.only(bottom: 14),
              child: Column(
                children: [
                  Row(
                    children: [
                      Expanded(
                        child: Text(
                          translateMood
                              ? moodName('${r['name']}')
                              : '${r['name']}',
                          style: Theme.of(context).textTheme.bodyMedium,
                        ),
                      ),
                      const SizedBox(width: 12),
                      Text(
                        '${r['count']}',
                        style: Theme.of(context).textTheme.labelLarge,
                      ),
                    ],
                  ),
                  const SizedBox(height: 8),
                  ClipRRect(
                    borderRadius: BorderRadius.circular(5),
                    child: LinearProgressIndicator(
                      value: (r['count'] as num) / max,
                      minHeight: 7,
                      color: const Color(0xFFB99CFF),
                      backgroundColor: AppColors.surfaceSecondary,
                    ),
                  ),
                ],
              ),
            ),
        ],
      ),
      subtitle: subtitle,
    );
  }

  Widget _metric(IconData icon, String value, String label) => Container(
    padding: const EdgeInsets.all(16),
    decoration: BoxDecoration(
      color: AppColors.surfaceSecondary,
      borderRadius: BorderRadius.circular(16),
    ),
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Icon(icon, size: 20, color: Theme.of(context).colorScheme.primary),
        const SizedBox(height: 12),
        Text(value, style: Theme.of(context).textTheme.titleLarge),
        const SizedBox(height: 3),
        Text(label, style: Theme.of(context).textTheme.bodySmall),
      ],
    ),
  );
  @override
  Widget build(BuildContext context) {
    final trend = data?['trend'] as List? ?? [];
    return Scaffold(
      appBar: AppBar(
        title: const Text('Thống kê cá nhân'),
        actions: [
          IconButton(
            tooltip: 'Tải lại thống kê',
            onPressed: loading ? null : load,
            icon: const Icon(Icons.refresh),
          ),
        ],
      ),
      body: RefreshIndicator(
        onRefresh: load,
        child: ListView(
          key: const PageStorageKey('statistics-scroll'),
          padding: const EdgeInsets.fromLTRB(20, 0, 20, 28),
          physics: const AlwaysScrollableScrollPhysics(),
          children: [
            const SectionHeading(
              'Nhìn lại hành trình của bạn',
              subtitle: 'Cảm xúc và những lựa chọn giải trí qua từng ngày.',
            ),
            Wrap(
              spacing: 8,
              runSpacing: 8,
              children: [
                for (final count in [7, 30, 90])
                  ChoiceChip(
                    label: Text('$count ngày'),
                    selected: _year == null && days == count,
                    onSelected: (_) {
                      days = count;
                      _year = null;
                      load();
                    },
                  ),
                ChoiceChip(
                  label: const Text('Theo năm'),
                  selected: _year != null,
                  onSelected: (_) {
                    _year ??= DateTime.now().year;
                    load();
                  },
                ),
              ],
            ),
            if (_year != null) ...[
              const SizedBox(height: 12),
              DropdownButtonFormField<int>(
                key: ValueKey('statistics-year-$_year'),
                initialValue: _year,
                decoration: const InputDecoration(
                  labelText: 'Năm thống kê',
                  prefixIcon: Icon(Icons.calendar_month_outlined),
                ),
                items: [
                  for (var value = DateTime.now().year; value >= 2000; value--)
                    DropdownMenuItem(value: value, child: Text('Năm $value')),
                ],
                onChanged: (value) {
                  if (value == null || value == _year) return;
                  _year = value;
                  load();
                },
              ),
            ],
            const SizedBox(height: 18),
            if (loading && data == null)
              const LoadingCards()
            else if (loading)
              const Padding(
                padding: EdgeInsets.only(bottom: 14),
                child: LinearProgressIndicator(),
              ),
            if (error != null)
              StatusCard(
                message: error!,
                onRetry: load,
                title: data == null
                    ? 'Chưa tải được thống kê'
                    : 'Đang hiển thị số liệu gần nhất',
              ),
            if (data != null) ...[
              if (_loadedYear != _year ||
                  (_year == null && _loadedDays != days))
                Padding(
                  padding: const EdgeInsets.only(bottom: 12),
                  child: Text(
                    _loadedYear != null
                        ? 'Số liệu gần nhất: năm $_loadedYear.'
                        : 'Số liệu gần nhất: $_loadedDays ngày.',
                    style: Theme.of(context).textTheme.bodySmall,
                  ),
                ),
              Text(
                '${data!['summary']['moodEntries']} lượt ghi nhận tâm trạng',
                style: Theme.of(context).textTheme.headlineSmall,
              ),
              const SizedBox(height: 14),
              LayoutBuilder(
                builder: (context, constraints) {
                  final width = (constraints.maxWidth - 12) / 2;
                  final summary = data!['summary'];
                  return Wrap(
                    spacing: 12,
                    runSpacing: 12,
                    children: [
                      SizedBox(
                        width: width,
                        child: _metric(
                          Icons.touch_app_outlined,
                          '${summary['interactions']}',
                          'Tương tác',
                        ),
                      ),
                      SizedBox(
                        width: width,
                        child: _metric(
                          Icons.calendar_today_outlined,
                          '${summary['activeDays']}',
                          'Ngày hoạt động',
                        ),
                      ),
                      SizedBox(
                        width: width,
                        child: _metric(
                          Icons.star_outline,
                          '${summary['ratings']}',
                          'Đánh giá',
                        ),
                      ),
                      SizedBox(
                        width: width,
                        child: _metric(
                          Icons.favorite_border,
                          (summary['averageStars'] as num?)?.toStringAsFixed(
                                1,
                              ) ??
                              '—',
                          'Điểm sao trung bình',
                        ),
                      ),
                    ],
                  );
                },
              ),
              const SizedBox(height: 14),
              Text(
                'Kỳ trước: ${data!['previous']['moodEntries']} lượt tâm trạng · ${data!['previous']['interactions']} tương tác',
                style: Theme.of(context).textTheme.bodySmall,
              ),
              const SizedBox(height: 6),
              Text(
                _loadedYear != null
                    ? (_loadedYear == DateTime.now().year
                          ? 'Năm nay tính đến hiện tại; so sánh cùng giai đoạn năm trước.'
                          : 'So sánh với năm trước đó. Mỗi điểm trên biểu đồ là một tháng.')
                    : 'Kỳ hiện tại gồm hôm nay chưa kết thúc; kỳ trước gồm các ngày đầy đủ.',
                style: Theme.of(context).textTheme.bodySmall,
              ),
              const SizedBox(height: 22),
              SegmentedButton<int>(
                segments: const [
                  ButtonSegment(value: 0, label: Text('Tâm trạng')),
                  ButtonSegment(value: 1, label: Text('Giải trí')),
                ],
                selected: {_section},
                onSelectionChanged: (v) => setState(() => _section = v.first),
              ),
              const SizedBox(height: 16),
              if (_section == 0) ...[
                _panel(
                  _loadedYear == null
                      ? 'Xu hướng tâm trạng theo ngày'
                      : 'Xu hướng tâm trạng năm $_loadedYear',
                  DailyTrendChart(
                    key: ValueKey('mood-$_loadedDays-$_loadedYear'),
                    monthly: _loadedYear != null,
                    rows: trend,
                  ),
                  subtitle: _loadedYear == null
                      ? 'Trung bình từ những lần tâm trạng được ghi nhận.'
                      : 'Mỗi tháng là trung bình của các bản ghi thực tế; tháng chưa có dữ liệu để trống.',
                ),
                bars(
                  'Phân bố tâm trạng',
                  data!['moods'] as List,
                  translateMood: true,
                  subtitle: 'Số lần ghi nhận mỗi tâm trạng trong kỳ.',
                ),
              ] else ...[
                _panel(
                  _loadedYear == null
                      ? 'Tương tác mỗi ngày'
                      : 'Tương tác mỗi tháng',
                  DailyTrendChart(
                    key: ValueKey('activity-$_loadedDays-$_loadedYear'),
                    monthly: _loadedYear != null,
                    rows: trend,
                    activity: true,
                  ),
                ),
                bars('Phim và âm nhạc', [
                  for (final r in data!['contentTypes'])
                    {
                      'name': r['name'] == 'Movie'
                          ? 'Phim'
                          : r['name'] == 'Song'
                          ? 'Âm nhạc'
                          : 'Không rõ',
                      'count': r['count'],
                    },
                ], subtitle: 'Số lượt mở nội dung theo loại.'),
                bars(
                  'Thể loại thường khám phá',
                  data!['genres'] as List,
                  subtitle: 'Một nội dung có thể thuộc nhiều thể loại; dùng nhãn catalog hiện tại.',
                ),
                bars('Khung giờ hoạt động', data!['hours'] as List),
              ],
              const SizedBox(height: 4),
              const StatusCard(
                icon: Icons.info_outline,
                message: 'Thống kê theo múi giờ thiết bị. Tương tác là thao tác mở nội dung, không phải thời lượng nghe/xem.',
              ),
            ],
          ],
        ),
      ),
    );
  }
}
