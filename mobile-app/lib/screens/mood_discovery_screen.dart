import 'package:flutter/material.dart';

import '../core/colors.dart';
import '../core/api_service.dart';
import '../models/mobile_models.dart';
import '../services/mood_analysis/mood_analyzer.dart';
import 'content_detail_screen.dart';

class MoodDiscoveryScreen extends StatefulWidget {
  const MoodDiscoveryScreen({super.key});

  @override
  State<MoodDiscoveryScreen> createState() => _MoodDiscoveryScreenState();
}

class _MoodDiscoveryScreenState extends State<MoodDiscoveryScreen> {
  final _descriptionController = TextEditingController();
  List<Mood> _moods = [];
  Mood? _selectedMood;
  RecommendationResult? _recommendation;
  MoodAnalysisResult? _analysis;
  String? _recommendError;
  String? _moodError;
  int _recommendationType = 0;
  bool _loadingMoods = true;
  bool _requestingRecommendations = false;
  bool _analyzing = false;
  String? _analysisError;
  int _analysisRequestVersion = 0;

  @override
  void initState() {
    super.initState();
    _loadMoods();
  }

  @override
  void dispose() {
    _descriptionController.dispose();
    super.dispose();
  }

  Future<void> _loadMoods() async {
    setState(() {
      _loadingMoods = true;
      _moodError = null;
    });
    try {
      final moods = await ApiService.moods();
      if (!mounted) return;
      setState(() {
        _moods = moods.where((mood) => mood.name.isNotEmpty).toList();
        if (_selectedMood != null &&
            !_moods.any((mood) => mood.id == _selectedMood!.id)) {
          _selectedMood = null;
        }
      });
    } on ApiException catch (error) {
      if (mounted) setState(() => _moodError = error.message);
    } finally {
      if (mounted) setState(() => _loadingMoods = false);
    }
  }

  Future<void> _analyzeDescription() async {
    if (_analyzing || _loadingMoods) return;
    final requestVersion = ++_analysisRequestVersion;
    setState(() {
      _analyzing = true;
      _analysisError = null;
      _analysis = null;
      _selectedMood = null;
      _recommendation = null;
    });
    try {
      final text = _descriptionController.text.trim();
      if (text.isEmpty) {
        setState(() => _analysisError = 'Hãy nhập mô tả tâm trạng trước.');
        return;
      }
      // Conservative guard for Vietnamese diacritics; not a general language detector.
      if (RegExp(
        r'[ăâđêôơư\u1EA0-\u1EF9]',
        caseSensitive: false,
      ).hasMatch(text)) {
        setState(
          () => _analysisError = 'Model AI hiện chưa hỗ trợ tiếng Việt. Hãy nhập tiếng Anh hoặc chọn tâm trạng bên dưới.',
        );
        return;
      }
      final rawResult = await ApiService.analyzeMood(text, language: 'en');
      if (!mounted || requestVersion != _analysisRequestVersion) return;
      final mappedMood = rawResult.moodId == null
          ? null
          : _moods.where((mood) => mood.id == rawResult.moodId).firstOrNull;
      final result =
          rawResult.source == MoodAnalysisSource.ai && mappedMood != null
          ? rawResult.withMappedMood(mappedMood)
          : rawResult;
      setState(() {
        _analysis = result;
        if (mappedMood != null) {
          _selectedMood = mappedMood;
        } else if (result.source != MoodAnalysisSource.ai) {
          _selectedMood = null;
        }
      });
    } on ApiException catch (error) {
      if (mounted && requestVersion == _analysisRequestVersion) {
        setState(() {
          _analysisError = error.message;
          _analysis = null;
        });
      }
    } finally {
      if (mounted && requestVersion == _analysisRequestVersion) {
        setState(() => _analyzing = false);
      }
    }
  }

