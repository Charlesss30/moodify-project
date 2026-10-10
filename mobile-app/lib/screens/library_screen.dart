import 'package:flutter/material.dart';

import '../core/colors.dart';
import '../core/api_service.dart';
import '../models/mobile_models.dart';
import 'content_detail_screen.dart';

class LibraryScreen extends StatefulWidget {
  const LibraryScreen({super.key});
  @override
  State<LibraryScreen> createState() => _LibraryScreenState();
}

class _LibraryScreenState extends State<LibraryScreen> {
  static const _catalogBatchSize = 20;
  final _searchController = TextEditingController();
  List<ContentItem> _catalog = [];
  String? _catalogError;
  String? _catalogType;
  String _catalogGenre = 'Tất cả';
  String _searchQuery = '';
  int _visibleCatalogCount = _catalogBatchSize;
  bool _loadingCatalog = false;
  @override
  void initState() {
    super.initState();
    _loadCatalog('Movie');
  }

  @override
  void dispose() {
    _searchController.dispose();
    super.dispose();
  }

  Future<void> _loadCatalog(String type) async {
    if (_loadingCatalog) return;
    setState(() {
      _catalogType = type;
      _loadingCatalog = true;
      _catalogError = null;
      _catalog = [];
      _catalogGenre = 'Tất cả';
      _visibleCatalogCount = _catalogBatchSize;
    });
    try {
      final items = await ApiService.catalog(type);
      if (mounted) {
        setState(
          () => _catalog = items.where((item) => item.id.isNotEmpty).toList(),
        );
      }
    } on ApiException catch (error) {
      if (mounted) setState(() => _catalogError = error.message);
    } finally {
      if (mounted) setState(() => _loadingCatalog = false);
    }
  }

  List<ContentItem> get _filteredCatalog {
    final query = _searchQuery.trim().toLowerCase();
    return _catalog
        .where((item) {
          final matchesGenre =
              _catalogGenre == 'Tất cả' || item.genres.contains(_catalogGenre);
          final searchable =
              '${item.title} ${item.artist ?? ''} ${item.genre ?? ''}'
                  .toLowerCase();
          return matchesGenre && (query.isEmpty || searchable.contains(query));
        })
        .toList(growable: false);
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: const Text('Thư viện'),
      actions: [
        IconButton(
          tooltip: 'Tải lại thư viện',
          onPressed: _loadingCatalog
              ? null
              : () => _loadCatalog(_catalogType ?? 'Movie'),
          icon: const Icon(Icons.refresh),
        ),
      ],
    ),
    body: _buildCatalog(context),
  );
  Widget _buildCatalog(BuildContext context) {
    final filtered = _filteredCatalog;
    final visible = filtered.take(_visibleCatalogCount);
    final genres = _catalog.expand((item) => item.genres).toSet().toList()
      ..sort();
    return ListView(
      padding: const EdgeInsets.fromLTRB(18, 0, 18, 24),
      children: [
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
          selected: {_catalogType ?? 'Movie'},
          onSelectionChanged: _loadingCatalog
              ? null
              : (values) => _loadCatalog(values.first),
        ),
        const SizedBox(height: 12),
        TextField(
          controller: _searchController,
          hintLocales: const [Locale('vi', 'VN'), Locale('en')],
          textInputAction: TextInputAction.search,
          decoration: InputDecoration(
            labelText: 'Tìm trong thư viện',
            prefixIcon: const Icon(Icons.search),
            suffixIcon: _searchQuery.isEmpty
                ? null
                : IconButton(
                    tooltip: 'Xóa tìm kiếm',
                    onPressed: () {
                      _searchController.clear();
                      setState(() => _searchQuery = '');
                    },
                    icon: const Icon(Icons.close),
                  ),
          ),
          onChanged: (value) => setState(() {
            _searchQuery = value;
            _visibleCatalogCount = _catalogBatchSize;
          }),
        ),
        const SizedBox(height: 12),
        if (!_loadingCatalog && _catalogError == null && genres.isNotEmpty)
          DropdownButtonFormField<String>(
            key: ValueKey(_catalogGenre),
            initialValue: _catalogGenre,
            decoration: const InputDecoration(labelText: 'Thể loại'),
            items: ['Tất cả', ...genres]
                .map(
                  (genre) => DropdownMenuItem(value: genre, child: Text(genre)),
                )
                .toList(),
            onChanged: (genre) => setState(() {
              _catalogGenre = genre ?? 'Tất cả';
              _visibleCatalogCount = _catalogBatchSize;
            }),
          ),
        if (_loadingCatalog)
          const Center(
            child: Padding(
              padding: EdgeInsets.all(28),
              child: CircularProgressIndicator(),
            ),
          )
        else if (_catalogError != null)
          _InlineError(
            message: _catalogError!,
            onRetry: () => _loadCatalog(_catalogType ?? 'Movie'),
          )
        else if (filtered.isEmpty)
          const _EmptyMessage(
            message: 'Không tìm thấy nội dung phù hợp trong thư viện.',
          )
        else ...[
          const SizedBox(height: 8),
          Text(
            '${filtered.length} kết quả',
            style: const TextStyle(
              color: AppColors.textSecondary,
              fontSize: 12,
            ),
          ),
          ...visible.map(
            (item) =>
                _CatalogCard(item: item, onOpen: () => _openDetails(item)),
          ),
          if (_visibleCatalogCount < filtered.length)
            OutlinedButton(
              onPressed: () =>
                  setState(() => _visibleCatalogCount += _catalogBatchSize),
              child: const Text('Hiển thị thêm'),
            ),
        ],
      ],
    );
  }

  void _openDetails(ContentItem item) {
    Navigator.of(context).push(
      MaterialPageRoute<void>(
        builder: (_) => ContentDetailScreen(
          contentId: item.id,
          type: item.type,
          recommendationId: item.recommendationId,
        ),
      ),
    );
  }
}

