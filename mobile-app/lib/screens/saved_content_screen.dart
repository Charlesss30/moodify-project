import 'dart:async';

import 'package:flutter/material.dart';

import '../core/api_service.dart';
import '../core/presentation.dart';
import '../models/personal_models.dart';
import '../widgets/app_widgets.dart';
import 'content_detail_screen.dart';

class SavedContentScreen extends StatefulWidget {
  const SavedContentScreen({
    super.key,
    this.embedded = false,
    this.active = true,
    this.refreshRequest = 0,
  });
  final bool embedded, active;
  final int refreshRequest;
  @override
  State<SavedContentScreen> createState() => _SavedContentScreenState();
}

class _SavedContentScreenState extends State<SavedContentScreen> {
  final List<SavedEntry> _items = [];
  final Set<String> _removing = {};
  int _page = 0, _total = 0, _generation = 0;
  String? _type, _error;
  bool _loading = false, _retryReset = true, _dirty = false, _covered = false;

  @override
  void initState() {
    super.initState();
    ApiService.savedRevision.addListener(_invalidate);
    if (widget.active) {
      _load(reset: true);
    } else {
      _dirty = true;
    }
  }

  @override
  void didUpdateWidget(covariant SavedContentScreen oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (widget.refreshRequest != oldWidget.refreshRequest) {
      _invalidate();
    } else if (widget.active && !oldWidget.active && _dirty) {
      _load(reset: true);
    }
  }

  @override
  void dispose() {
    ApiService.savedRevision.removeListener(_invalidate);
    super.dispose();
  }

  void _invalidate() {
    _dirty = true;
    if (widget.active && !_covered && _removing.isEmpty && !_loading) {
      unawaited(_load(reset: true));
    }
  }