  Future<void> _requestRecommendations() async {
    final mood = _selectedMood;
    if (_requestingRecommendations || mood == null) return;
    setState(() {
      _requestingRecommendations = true;
      _recommendError = null;
      _recommendation = null;
    });
    try {
      final type = _recommendationType == 1
          ? 'Movie'
          : _recommendationType == 2
          ? 'Music'
          : null;
      final result = await ApiService.recommend(
        mood.id,
        type,
        source:
            _analysis?.source == MoodAnalysisSource.ruleBased &&
                _analysis?.moodId == mood.id
            ? 'rule_based'
            : 'manual',
        analysisId: _analysis?.source == MoodAnalysisSource.ai
            ? _analysis?.analysisId
            : null,
      );
      if (mounted) setState(() => _recommendation = result);
    } on ApiException catch (error) {
      if (mounted) setState(() => _recommendError = error.message);
    } finally {
      if (mounted) setState(() => _requestingRecommendations = false);
    }
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: const Text('Khám phá'),
      actions: [
        IconButton(
          tooltip: 'Tải lại tâm trạng',
          onPressed: _loadingMoods || _requestingRecommendations
              ? null
              : _loadMoods,
          icon: const Icon(Icons.refresh),
        ),
      ],
    ),
    body: _buildRecommendations(context),
  );

  Widget _buildRecommendations(BuildContext context) => ListView(
    padding: const EdgeInsets.fromLTRB(18, 0, 18, 24),
    children: [
      const Text(
        'Chọn nhanh một tâm trạng hoặc mô tả ngắn để nhận gợi ý.',
        style: TextStyle(color: AppColors.textSecondary),
      ),
      const SizedBox(height: 16),
      TextField(
        controller: _descriptionController,
        hintLocales: const [Locale('vi', 'VN'), Locale('en')],
        keyboardType: TextInputType.multiline,
        enableSuggestions: true,
        enabled: !_requestingRecommendations,
        minLines: 1,
        maxLines: 3,
        maxLength: 500,
        textCapitalization: TextCapitalization.sentences,
        decoration: const InputDecoration(
          labelText: 'Mô tả tâm trạng',
          hintText: 'Ví dụ: I feel happy today',
          helperText: 'AI hiện hỗ trợ mô tả bằng tiếng Anh.',
          prefixIcon: Icon(Icons.edit_note),
        ),
        onChanged: (_) {
          _analysisRequestVersion++;
          if (_analysis != null ||
              _analysisError != null ||
              _analyzing ||
              _recommendation != null) {
            setState(() {
              if (_analysis != null) {
                _analysis = null;
                _selectedMood = null;
              }
              _recommendation = null;
              _analysisError = null;
              _analyzing = false;
            });
          }
        },
      ),
      Align(
        alignment: Alignment.centerRight,
        child: OutlinedButton.icon(
          onPressed: _requestingRecommendations || _loadingMoods || _analyzing
              ? null
              : _analyzeDescription,
          icon: _analyzing
              ? const SizedBox.square(
                  dimension: 16,
                  child: CircularProgressIndicator(strokeWidth: 2),
                )
              : const Icon(Icons.auto_awesome, size: 18),
          label: const Text('Phân tích tâm trạng'),
        ),
      ),
      if (_analysisError != null)
        _InlineError(message: _analysisError!, onRetry: _analyzeDescription),
      if (_analysis != null) _AnalysisCard(result: _analysis!),
      const SizedBox(height: 10),
      if (_loadingMoods)
        const Center(
          child: Padding(
            padding: EdgeInsets.all(20),
            child: CircularProgressIndicator(),
          ),
        )
      else if (_moodError != null)
        _InlineError(message: _moodError!, onRetry: _loadMoods)
      else if (_moods.isEmpty)
        const _EmptyMessage(message: 'Máy chủ chưa có tâm trạng để lựa chọn.')
      else ...[
        DropdownButtonFormField<int>(
          key: ValueKey(_selectedMood?.id),
          initialValue: _selectedMood?.id,
          decoration: const InputDecoration(labelText: 'Chọn tâm trạng'),
          items: _moods
              .map(
                (mood) =>
                    DropdownMenuItem(value: mood.id, child: Text(mood.name)),
              )
              .toList(),
          onChanged: _requestingRecommendations
              ? null
              : (id) => setState(() {
                  _selectedMood = _moods
                      .where((mood) => mood.id == id)
                      .firstOrNull;
                  if (_analyzing) {
                    _analysisRequestVersion++;
                    _analyzing = false;
                    _analysisError = null;
                  }
                  _recommendation = null;
                }),
        ),
        const SizedBox(height: 16),
        Text('Nội dung', style: Theme.of(context).textTheme.titleSmall),
        const SizedBox(height: 8),
        SegmentedButton<int>(
          segments: const [
            ButtonSegment(value: 0, label: Text('Cả hai')),
            ButtonSegment(
              value: 1,
              label: Text('Phim'),
              icon: Icon(Icons.movie_outlined),
            ),
            ButtonSegment(
              value: 2,
              label: Text('Nhạc'),
              icon: Icon(Icons.music_note),
            ),
          ],
          selected: {_recommendationType},
          onSelectionChanged: _requestingRecommendations
              ? null
              : (values) => setState(() {
                  _recommendationType = values.first;
                  if (_analysis?.source == MoodAnalysisSource.ai) {
                    _analysisRequestVersion++;
                    _analysis = null;
                    _selectedMood = null;
                    _analysisError = 'Đã đổi loại nội dung. Hãy phân tích lại hoặc chọn tâm trạng thủ công.';
                  }
                  _recommendation = null;
                  _recommendError = null;
                }),
        ),
        const SizedBox(height: 14),
        FilledButton.icon(
          onPressed: _selectedMood == null || _requestingRecommendations
              ? null
              : _requestRecommendations,
          icon: _requestingRecommendations
              ? const SizedBox.square(
                  dimension: 18,
                  child: CircularProgressIndicator(strokeWidth: 2),
                )
              : const Icon(Icons.search),
          label: Text(
            _requestingRecommendations
                ? 'Đang tìm gợi ý…'
                : 'Xác nhận và nhận gợi ý',
          ),
        ),
        if (_recommendError != null) ...[
          const SizedBox(height: 12),
          _InlineError(
            message: _recommendError!,
            onRetry: _requestRecommendations,
          ),
        ],
        if (_recommendation != null) ...[
          const SizedBox(height: 22),
          Text(
            'Gợi ý cho ${_recommendation!.moodLabel}',
            style: Theme.of(context).textTheme.titleLarge
                ?.copyWith(fontWeight: FontWeight.w700),
          ),
          const SizedBox(height: 4),
          Text(
            'Gợi ý theo tâm trạng và phản hồi · ${_recommendation!.algorithm}',
            style: const TextStyle(
              color: AppColors.textSecondary,
              fontSize: 12,
            ),
          ),
          if (_recommendation!.items.isEmpty)
            const _EmptyMessage(
              message: 'Chưa có nội dung phù hợp. Thử đổi tâm trạng hoặc loại nội dung.',
            ),
          ..._recommendation!.items.map(
            (item) => _RecommendationCard(
              item: item,
              onOpen: () => _openDetails(item.content),
            ),
          ),
        ],
      ],
    ],
  );

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

