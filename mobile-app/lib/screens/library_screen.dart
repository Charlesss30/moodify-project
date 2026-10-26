import 'package:flutter/material.dart';

import '../core/api_service.dart';
import '../core/colors.dart';
import '../models/mobile_models.dart';
import '../widgets/app_widgets.dart';
import 'content_detail_screen.dart';
import 'saved_content_screen.dart';

class LibraryScreen extends StatefulWidget {
  const LibraryScreen({super.key, this.savedRequest = 0, this.active = true});
  final int savedRequest;
  final bool active;
  @override
  State<LibraryScreen> createState() => _LibraryScreenState();
}

class _LibraryScreenState extends State<LibraryScreen> {
  static const _batch = 20;
  final _searchController = TextEditingController();
  final Map<String, List<ContentItem>> _loaded = {};
  List<ContentItem> _catalog = [];
  String? _error;
  String _type = 'Movie', _genre = 'Tất cả', _query = '';
  int _visible = _batch;
  bool _loading = false;
  late bool _showSaved, _savedVisited;
  int _savedRefreshRequest = 0;

  @override
  void initState() {
    super.initState();
    _showSaved = _savedVisited = widget.savedRequest > 0;
    if (!_showSaved) {
      _load('Movie');
    }
  }

  @override
  void didUpdateWidget(covariant LibraryScreen oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (widget.savedRequest != oldWidget.savedRequest) {
      setState(() {
        _showSaved = _savedVisited = true;
      });
    }
  }

  void _selectCollection(bool saved) {
    setState(() {
      _showSaved = saved;
      if (saved) {
        _savedVisited = true;
      }
    });
    if (!saved && _catalog.isEmpty && !_loading) {
      _load(_type);
    }
  }

  @override
  void dispose() {
    _searchController.dispose();
    super.dispose();
  }

