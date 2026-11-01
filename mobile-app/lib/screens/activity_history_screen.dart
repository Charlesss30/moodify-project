import 'package:flutter/material.dart';

import '../core/api_service.dart';
import '../core/presentation.dart';
import '../widgets/app_widgets.dart';
import 'content_detail_screen.dart';

class ActivityHistoryScreen extends StatefulWidget {
  const ActivityHistoryScreen({super.key, this.embedded = false});
  final bool embedded;
  @override
  State<ActivityHistoryScreen> createState() => _ActivityHistoryScreenState();
}

class _ActivityHistoryScreenState extends State<ActivityHistoryScreen> {
  final List<Map<String, dynamic>> _items = [];
  bool _loading = false;
  String? _error;
  bool _retryReset = false;
  int _page = 0, _total = 0;
  static const _actions = {
    'detail_open': 'Mở chi tiết',
    'player_loaded': 'Tải Spotify player',
    'external_open': 'Mở liên kết ngoài',
    'trailer_open': 'Mở trailer',
  };
  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load({bool reset = false}) async {
    if (_loading) return;
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final data = await ApiService.interactions(reset ? 1 : _page + 1);
      if (!mounted) return;
      setState(() {
        if (reset) _items.clear();
        _items.addAll(
          (data['items'] as List).map((x) => Map<String, dynamic>.from(x)),
        );
        _page = (data['page'] as num?)?.toInt() ?? (reset ? 1 : _page + 1);
        _total = (data['total'] as num).toInt();
      });
    } on ApiException catch (e) {
      if (mounted) {
        setState(() {
          _error = e.message;
          _retryReset = reset;
        });
      }
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final body = RefreshIndicator(
      onRefresh: () => _load(reset: true),
      child: CustomScrollView(
        key: const PageStorageKey('activity-history-scroll'),
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
                          'Hoạt động xem, nghe',
                          style: Theme.of(context).textTheme.titleMedium,
                        ),
                      ),
                      IconButton(
                        tooltip: 'Tải lại hoạt động',
                        onPressed: _loading ? null : () => _load(reset: true),
                        icon: const Icon(Icons.refresh),
                      ),
                    ],
                  ),
                  Text(
                    'Lưu thao tác mở nội dung. Mở player hoặc liên kết không có nghĩa đã nghe/xem hết.',
                    style: Theme.of(context).textTheme.bodySmall,
                  ),
                  const SizedBox(height: 16),
                  if (_error != null)
                    StatusCard(
                      message: _error!,
                      onRetry: () => _load(reset: _retryReset),
                    ),
                  if (_loading && _items.isEmpty) const LoadingCards(),
                  if (_loading && _items.isNotEmpty)
                    const LinearProgressIndicator(),
                  if (!_loading && _items.isEmpty && _error == null)
                    const StatusCard(
                      icon: Icons.touch_app_outlined,
                      title: 'Chưa có hoạt động',
                      message: 'Nội dung bạn mở từ gợi ý hoặc thư viện sẽ xuất hiện ở đây.',
                    ),
                ],
              ),
            ),
          ),
          SliverPadding(
            padding: const EdgeInsets.symmetric(horizontal: 20),
            sliver: SliverList.builder(
              itemCount: _items.length,
              itemBuilder: (context, i) {
                final item = _items[i];
                final date = shortDate(
                  item['createdAt'] as String?,
                  year: true,
                );
                final previous = i == 0
                    ? null
                    : shortDate(
                        _items[i - 1]['createdAt'] as String?,
                        year: true,
                      );
                final time = DateTime.tryParse(
                  item['createdAt'] as String? ?? '',
                )?.toLocal();
                return Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    if (date != previous)
                      Padding(
                        padding: const EdgeInsets.only(bottom: 10, top: 12),
                        child: Text(
                          date,
                          style: Theme.of(context).textTheme.labelLarge,
                        ),
                      ),
                    Card(
                      child: ListTile(
                        contentPadding: const EdgeInsets.symmetric(
                          horizontal: 12,
                          vertical: 8,
                        ),
                        leading: ContentArtwork(
                          url: item['imageUrl'] as String?,
                          music: item['contentType'] == 'Song',
                          width: 48,
                          height: 60,
                          radius: 8,
                        ),
                        title: Text(
                          item['title'] as String? ??
                              item['contentId'] as String? ??
                              'Nội dung không còn khả dụng',
                          maxLines: 2,
                          overflow: TextOverflow.ellipsis,
                        ),
                        subtitle: Text(
                          '${_actions[item['action']] ?? item['action']} · ${timeOfDay(time)}',
                        ),
                        trailing: const Icon(Icons.chevron_right, size: 18),
                        onTap: item['contentId'] == null
                            ? null
                            : () => Navigator.of(context).push(
                                MaterialPageRoute<void>(
                                  builder: (_) => ContentDetailScreen(
                                    contentId: item['contentId'] as String,
                                    type: item['contentType'] == 'Song'
                                        ? 'Music'
                                        : 'Movie',
                                    recommendationId:
                                        item['recommendationId'] as String?,
                                  ),
                                ),
                              ),
                      ),
                    ),
                  ],
                );
              },
            ),
          ),
          if (!_loading && _items.length < _total)
            SliverPadding(
              padding: const EdgeInsets.all(20),
              sliver: SliverToBoxAdapter(
                child: OutlinedButton(
                  onPressed: () => _load(),
                  child: const Text('Tải thêm'),
                ),
              ),
            ),
          const SliverToBoxAdapter(child: SizedBox(height: 24)),
        ],
      ),
    );
    return widget.embedded
        ? body
        : Scaffold(
            appBar: AppBar(title: const Text('Lịch sử tương tác')),
            body: body,
          );
  }
}
