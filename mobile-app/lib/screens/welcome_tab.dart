import 'package:flutter/material.dart';

import '../core/api_service.dart';
import '../models/mobile_models.dart';
import '../models/session_user.dart';
import 'content_detail_screen.dart';

class WelcomeTab extends StatefulWidget {
  const WelcomeTab({super.key, required this.user, required this.onDiscover});
  final SessionUser user;
  final VoidCallback onDiscover;
  @override
  State<WelcomeTab> createState() => _WelcomeTabState();
}

class _WelcomeTabState extends State<WelcomeTab> {
  Mood? _mood;
  RecommendationResult? _result;
  bool _busy = false, _quizOpen = false;
  String? _error;
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (mounted) _quiz();
    });
  }

  Future<void> _quiz() async {
    if (_quizOpen || _busy) return;
    _quizOpen = true;
    final mood = await showDialog<Mood>(
      context: context,
      builder: (_) => const _MoodQuiz(),
    );
    _quizOpen = false;
    if (!mounted || mood == null) return;
    setState(() {
      _mood = mood;
      _result = null;
    });
    await _recommend();
  }

  Future<void> _recommend() async {
    if (_busy || _mood == null) return;
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      final result = await ApiService.recommend(
        _mood!.id,
        null,
        limit: 6,
        balanced: true,
      );
      if (mounted) setState(() => _result = result);
    } on ApiException catch (e) {
      if (mounted) setState(() => _error = e.message);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => ListView(
    padding: const EdgeInsets.all(22),
    children: [
      Text(
        'Xin chào, ${widget.user.fullName.isEmpty ? widget.user.email : widget.user.fullName}',
        style: Theme.of(context).textTheme.titleLarge,
      ),
      const SizedBox(height: 24),
      Text(
        _mood == null
            ? 'Hôm nay bạn thế nào?'
            : 'Dành cho tâm trạng ${_mood!.name}',
        style: Theme.of(context).textTheme.headlineSmall,
      ),
      const SizedBox(height: 10),
      Align(
        alignment: Alignment.centerLeft,
        child: FilledButton.tonalIcon(
          onPressed: _busy ? null : _quiz,
          icon: const Icon(Icons.mood),
          label: Text(_mood == null ? 'Chọn cảm xúc của bạn' : 'Đổi tâm trạng'),
        ),
      ),
      if (_busy)
        const Padding(
          padding: EdgeInsets.all(24),
          child: Center(child: CircularProgressIndicator()),
        ),
      if (_error != null) ...[
        Text(_error!),
        TextButton(onPressed: _recommend, child: const Text('Thử lại gợi ý')),
      ],
      if (_result != null) ...[
        _shelf('Nhạc cho bạn', true),
        _shelf('Phim cho bạn', false),
      ],
      const SizedBox(height: 20),
      const Text('Chưa diễn tả hết cảm xúc của bạn?'),
      const SizedBox(height: 8),
      OutlinedButton.icon(
        onPressed: widget.onDiscover,
        icon: const Icon(Icons.explore_outlined),
        label: const Text('Khám phá chi tiết tâm trạng'),
      ),
    ],
  );
  Widget _shelf(String title, bool music) {
    final items = _result!.items
        .where((x) => x.content.isMusic == music)
        .take(3)
        .toList();
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        const SizedBox(height: 22),
        Text(title, style: Theme.of(context).textTheme.titleLarge),
        const SizedBox(height: 12),
        if (items.isEmpty)
          const Text('Chưa có nội dung phù hợp.')
        else
          SizedBox(
            height: 245,
            child: ListView.separated(
              scrollDirection: Axis.horizontal,
              itemCount: items.length,
              separatorBuilder: (_, _) => const SizedBox(width: 12),
              itemBuilder: (context, index) {
                final item = items[index].content;
                return SizedBox(
                  width: 148,
                  child: Card(
                    clipBehavior: Clip.antiAlias,
                    child: InkWell(
                      onTap: () => Navigator.of(context).push(
                        MaterialPageRoute<void>(
                          builder: (_) => ContentDetailScreen(
                            contentId: item.id,
                            type: item.type,
                            recommendationId: item.recommendationId,
                          ),
                        ),
                      ),
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          SizedBox(
                            height: 145,
                            width: double.infinity,
                            child: item.imageUrl == null
                                ? Icon(music ? Icons.music_note : Icons.movie)
                                : Image.network(
                                    item.imageUrl!,
                                    fit: BoxFit.cover,
                                    errorBuilder: (_, _, _) => Icon(
                                      music ? Icons.music_note : Icons.movie,
                                    ),
                                  ),
                          ),
                          Padding(
                            padding: const EdgeInsets.all(8),
                            child: Column(
                              crossAxisAlignment: CrossAxisAlignment.start,
                              children: [
                                Text(
                                  item.title,
                                  maxLines: 2,
                                  overflow: TextOverflow.ellipsis,
                                  style: const TextStyle(
                                    fontWeight: FontWeight.w700,
                                  ),
                                ),
                                if ((music && item.artist != null) ||
                                    (!music && item.director != null))
                                  Text(
                                    music
                                        ? item.artist!
                                        : 'ĐD: ${item.director}',
                                    maxLines: 1,
                                    overflow: TextOverflow.ellipsis,
                                    style: const TextStyle(fontSize: 12),
                                  ),
                              ],
                            ),
                          ),
                        ],
                      ),
                    ),
                  ),
                );
              },
            ),
          ),
      ],
    );
  }
}