class _CatalogCard extends StatelessWidget {
  const _CatalogCard({required this.item, required this.onOpen});
  final ContentItem item;
  final VoidCallback onOpen;

  @override
  Widget build(BuildContext context) => Card(
    margin: const EdgeInsets.only(top: 10),
    child: ListTile(
      onTap: onOpen,
      leading: _Artwork(
        url: item.imageUrl,
        type: item.type,
        width: 50,
        height: 62,
      ),
      title: Text(item.title, maxLines: 2, overflow: TextOverflow.ellipsis),
      subtitle: Text(
        item.isMusic
            ? [item.artist, item.genre]
                  .whereType<String>()
                  .where((value) => value.isNotEmpty)
                  .join(' · ')
            : [
                item.genre,
                if (item.rating != null) '★ ${item.rating}',
              ].whereType<String>().join(' · '),
      ),
      trailing: const Icon(Icons.chevron_right),
    ),
  );
}

class _Artwork extends StatelessWidget {
  const _Artwork({
    required this.url,
    required this.type,
    required this.width,
    required this.height,
  });
  final String? url;
  final String type;
  final double width;
  final double height;

  @override
  Widget build(BuildContext context) {
    final uri = url == null ? null : Uri.tryParse(url!);
    final valid =
        uri != null && (uri.scheme == 'https' || uri.scheme == 'http');
    return ClipRRect(
      borderRadius: BorderRadius.circular(10),
      child: SizedBox(
        width: width,
        height: height,
        child: valid
            ? Image.network(
                url!,
                fit: BoxFit.cover,
                errorBuilder: (_, _, _) => _fallback(),
              )
            : _fallback(),
      ),
    );
  }

  Widget _fallback() => ColoredBox(
    color: AppColors.surfaceSecondary,
    child: Icon(
      type == 'Music' ? Icons.music_note : Icons.movie_outlined,
      color: AppColors.textSecondary,
    ),
  );
}

class _InlineError extends StatelessWidget {
  const _InlineError({required this.message, required this.onRetry});
  final String message;
  final VoidCallback onRetry;

  @override
  Widget build(BuildContext context) => Card(
    child: ListTile(
      leading: const Icon(Icons.cloud_off, color: AppColors.warning),
      title: Text(message),
      trailing: IconButton(
        tooltip: 'Thử lại',
        onPressed: onRetry,
        icon: const Icon(Icons.refresh),
      ),
    ),
  );
}

class _EmptyMessage extends StatelessWidget {
  const _EmptyMessage({required this.message});
  final String message;

  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.symmetric(vertical: 24),
    child: Text(
      message,
      textAlign: TextAlign.center,
      style: const TextStyle(color: AppColors.textSecondary),
    ),
  );
}
