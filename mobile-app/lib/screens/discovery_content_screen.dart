import 'package:flutter/material.dart';

import '../core/api_service.dart';
import '../core/presentation.dart';
import '../models/mobile_models.dart';
import '../widgets/app_widgets.dart';
import 'content_detail_screen.dart';

class DiscoveryContentScreen extends StatefulWidget {
  const DiscoveryContentScreen({super.key});
  @override
  State<DiscoveryContentScreen> createState() => _DiscoveryContentScreenState();
}

class _DiscoveryContentScreenState extends State<DiscoveryContentScreen> {
  String _mode = 'popular';
  String? _type;
  List<_DiscoveryEntry> _items = const [];
  bool _loading = true;
  String? _error;
  int _generation = 0;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load({bool clear = false}) async {
    final generation = ++_generation;
    final mode = _mode;
    final type = _type;
    setState(() {
      _loading = true;
      _error = null;
      if (clear) _items = const [];
    });
    try {
      final response = await ApiService.request(
        '/Discovery?mode=$mode&limit=12${type == null ? '' : '&type=$type'}',
      );
      if (response is! Map || response['items'] is! List) {
        throw const FormatException('Invalid discovery response');
      }
      final items = (response['items'] as List)
          .map((raw) {
            if (raw is! Map || raw['content'] is! Map) {
              throw const FormatException('Invalid discovery content');
            }
            final content = ContentItem.fromJson(
              Map<String, dynamic>.from(raw['content'] as Map),
            );
            if (content.id.isEmpty || content.title.isEmpty) {
              throw const FormatException('Missing discovery content');
            }
            return _DiscoveryEntry(
              content,
              (raw['uniqueVisitors'] as num?)?.toInt(),
              raw['updatedAt'] as String?,
            );
          })
          .toList(growable: false);
      if (!mounted || generation != _generation) return;
      setState(() => _items = items);
    } on ApiException catch (error) {
      if (mounted && generation == _generation) {
        setState(() => _error = error.message);
      }
    } on FormatException {
      if (mounted && generation == _generation) {
        setState(() => _error = 'Chưa đọc được danh sách. Hãy thử lại.');
      }
    } finally {
      if (mounted && generation == _generation) {
        setState(() => _loading = false);
      }
    }
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(title: const Text('Nội dung nổi bật')),
    body: RefreshIndicator(
      onRefresh: _load,
      child: ListView.builder(
        key: const PageStorageKey('discovery-content-scroll'),
        physics: const AlwaysScrollableScrollPhysics(),
        padding: const EdgeInsets.fromLTRB(20, 0, 20, 28),
        itemCount: _items.length + 1,
        itemBuilder: (context, index) {
          if (index == 0) {
            return Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                const SectionHeading(
                  'Thêm một lựa chọn cho hôm nay',
                  subtitle: 'Khám phá phim và nhạc trong kho Moodify.',
                ),
                Wrap(
                  spacing: 8,
                  runSpacing: 8,
                  children: [
                    ChoiceChip(
                      label: const Text('Được quan tâm'),
                      selected: _mode == 'popular',
                      onSelected: (_) {
                        if (_mode == 'popular') return;
                        _mode = 'popular';
                        _load(clear: true);
                      },
                    ),
                    ChoiceChip(
                      label: const Text('Vừa cập nhật'),
                      selected: _mode == 'updated',
                      onSelected: (_) {
                        if (_mode == 'updated') return;
                        _mode = 'updated';
                        _load(clear: true);
                      },
                    ),
                  ],
                ),
                const SizedBox(height: 10),
                Wrap(
                  spacing: 8,
                  children: [
                    for (final option in const [
                      (null, 'Tất cả'),
                      ('Movie', 'Phim'),
                      ('Music', 'Nhạc'),
                    ])
                      FilterChip(
                        label: Text(option.$2),
                        selected: _type == option.$1,
                        onSelected: (_) {
                          if (_type == option.$1) return;
                          _type = option.$1;
                          _load(clear: true);
                        },
                      ),
                  ],
                ),
                const SizedBox(height: 10),
                Text(
                  _mode == 'popular'
                      ? 'Trong 7 ngày trên Moodify. Mỗi tài khoản mở một nội dung được tính một lần; không phải bảng xếp hạng toàn cầu.'
                      : 'Theo lần cập nhật gần nhất trong kho, không phải ngày phát hành.',
                  style: Theme.of(context).textTheme.bodySmall,
                ),
                const SizedBox(height: 16),
                if (_loading && _items.isEmpty) const LoadingCards(),
                if (_error != null)
                  StatusCard(message: _error!, onRetry: _load),
                if (!_loading && _items.isEmpty && _error == null)
                  StatusCard(
                    message: _mode == 'popular'
                        ? 'Chưa có lượt quan tâm trong 7 ngày. Bạn có thể xem mục Vừa cập nhật.'
                        : 'Chưa có nội dung cập nhật trong kho.',
                  ),
              ],
            );
          }
          final entry = _items[index - 1];
          return ContentListTile(
            item: entry.content,
            footnote: _mode == 'popular'
                ? '${entry.visitors ?? 0} tài khoản đã mở chi tiết'
                : 'Cập nhật ${shortDate(entry.updatedAt, year: true)}',
            onTap: () => Navigator.of(context).push(
              MaterialPageRoute<void>(
                builder: (_) => ContentDetailScreen(
                  contentId: entry.content.id,
                  type: entry.content.type,
                ),
              ),
            ),
          );
        },
      ),
    ),
  );
}

class _DiscoveryEntry {
  const _DiscoveryEntry(this.content, this.visitors, this.updatedAt);
  final ContentItem content;
  final int? visitors;
  final String? updatedAt;
}