class _MoodQuiz extends StatefulWidget {
  const _MoodQuiz();
  @override
  State<_MoodQuiz> createState() => _MoodQuizState();
}

class _MoodQuizState extends State<_MoodQuiz> {
  late Future<List<Mood>> _moods = ApiService.moods();
  String _emoji(Mood m) {
    final n = m.name.toLowerCase();
    if (n.contains('angry')) return '😠';
    if (n.contains('sad')) return '😢';
    if (n.contains('stress') || n.contains('anxious')) return '😟';
    if (n.contains('relax') || n.contains('calm')) return '😌';
    if (n.contains('happy')) return '😊';
    if (n.contains('excit')) return '🤩';
    if (m.valence < -.2) return m.arousal > .2 ? '😟' : '😔';
    if (m.valence > .2) return m.arousal > .2 ? '🤩' : '😌';
    return '😐';
  }

  @override
  Widget build(BuildContext context) => AlertDialog(
    title: const Text('Hôm nay bạn thế nào?'),
    content: SizedBox(
      width: 360,
      child: FutureBuilder<List<Mood>>(
        future: _moods,
        builder: (context, snapshot) {
          if (snapshot.connectionState != ConnectionState.done) {
            return const SizedBox(
              height: 90,
              child: Center(child: CircularProgressIndicator()),
            );
          }
          if (snapshot.hasError) {
            return Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                const Text('Chưa tải được tâm trạng.'),
                TextButton(
                  onPressed: () => setState(() => _moods = ApiService.moods()),
                  child: const Text('Thử lại'),
                ),
              ],
            );
          }
          final moods = snapshot.data ?? [];
          if (moods.isEmpty) return const Text('Chưa có tâm trạng để chọn.');
          return SingleChildScrollView(
            child: Wrap(
              spacing: 8,
              runSpacing: 8,
              children: moods
                  .map(
                    (m) => ActionChip(
                      label: Text('${_emoji(m)}  ${m.name}'),
                      onPressed: () => Navigator.pop(context, m),
                    ),
                  )
                  .toList(),
            ),
          );
        },
      ),
    ),
    actions: [
      TextButton(
        onPressed: () => Navigator.pop(context),
        child: const Text('Để sau'),
      ),
    ],
  );
}