  Future<void> _load({bool reset = false}) async {
    if (!reset && (_loading || _items.length >= _total)) {
      return;
    }
    final generation = reset ? ++_generation : _generation;
    _dirty = false;
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final result = await ApiService.savedContents(
        reset ? 1 : _page + 1,
        type: _type,
      );
      if (!mounted || generation != _generation) {
        return;
      }
      setState(() {
        if (reset) {
          _items.clear();
        }
        _items.addAll(result.items);
        _page = result.page;
        _total = result.total;
      });
    } on ApiException catch (error) {
      if (mounted && generation == _generation) {
        setState(() {
          _error = error.message;
          _retryReset = reset;
        });
      }
    } finally {
      if (mounted && generation == _generation) {
        setState(() => _loading = false);
        if (_dirty &&
            _error == null &&
            widget.active &&
            !_covered &&
            _removing.isEmpty) {
          unawaited(_load(reset: true));
        }
      }
    }
  }

  void _filter(String value) {
    setState(() {
      _type = value == 'Both' ? null : value;
      _items.clear();
      _page = _total = 0;
    });
    _load(reset: true);
  }

  Future<void> _open(SavedEntry entry) async {
    _covered = true;
    await Navigator.of(context).push(
      MaterialPageRoute<void>(
        builder: (_) => ContentDetailScreen(
          contentId: entry.content.id,
          type: entry.content.type,
        ),
      ),
    );
    _covered = false;
    if (mounted && _dirty) {
      await _load(reset: true);
    }
  }

  Future<void> _remove(SavedEntry entry) async {
    final id = entry.content.id;
    if (_removing.contains(id)) {
      return;
    }
    setState(() => _removing.add(id));
    var removed = false;
    try {
      await ApiService.setSaved(id, false);
      if (!mounted) {
        return;
      }
      setState(() {
        _items.removeWhere((x) => x.content.id == id);
        _total = (_total - 1).clamp(0, _total);
      });
      removed = true;
    } on ApiException catch (error) {
      if (mounted) {
        ScaffoldMessenger.of(context)
            .showSnackBar(SnackBar(content: Text(error.message)));
      }
    } finally {
      if (mounted) {
        setState(() => _removing.remove(id));
      }
    }
    // Rebase offset pagination after removal so later pages cannot skip an item.
    if (removed && mounted) {
      await _load(reset: true);
    }
  }

  @override
  Widget build(BuildContext context) {
    final body = RefreshIndicator(
      onRefresh: () => _load(reset: true),
      child: CustomScrollView(
        key: const PageStorageKey('saved-content-scroll'),
        physics: const AlwaysScrollableScrollPhysics(),
        slivers: [
          SliverPadding(
            padding: const EdgeInsets.fromLTRB(20, 0, 20, 16),
            sliver: SliverToBoxAdapter(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  Text(
                    'Giữ lại những bộ phim và bài hát bạn muốn quay về.',
                    style: Theme.of(context).textTheme.bodySmall,
                  ),
                  const SizedBox(height: 16),
                  SegmentedButton<String>(
                    segments: const [
                      ButtonSegment(value: 'Both', label: Text('Tất cả')),
                      ButtonSegment(value: 'Movie', label: Text('Phim')),
                      ButtonSegment(value: 'Music', label: Text('Nhạc')),
                    ],
                    selected: {_type ?? 'Both'},
                    onSelectionChanged: _removing.isEmpty
                        ? (values) => _filter(values.first)
                        : null,
                  ),
                  const SizedBox(height: 16),
                  if (_total > 0) Text('$_total nội dung đã lưu'),
                  if (_loading && _items.isNotEmpty)
                    const LinearProgressIndicator(),
                  if (_loading && _items.isEmpty) const LoadingCards(),
                  if (_error != null)
                    StatusCard(
                      message: _error!,
                      onRetry: () => _load(reset: _retryReset),
                    ),
                  if (!_loading && _items.isEmpty && _error == null)
                    const StatusCard(
                      icon: Icons.bookmark_border,
                      title: 'Chưa có nội dung đã lưu',
                      message: 'Mở chi tiết phim hoặc bài hát, nhấn “Lưu để xem sau” hoặc “Lưu để nghe sau” để thêm vào đây.',
                    ),
                ],
              ),
            ),
          ),
          SliverPadding(
            padding: const EdgeInsets.symmetric(horizontal: 20),
            sliver: SliverList.builder(
              itemCount: _items.length,
              itemBuilder: (context, index) {
                final entry = _items[index];
                final item = entry.content;
                return Card(
                  key: ValueKey(item.id),
                  child: ListTile(
                    contentPadding: const EdgeInsets.fromLTRB(12, 8, 4, 8),
                    leading: ContentArtwork(
                      url: item.imageUrl,
                      music: item.isMusic,
                      width: 48,
                      height: item.isMusic ? 48 : 66,
                      radius: 8,
                    ),
                    title: Text(
                      item.title,
                      maxLines: 2,
                      overflow: TextOverflow.ellipsis,
                      style: Theme.of(context).textTheme.titleSmall,
                    ),
                    subtitle: Text(
                      entry.available
                          ? '${item.isMusic ? 'Âm nhạc' : 'Phim'} · Đã lưu ${shortDate(entry.createdAt?.toIso8601String())}'
                          : 'Nội dung hiện không còn khả dụng.',
                      style: Theme.of(context).textTheme.bodySmall,
                    ),
                    onTap: entry.available ? () => _open(entry) : null,
                    trailing: IconButton(
                      tooltip: 'Bỏ lưu ${item.title}',
                      onPressed: _removing.contains(item.id)
                          ? null
                          : () => _remove(entry),
                      icon: _removing.contains(item.id)
                          ? const SizedBox(
                              width: 20,
                              height: 20,
                              child: CircularProgressIndicator(strokeWidth: 2),
                            )
                          : const Icon(Icons.bookmark_remove_outlined),
                    ),
                  ),
                );
              },
            ),
          ),
          if (_items.length < _total)
            SliverPadding(
              padding: const EdgeInsets.all(20),
              sliver: SliverToBoxAdapter(
                child: OutlinedButton(
                  onPressed: _loading ? null : () => _load(),
                  child: Text(_loading ? 'Đang tải…' : 'Tải thêm mục đã lưu'),
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
            appBar: AppBar(title: const Text('Nội dung đã lưu')),
            body: body,
          );
  }
}