  Future<void> _load(String type, {bool refresh = false}) async {
    if (_loading) return;
    setState(() {
      if (_type != type) {
        _genre = 'Tất cả';
        _catalog = _loaded[type] ?? [];
      }
      _type = type;
      _loading = true;
      _error = null;
      _visible = _batch;
    });
    try {
      final items = !refresh && _loaded.containsKey(type)
          ? _loaded[type]!
          : await ApiService.catalog(type);
      if (!mounted) return;
      setState(() {
        _catalog = items.where((x) => x.id.isNotEmpty).toList();
        _loaded[type] = _catalog;
        if (!_catalog.any((x) => x.genres.contains(_genre))) _genre = 'Tất cả';
      });
    } on ApiException catch (e) {
      if (mounted) setState(() => _error = e.message);
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  void _open(ContentItem item) => Navigator.of(context).push(
    MaterialPageRoute<void>(
      builder: (_) => ContentDetailScreen(
        contentId: item.id,
        type: item.type,
        recommendationId: item.recommendationId,
      ),
    ),
  );

  @override
  Widget build(BuildContext context) {
    final query = _query.trim().toLowerCase();
    final filtered = _catalog
        .where(
          (x) =>
              (_genre == 'Tất cả' || x.genres.contains(_genre)) &&
              (query.isEmpty ||
                  '${x.title} ${x.artist ?? ''} ${x.director ?? ''} ${x.genre ?? ''}'
                      .toLowerCase()
                      .contains(query)),
        )
        .toList(growable: false);
    final genres = _catalog.expand((x) => x.genres).toSet().toList()..sort();
    final count = filtered.length < _visible ? filtered.length : _visible;
    return Scaffold(
      appBar: AppBar(
        title: const Text('Thư viện'),
        actions: [
          IconButton(
            tooltip: 'Tải lại thư viện',
            onPressed: _showSaved
                ? () => setState(() => _savedRefreshRequest++)
                : _loading
                ? null
                : () => _load(_type, refresh: true),
            icon: const Icon(Icons.refresh),
          ),
        ],
      ),
      body: Column(
        children: [
          Padding(
            padding: const EdgeInsets.fromLTRB(20, 0, 20, 16),
            child: SizedBox(
              width: double.infinity,
              child: SegmentedButton<bool>(
                segments: const [
                  ButtonSegment(
                    value: false,
                    label: Text('Kho nội dung'),
                    icon: Icon(Icons.grid_view_outlined),
                  ),
                  ButtonSegment(
                    value: true,
                    label: Text('Đã lưu'),
                    icon: Icon(Icons.bookmark_outline),
                  ),
                ],
                selected: {_showSaved},
                onSelectionChanged: (values) => _selectCollection(values.first),
              ),
            ),
          ),
          Expanded(
            child: IndexedStack(
              index: _showSaved ? 1 : 0,
              children: [
                RefreshIndicator(
                  onRefresh: () => _load(_type, refresh: true),
                  child: CustomScrollView(
                    key: const PageStorageKey('library-scroll'),
                    physics: const AlwaysScrollableScrollPhysics(),
                    keyboardDismissBehavior:
                        ScrollViewKeyboardDismissBehavior.onDrag,
                    slivers: [
                      SliverPadding(
                        padding: const EdgeInsets.fromLTRB(20, 0, 20, 14),
                        sliver: SliverToBoxAdapter(
                          child: Column(
                            crossAxisAlignment: CrossAxisAlignment.stretch,
                            children: [
                              Text(
                                'Tìm một bộ phim hay, một giai điệu hợp ý.',
                                style: Theme.of(context).textTheme.bodySmall,
                              ),
                              const SizedBox(height: 18),
                              SegmentedButton<String>(
                                segments: const [
                                  ButtonSegment(
                                    value: 'Movie',
                                    label: Text('Phim'),
                                    icon: Icon(Icons.movie_outlined),
                                  ),
                                  ButtonSegment(
                                    value: 'Music',
                                    label: Text('Nhạc'),
                                    icon: Icon(Icons.music_note),
                                  ),
                                ],
                                selected: {_type},
                                onSelectionChanged: _loading
                                    ? null
                                    : (v) => _load(v.first),
                              ),
                              const SizedBox(height: 16),
                              TextField(
                                controller: _searchController,
                                hintLocales: const [
                                  Locale('vi', 'VN'),
                                  Locale('en'),
                                ],
                                textInputAction: TextInputAction.search,
                                decoration: InputDecoration(
                                  labelText: 'Tìm trong thư viện',
                                  hintText: _type == 'Music'
                                      ? 'Tên bài hát, nghệ sĩ, thể loại'
                                      : 'Tên phim, đạo diễn, thể loại',
                                  prefixIcon: const Icon(Icons.search),
                                  suffixIcon: _query.isEmpty
                                      ? null
                                      : IconButton(
                                          tooltip: 'Xóa tìm kiếm',
                                          icon: const Icon(Icons.close),
                                          onPressed: () {
                                            _searchController.clear();
                                            setState(() => _query = '');
                                          },
                                        ),
                                ),
                                onChanged: (v) => setState(() {
                                  _query = v;
                                  _visible = _batch;
                                }),
                              ),
                              const SizedBox(height: 12),
                              if (genres.isNotEmpty)
                                DropdownButtonFormField<String>(
                                  key: ValueKey(_genre),
                                  initialValue: _genre,
                                  isExpanded: true,
                                  decoration: const InputDecoration(
                                    labelText: 'Thể loại',
                                  ),
                                  items: ['Tất cả', ...genres]
                                      .map(
                                        (g) => DropdownMenuItem(
                                          value: g,
                                          child: Text(
                                            g,
                                            maxLines: 1,
                                            overflow: TextOverflow.ellipsis,
                                          ),
                                        ),
                                      )
                                      .toList(),
                                  onChanged: (v) => setState(() {
                                    _genre = v ?? 'Tất cả';
                                    _visible = _batch;
                                  }),
                                ),
                              const SizedBox(height: 16),
                              if (_loading && _catalog.isNotEmpty)
                                const LinearProgressIndicator(),
                              if (_error != null)
                                StatusCard(
                                  message: _error!,
                                  icon: Icons.wifi_off_rounded,
                                  onRetry: () => _load(_type, refresh: true),
                                ),
                              if (!_loading && filtered.isNotEmpty)
                                Text(
                                  '${filtered.length} kết quả',
                                  style: Theme.of(context).textTheme.bodySmall,
                                ),
                            ],
                          ),
                        ),
                      ),
                      if (_loading && _catalog.isEmpty)
                        const SliverPadding(
                          padding: EdgeInsets.symmetric(horizontal: 20),
                          sliver: SliverToBoxAdapter(child: LoadingCards()),
                        )
                      else if (filtered.isEmpty)
                        const SliverPadding(
                          padding: EdgeInsets.symmetric(horizontal: 20),
                          sliver: SliverToBoxAdapter(
                            child: StatusCard(
                              icon: Icons.search_off,
                              title: 'Chưa tìm thấy nội dung',
                              message: 'Thử tên khác hoặc chọn lại thể loại.',
                            ),
                          ),
                        ),
                      if (_type == 'Music')
                        SliverPadding(
                          padding: const EdgeInsets.symmetric(horizontal: 20),
                          sliver: SliverList.builder(
                            itemCount: count,
                            itemBuilder: (_, i) => ContentListTile(
                              item: filtered[i],
                              onTap: () => _open(filtered[i]),
                            ),
                          ),
                        )
                      else
                        SliverPadding(
                          padding: const EdgeInsets.symmetric(horizontal: 20),
                          sliver: SliverLayoutBuilder(
                            builder: (context, constraints) {
                              final columns =
                                  (constraints.crossAxisExtent / 180)
                                      .floor()
                                      .clamp(2, 5);
                              final width =
                                  (constraints.crossAxisExtent -
                                      (columns - 1) * 14) /
                                  columns;
                              final height =
                                  width * 1.5 +
                                  MediaQuery.textScalerOf(context).scale(86);
                              return SliverGrid.builder(
                                itemCount: count,
                                gridDelegate:
                                    SliverGridDelegateWithFixedCrossAxisCount(
                                      crossAxisCount: columns,
                                      mainAxisExtent: height,
                                      crossAxisSpacing: 14,
                                      mainAxisSpacing: 14,
                                    ),
                                itemBuilder: (context, i) {
                                  final item = filtered[i];
                                  return Material(
                                    color: Colors.transparent,
                                    child: InkWell(
                                      borderRadius: BorderRadius.circular(14),
                                      onTap: () => _open(item),
                                      child: Column(
                                        crossAxisAlignment:
                                            CrossAxisAlignment.start,
                                        children: [
                                          ContentArtwork(
                                            url: item.imageUrl,
                                            width: width,
                                            height: width * 1.5,
                                          ),
                                          const SizedBox(height: 9),
                                          Text(
                                            item.title,
                                            maxLines: 2,
                                            overflow: TextOverflow.ellipsis,
                                            style: Theme.of(context)
                                                .textTheme
                                                .titleSmall,
                                          ),
                                          const SizedBox(height: 3),
                                          Text(
                                            [
                                              if (item.rating != null)
                                                '★ ${item.rating}',
                                              if (item.genre != null)
                                                item.genre!,
                                            ].join(' · '),
                                            maxLines: 1,
                                            overflow: TextOverflow.ellipsis,
                                            style: const TextStyle(
                                              color: AppColors.textSecondary,
                                              fontSize: 12,
                                            ),
                                          ),
                                        ],
                                      ),
                                    ),
                                  );
                                },
                              );
                            },
                          ),
                        ),
                      if (count < filtered.length)
                        SliverPadding(
                          padding: const EdgeInsets.all(20),
                          sliver: SliverToBoxAdapter(
                            child: OutlinedButton(
                              onPressed: () =>
                                  setState(() => _visible += _batch),
                              child: const Text('Hiển thị thêm'),
                            ),
                          ),
                        ),
                      const SliverToBoxAdapter(child: SizedBox(height: 28)),
                    ],
                  ),
                ),
                _savedVisited
                    ? SavedContentScreen(
                        embedded: true,
                        active: widget.active && _showSaved,
                        refreshRequest: _savedRefreshRequest,
                      )
                    : const SizedBox.shrink(),
              ],
            ),
          ),
        ],
      ),
    );
  }
}
