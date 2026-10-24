import 'package:flutter/material.dart';

import '../core/api_service.dart';
import '../core/presentation.dart';
import '../models/personal_models.dart';
import '../widgets/app_widgets.dart';
import 'content_detail_screen.dart';

class MyRatingsScreen extends StatefulWidget {
  const MyRatingsScreen({super.key});
  @override
  State<MyRatingsScreen> createState() => _MyRatingsScreenState();
}

class _MyRatingsScreenState extends State<MyRatingsScreen> {
  final List<PersonalRating> _items = [];
  int _page = 0, _total = 0;
  bool _loading = false, _retryReset = true;
  String? _error;
  @override
  void initState() {
    super.initState();
    _load(reset: true);
  }

  Future<void> _load({bool reset = false}) async {
    if (_loading || (!reset && _items.length >= _total)) {
      return;
    }
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final result = await ApiService.ownRatings(reset ? 1 : _page + 1);
      if (!mounted) {
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
      if (mounted) {
        setState(() {
          _error = error.message;
          _retryReset = reset;
        });
      }
    } finally {
      if (mounted) {
        setState(() => _loading = false);
      }
    }
  }

  Future<void> _open(PersonalRating rating) async {
    final revision = ApiService.ratingsRevision.value;
    await Navigator.of(context).push(
      MaterialPageRoute<void>(
        builder: (_) => ContentDetailScreen(
          contentId: rating.content.id,
          type: rating.content.type,
          recommendationId: rating.recommendationId,
        ),
      ),
    );
    if (mounted && revision != ApiService.ratingsRevision.value) {
      await _load(reset: true);
    }
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(title: const Text('Đánh giá của tôi')),
    body: RefreshIndicator(
      onRefresh: () => _load(reset: true),
      child: CustomScrollView(
        key: const PageStorageKey('my-ratings-scroll'),
        physics: const AlwaysScrollableScrollPhysics(),
        slivers: [
          SliverPadding(
            padding: const EdgeInsets.fromLTRB(20, 0, 20, 16),
            sliver: SliverToBoxAdapter(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    'Những cảm nhận bạn đã chia sẻ về phim và âm nhạc.',
                    style: Theme.of(context).textTheme.bodySmall,
                  ),
                  if (_total > 0)
                    Padding(
                      padding: const EdgeInsets.only(top: 8),
                      child: Text('$_total lượt đánh giá'),
                    ),
                  if (_loading && _items.isEmpty) const LoadingCards(),
                  if (_error != null)
                    StatusCard(
                      message: _error!,
                      onRetry: () => _load(reset: _retryReset),
                    ),
                  if (!_loading && _items.isEmpty && _error == null)
                    const StatusCard(
                      icon: Icons.rate_review_outlined,
                      title: 'Bạn chưa có đánh giá nào',
                      message: 'Sau khi trải nghiệm, mở chi tiết phim hoặc bài hát để chia sẻ cảm nhận.',
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
                final rating = _items[index];
                final item = rating.content;
                return Card(
                  clipBehavior: Clip.antiAlias,
                  child: InkWell(
                    onTap: rating.available ? () => _open(rating) : null,
                    child: Padding(
                      padding: const EdgeInsets.all(14),
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Row(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            children: [
                              ContentArtwork(
                                url: item.imageUrl,
                                music: item.isMusic,
                                width: 54,
                                height: item.isMusic ? 54 : 76,
                                radius: 9,
                              ),
                              const SizedBox(width: 12),
                              Expanded(
                                child: Column(
                                  crossAxisAlignment: CrossAxisAlignment.start,
                                  children: [
                                    Text(
                                      item.title,
                                      maxLines: 2,
                                      overflow: TextOverflow.ellipsis,
                                      style: Theme.of(context)
                                          .textTheme
                                          .titleSmall,
                                    ),
                                    const SizedBox(height: 4),
                                    Text(
                                      item.isMusic ? 'Âm nhạc' : 'Phim',
                                      style: Theme.of(context)
                                          .textTheme
                                          .bodySmall,
                                    ),
                                    Text(
                                      shortDate(
                                        rating.createdAt?.toIso8601String(),
                                        year: true,
                                      ),
                                      style: Theme.of(context)
                                          .textTheme
                                          .bodySmall,
                                    ),
                                  ],
                                ),
                              ),
                              if (rating.available)
                                const Icon(Icons.chevron_right, size: 20),
                            ],
                          ),
                          const SizedBox(height: 12),
                          Semantics(
                            label: '${rating.stars} trên 5 sao',
                            excludeSemantics: true,
                            child: Row(
                              children: List.generate(
                                5,
                                (star) => Icon(
                                  star < rating.stars
                                      ? Icons.star_rounded
                                      : Icons.star_outline_rounded,
                                  color: Theme.of(context).colorScheme.primary,
                                  size: 22,
                                ),
                              ),
                            ),
                          ),
                          if (rating.comment?.trim().isNotEmpty ?? false) ...[
                            const SizedBox(height: 8),
                            Text(
                              rating.comment!,
                              style: Theme.of(context).textTheme.bodyMedium,
                            ),
                          ],
                          if (!rating.available) ...[
                            const SizedBox(height: 8),
                            Text(
                              'Nội dung hiện không còn khả dụng.',
                              style: Theme.of(context).textTheme.bodySmall,
                            ),
                          ],
                        ],
                      ),
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
                  child: Text(_loading ? 'Đang tải…' : 'Tải thêm đánh giá'),
                ),
              ),
            ),
          const SliverToBoxAdapter(child: SizedBox(height: 24)),
        ],
      ),
    ),
  );
}