class _AnalysisCard extends StatelessWidget {
  const _AnalysisCard({required this.result});
  final MoodAnalysisResult result;

  @override
  Widget build(BuildContext context) {
    final candidateLabels = result.candidates
        .take(3)
        .map((candidate) => candidate.label)
        .join(', ');
    final eligibleLabels = result.decisionCandidates
        .map((candidate) => candidate.label)
        .join(', ');
    final icon = switch (result.status) {
      MoodAnalysisStatus.matched => Icons.lightbulb_outline,
      MoodAnalysisStatus.ambiguous => Icons.help_outline,
      MoodAnalysisStatus.unknown => Icons.search_off,
      MoodAnalysisStatus.unavailable => Icons.cloud_off_outlined,
      MoodAnalysisStatus.unsupportedLanguage => Icons.translate,
    };
    return Card(
      color: AppColors.surfaceSecondary,
      child: ListTile(
        leading: Icon(icon, color: AppColors.accentPrimary),
        title: Text(
          result.status == MoodAnalysisStatus.matched
              ? 'Có thể bạn đang ${result.label.toLowerCase()}'
              : result.label,
        ),
        subtitle: Text(
          result.source == MoodAnalysisSource.ai
              ? '${result.reason}\nModel thử nghiệm ${result.analyzerVersion}; top hiển thị: ${candidateLabels.isEmpty ? 'không có' : candidateLabels}. Nhãn đủ ngưỡng: ${eligibleLabels.isEmpty ? 'không có' : eligibleLabels}. Điểm không phải phần trăm chính xác.'
              : '${result.reason}\nBộ từ khóa ${result.analyzerVersion}; không dùng AI.',
        ),
        isThreeLine: true,
      ),
    );
  }
}

class _RecommendationCard extends StatelessWidget {
  const _RecommendationCard({required this.item, required this.onOpen});
  final RecommendationItem item;
  final VoidCallback onOpen;

  @override
  Widget build(BuildContext context) => Card(
    clipBehavior: Clip.antiAlias,
    margin: const EdgeInsets.only(top: 12),
    child: InkWell(
      onTap: onOpen,
      child: Padding(
        padding: const EdgeInsets.all(12),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                _Artwork(
                  url: item.content.imageUrl,
                  type: item.content.type,
                  width: 78,
                  height: 104,
                ),
                const SizedBox(width: 12),
                Expanded(child: _ContentMetadata(item: item.content)),
              ],
            ),
            const SizedBox(height: 10),
            Row(
              children: [
                const Icon(
                  Icons.favorite_outline,
                  size: 16,
                  color: AppColors.accentPrimary,
                ),
                const SizedBox(width: 6),
                Text(
                  'Khớp tâm trạng ${item.match.round()}%',
                  style: const TextStyle(fontSize: 12),
                ),
              ],
            ),
          ],
        ),
      ),
    ),
  );
}

class _ContentMetadata extends StatelessWidget {
  const _ContentMetadata({required this.item});
  final ContentItem item;

  @override
  Widget build(BuildContext context) => Column(
    crossAxisAlignment: CrossAxisAlignment.start,
    children: [
      Text(
        item.title,
        maxLines: 3,
        overflow: TextOverflow.ellipsis,
        style: Theme.of(context).textTheme.titleMedium
            ?.copyWith(fontWeight: FontWeight.w700),
      ),
      if (item.artist != null) ...[
        const SizedBox(height: 4),
        Text(
          item.artist!,
          style: const TextStyle(color: AppColors.textSecondary),
        ),
      ],
      if (item.genre != null) ...[
        const SizedBox(height: 4),
        Text(
          item.genre!,
          style: const TextStyle(color: AppColors.textSecondary, fontSize: 12),
        ),
      ],
      if (item.rating != null) ...[
        const SizedBox(height: 6),
        Text(
          '★ ${item.rating}',
          style: const TextStyle(color: AppColors.warning),
        ),
      ],
      if (item.description != null) ...[
        const SizedBox(height: 8),
        Text(
          item.description!,
          maxLines: 3,
          overflow: TextOverflow.ellipsis,
          style: const TextStyle(fontSize: 12, color: AppColors.textSecondary),
        ),
      ],
    ],
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
