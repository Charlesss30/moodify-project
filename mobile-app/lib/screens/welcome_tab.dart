import 'package:flutter/material.dart';

import '../widgets/moodify_mark.dart';

import '../core/api_service.dart';
import '../core/colors.dart';
import '../core/presentation.dart';
import '../widgets/app_widgets.dart';
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
  static const _dailyQuotes = <String>[
    'Bạn không cần phải ổn ngay lập tức. Cứ đi từng bước nhỏ thôi.',
    'Một ngày bình thường vẫn có thể bắt đầu cho điều tốt đẹp.',
    'Nghỉ ngơi cũng là một cách để tiếp tục tiến về phía trước.',
    'Bạn đã đi qua nhiều ngày khó khăn hơn bạn nghĩ.',
    'Hôm nay, hãy dành cho mình một chút dịu dàng.',
    'Không cần hoàn hảo; chỉ cần thành thật với cảm xúc của mình.',
    'Một giai điệu hay đôi khi đủ để mở ra một khởi đầu mới.',
    'Cảm xúc nào rồi cũng đổi thay. Hãy cho mình thời gian.',
    'Đi chậm một chút vẫn là đang đi.',
    'Bạn xứng đáng có những phút giây bình yên cho riêng mình.',
    'Một câu chuyện hay có thể khiến hôm nay nhẹ nhàng hơn.',
    'Hãy chọn điều khiến lòng bạn thấy dễ chịu hơn hôm qua.',
  ];

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
        limit: 8,
        balanced: true,
      );
      if (mounted) setState(() => _result = result);
    } on ApiException catch (e) {
      if (mounted) setState(() => _error = e.message);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  String get _dailyQuote {
    final now = DateTime.now();
    final today = DateTime.utc(now.year, now.month, now.day);
    final dayNumber = today.difference(DateTime.utc(2020, 1, 1)).inDays;
    return _dailyQuotes[dayNumber % _dailyQuotes.length];
  }

  @override
  Widget build(BuildContext context) => ListView(
    key: const PageStorageKey('home-scroll'),
    padding: const EdgeInsets.fromLTRB(20, 20, 20, 28),
    children: [
      EntryFade(
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                const MoodifyMark(size: 38),
                const SizedBox(width: 10),
                const Text(
                  'MOODIFY',
                  style: TextStyle(
                    fontSize: 12,
                    fontWeight: FontWeight.w800,
                    letterSpacing: 2,
                  ),
                ),
              ],
            ),
            const SizedBox(height: 22),
            Text(
              'Xin chào, ${widget.user.fullName.isEmpty ? widget.user.email : widget.user.fullName}',
              style: Theme.of(context).textTheme.bodyMedium
                  ?.copyWith(color: AppColors.textSecondary),
            ),
            const SizedBox(height: 6),
            Text(
              'Một chút giải trí,\nđúng với cảm xúc bạn.',
              style: Theme.of(context).textTheme.headlineSmall,
            ),
          ],
        ),
      ),
      const SizedBox(height: 20),
      Container(
        padding: const EdgeInsets.all(18),
        decoration: BoxDecoration(
          borderRadius: BorderRadius.circular(20),
          gradient: const LinearGradient(
            colors: [Color(0xFF292038), Color(0xFF1B1B29)],
          ),
        ),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                Icon(
                  Icons.format_quote_rounded,
                  size: 20,
                  color: Theme.of(context).colorScheme.primary,
                ),
                const SizedBox(width: 8),
                Expanded(
                  child: Text(
                    'Một lời nhắn dành cho hôm nay',
                    style: Theme.of(context).textTheme.labelMedium,
                  ),
                ),
              ],
            ),
            const SizedBox(height: 10),
            Text(_dailyQuote, style: Theme.of(context).textTheme.bodyMedium),
          ],
        ),
      ),
      const SizedBox(height: 24),
      SectionHeading(
        _mood == null
            ? 'Hôm nay bạn thế nào?'
            : 'Dành cho tâm trạng ${moodName(_mood!.name).toLowerCase()}',
        subtitle: _mood == null
            ? 'Chọn cảm xúc để tìm nhạc và phim dành cho bạn.'
            : 'Một vài lựa chọn để bạn bắt đầu.',
      ),
      Align(
        alignment: Alignment.centerLeft,
        child: FilledButton.tonalIcon(
          onPressed: _busy ? null : _quiz,
          icon: _mood == null
              ? const Icon(Icons.mood)
              : Text(
                  moodEmoji(_mood!.name),
                  style: const TextStyle(fontSize: 20),
                ),
          label: Text(_mood == null ? 'Chọn cảm xúc của bạn' : 'Đổi tâm trạng'),
        ),
      ),
      const SizedBox(height: 16),
      if (_busy) const LoadingCards(count: 2),
      if (_error != null)
        StatusCard(
          title: 'Chưa tải được gợi ý',
          message: _error!,
          onRetry: _recommend,
          actionLabel: 'Thử lại gợi ý',
          icon: Icons.wifi_off_rounded,
        ),
      if (_result != null) ...[
        _shelf('Nhạc cho bạn', true),
        _shelf('Phim cho bạn', false),
      ],
      const SizedBox(height: 20),
      Card(
        child: Padding(
          padding: const EdgeInsets.all(18),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              const Icon(
                Icons.explore_outlined,
                color: AppColors.accentPrimary,
              ),
              const SizedBox(height: 10),
              Text(
                'Muốn hiểu rõ cảm xúc hơn?',
                style: Theme.of(context).textTheme.titleMedium,
              ),
              const SizedBox(height: 6),
              Text(
                'Viết vài dòng về ngày của bạn để khám phá những gợi ý khác.',
                style: Theme.of(context).textTheme.bodySmall,
              ),
              const SizedBox(height: 12),
              OutlinedButton.icon(
                onPressed: widget.onDiscover,
                icon: const Icon(Icons.arrow_forward_rounded),
                label: const Text('Khám phá chi tiết tâm trạng'),
              ),
            ],
          ),
        ),
      ),
    ],
  );

  Widget _shelf(String title, bool music) {
    final items = _result!.items
        .where((x) => x.content.isMusic == music)
        .take(2)
        .toList();
    final width = ((MediaQuery.sizeOf(context).width - 52) / 2).clamp(
      132.0,
      176.0,
    );
    final imageHeight = music ? width : width * 1.5;
    return Padding(
      padding: const EdgeInsets.only(top: 24),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          SectionHeading(title),
          if (items.isEmpty)
            const StatusCard(message: 'Chưa có nội dung phù hợp.')
          else
            SizedBox(
              height:
                  imageHeight + MediaQuery.textScalerOf(context).scale(68) + 12,
              child: ListView.separated(
                scrollDirection: Axis.horizontal,
                itemCount: items.length,
                separatorBuilder: (_, _) => const SizedBox(width: 12),
                itemBuilder: (context, index) {
                  final item = items[index].content;
                  return SizedBox(
                    width: width,
                    child: Material(
                      color: Colors.transparent,
                      child: InkWell(
                        borderRadius: BorderRadius.circular(14),
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
                            ContentArtwork(
                              url: item.imageUrl,
                              music: music,
                              width: width,
                              height: imageHeight,
                            ),
                            const SizedBox(height: 10),
                            Text(
                              item.title,
                              maxLines: 2,
                              overflow: TextOverflow.ellipsis,
                              style: Theme.of(context).textTheme.titleSmall,
                            ),
                            if ((music ? item.artist : item.director) != null)
                              Text(
                                music ? item.artist! : 'ĐD: ${item.director}',
                                maxLines: 1,
                                overflow: TextOverflow.ellipsis,
                                style: Theme.of(context).textTheme.bodySmall,
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
      ),
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

  @override
  Widget build(BuildContext context) => AlertDialog(
    title: const Text('Hôm nay bạn thế nào?'),
    scrollable: true,
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
                      label: Text('${moodEmoji(m.name)}  ${moodName(m.name)}'),
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
