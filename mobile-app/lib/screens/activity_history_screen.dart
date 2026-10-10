import 'package:flutter/material.dart';

import '../core/api_service.dart';
import 'content_detail_screen.dart';

class ActivityHistoryScreen extends StatefulWidget {
  const ActivityHistoryScreen({super.key});
  @override
  State<ActivityHistoryScreen> createState() => _ActivityHistoryScreenState();
}

class _ActivityHistoryScreenState extends State<ActivityHistoryScreen> {
  final List<Map<String, dynamic>> _items = [];
  bool _loading = false;
  String? _error;
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
      final page = reset ? 1 : _page + 1;
      final data = await ApiService.interactions(page);
      if (!mounted) return;
      setState(() {
        if (reset) _items.clear();
        _items.addAll(
          (data['items'] as List).map((x) => Map<String, dynamic>.from(x)),
        );
        _page = page;
        _total = (data['total'] as num).toInt();
      });
    } on ApiException catch (e) {
      if (mounted) setState(() => _error = e.message);
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: const Text('Lịch sử tương tác'),
      actions: [
        IconButton(
          onPressed: _loading ? null : () => _load(reset: true),
          icon: const Icon(Icons.refresh),
        ),
      ],
    ),
    body: ListView(
      padding: const EdgeInsets.all(16),
      children: [
        const Text(
          'Ghi nhận thao tác mở nội dung. Tải player hoặc mở liên kết không có nghĩa đã nghe/xem hết.',
        ),
        if (_error != null)
          TextButton(
            onPressed: () => _load(),
            child: Text('$_error · Thử lại'),
          ),
        if (!_loading && _items.isEmpty && _error == null)
          const Padding(
            padding: EdgeInsets.all(20),
            child: Text('Chưa có tương tác được ghi nhận.'),
          ),
        for (final item in _items)
          ListTile(
            leading: item['imageUrl'] is String
                ? Image.network(
                    item['imageUrl'] as String,
                    width: 44,
                    height: 60,
                    fit: BoxFit.cover,
                    errorBuilder: (_, _, _) => const Icon(Icons.movie),
                  )
                : const Icon(Icons.history),
            title: Text(
              item['title'] as String? ??
                  item['contentId'] as String? ??
                  'Nội dung không còn khả dụng',
            ),
            subtitle: Text(
              '${_actions[item['action']] ?? item['action']}\n${DateTime.tryParse(item['createdAt'] as String? ?? '')?.toLocal() ?? ''}',
            ),
            onTap: item['contentId'] == null
                ? null
                : () => Navigator.of(context).push(
                    MaterialPageRoute<void>(
                      builder: (_) => ContentDetailScreen(
                        contentId: item['contentId'] as String,
                        type: item['contentType'] == 'Song' ? 'Music' : 'Movie',
                        recommendationId: item['recommendationId'] as String?,
                      ),
                    ),
                  ),
          ),
        if (_loading) const Center(child: CircularProgressIndicator()),
        if (!_loading && _items.length < _total)
          TextButton(onPressed: () => _load(), child: const Text('Tải thêm')),
      ],
    ),
  );
}
