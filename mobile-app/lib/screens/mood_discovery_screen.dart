import 'package:flutter/material.dart';

import '../core/presentation.dart';
import '../core/colors.dart';
import '../widgets/app_widgets.dart';
import '../core/api_service.dart';
import '../models/mobile_models.dart';
import '../services/mood_analysis/mood_analyzer.dart';
import '../services/mood_analysis/mood_language.dart';
import 'content_detail_screen.dart';
import 'discovery_content_screen.dart';

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
  int _recommendationRequestVersion = 0;
  ApiRequestCancellation? _analysisCancellation;
  String? _submittedAnalysisId;
  bool _recommendationNeedsAnalysis = false;
  String get _resolvedLanguage =>
      detectMoodLanguage(_descriptionController.text);

  String get _modelHint {
    if (_analysis?.status == MoodAnalysisStatus.unsupportedLanguage) {
      return 'Chưa hỗ trợ ngôn ngữ này.';
    }
    if (_analyzing) return 'Đang nhận diện ngôn ngữ và phân tích…';
    if (_descriptionController.text.trim().isEmpty) {
      return 'Tự nhận diện tiếng Việt / English.';
    }
    final language = _analysis?.language ?? _resolvedLanguage;
    final prefix = _analysis?.language == null ? 'Dự kiến' : 'Model';
    return language == 'vi'
        ? '$prefix: PhoBERT · Tiếng Việt'
        : '$prefix: RoBERTa VA · English';
  }

  void _invalidateAnalysis() {
    _analysisCancellation?.cancel();
    _analysisCancellation = null;
    _analysisRequestVersion++;
    _recommendationRequestVersion++;
    _requestingRecommendations = false;
    _submittedAnalysisId = null;
    _recommendationNeedsAnalysis = false;
    if (_analysis != null || _analyzing) _selectedMood = null;
    _analysis = null;
    _analysisError = null;
    _analyzing = false;
    _recommendation = null;
    _recommendError = null;
  }

  @override
  void initState() {
    super.initState();
    _loadMoods();
  }

  @override
  void dispose() {
    _analysisCancellation?.cancel();
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
    if (_analyzing || _loadingMoods || _requestingRecommendations) return;
    final requestVersion = ++_analysisRequestVersion;
    final cancellation = ApiRequestCancellation();
    _analysisCancellation?.cancel();
    _analysisCancellation = cancellation;
    setState(() {
      _analyzing = true;
      _analysisError = null;
      _analysis = null;
      _selectedMood = null;
      _recommendation = null;
      _recommendError = null;
      _recommendationNeedsAnalysis = false;
      _submittedAnalysisId = null;
    });
    try {
      final text = _descriptionController.text.trim();
      if (text.isEmpty) {
        setState(() => _analysisError = 'Hãy nhập mô tả tâm trạng trước.');
        return;
      }
      final rawResult = await ApiService.analyzeMood(
        text,
        cancellation: cancellation,
      );
      if (!mounted || requestVersion != _analysisRequestVersion) return;
      final mappedMood = rawResult.moodId == null
          ? null
          : _moods.where((mood) => mood.id == rawResult.moodId).firstOrNull;
      if (rawResult.moodId != null && mappedMood == null) {
        throw const ApiException(
          'Danh sách tâm trạng đã thay đổi. Hãy tải lại rồi phân tích lại.',
        );
      }
      setState(() {
        _analysis = rawResult;
        _selectedMood = mappedMood;
      });
    } on ApiRequestCancelled {
      // A newer description or manual choice owns the screen now.
    } on ApiException catch (error) {
      if (mounted && requestVersion == _analysisRequestVersion) {
        setState(() {
          _analysisError = error.statusCode == 503 || error.statusCode == 429
              ? 'Chưa thể phân tích lúc này. Bạn có thể thử lại hoặc chọn tâm trạng bên dưới.'
              : error.message;
          _analysis = null;
        });
      }
    } finally {
      if (mounted && requestVersion == _analysisRequestVersion) {
        _analysisCancellation = null;
        setState(() => _analyzing = false);
      }
    }
  }

  void _requireFreshAnalysis() {
    setState(() {
      _analysis = null;
      _selectedMood = null;
      _submittedAnalysisId = null;
      _recommendation = null;
      _recommendationNeedsAnalysis = true;
      _recommendError = 'Kết quả phân tích đã hết hiệu lực. Hãy phân tích lại hoặc tự chọn tâm trạng bên dưới.';
    });
  }

  Future<void> _requestRecommendations() async {
    final mood = _selectedMood;
    if (_requestingRecommendations || mood == null) return;
    final analysis = _analysis;
    final useAi =
        analysis?.source == MoodAnalysisSource.ai &&
        analysis?.usesVaModel == true &&
        analysis?.status == MoodAnalysisStatus.matched &&
        analysis?.moodId == mood.id;
    final expired =
        analysis?.expiresAt != null &&
        !analysis!.expiresAt!.isAfter(DateTime.now().toUtc());
    if (useAi && expired) {
      _requireFreshAnalysis();
      return;
    }
    final requestVersion = ++_recommendationRequestVersion;
    final originId = expired ? null : analysis?.analysisId;
    setState(() {
      _requestingRecommendations = true;
      _recommendError = null;
      _recommendationNeedsAnalysis = false;
      _recommendation = null;
      _submittedAnalysisId = originId;
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
        source: useAi ? 'ai_va' : 'manual',
        analysisId: originId,
      );
      if (!mounted || requestVersion != _recommendationRequestVersion) return;
      setState(() => _recommendation = result);
    } on ApiException catch (error) {
      if (!mounted || requestVersion != _recommendationRequestVersion) return;
      if (error.statusCode == 409 && originId != null) {
        _requireFreshAnalysis();
      } else {
        setState(() => _recommendError = error.message);
      }
    } finally {
      if (mounted && requestVersion == _recommendationRequestVersion) {
        setState(() => _requestingRecommendations = false);
      }
    }
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: const Text('Khám phá'),
      actions: [
        IconButton(
          tooltip: 'Nội dung nổi bật',
          icon: const Icon(Icons.trending_up_rounded),
          onPressed: () => Navigator.of(context).push(
            MaterialPageRoute<void>(
              builder: (_) => const DiscoveryContentScreen(),
            ),
          ),
        ),
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

  Widget _buildRecommendations(BuildContext context) => CustomScrollView(
    key: const PageStorageKey('discovery-scroll'),
    keyboardDismissBehavior: ScrollViewKeyboardDismissBehavior.onDrag,
    slivers: [
      SliverPadding(
        padding: const EdgeInsets.fromLTRB(20, 0, 20, 20),
        sliver: SliverToBoxAdapter(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              const SectionHeading(
                'Bắt đầu từ cảm xúc',
                subtitle:
                    'Mô tả ngày của bạn hoặc chọn một tâm trạng bên dưới.',
              ),
              Card(
                child: Padding(
                  padding: const EdgeInsets.all(16),
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      TextField(
                        controller: _descriptionController,
                        hintLocales: const [Locale('vi', 'VN'), Locale('en')],
                        keyboardType: TextInputType.multiline,
                        enableSuggestions: true,
                        enabled: !_requestingRecommendations,
                        minLines: 2,
                        maxLines: 4,
                        maxLength: 500,
                        textCapitalization: TextCapitalization.sentences,
                        decoration: InputDecoration(
                          labelText: 'Mô tả tâm trạng',
                          hintText: 'Hôm nay tôi thấy vui / I feel happy today',
                          helperText: _modelHint,
                          helperStyle: const TextStyle(
                            fontSize: 12,
                            color: AppColors.textSecondary,
                          ),
                          helperMaxLines: 2,
                          alignLabelWithHint: true,
                        ),
                        onChanged: (_) => setState(_invalidateAnalysis),
                      ),
                      const SizedBox(height: 8),
                      OutlinedButton.icon(
                        onPressed:
                            _requestingRecommendations ||
                                _loadingMoods ||
                                _analyzing
                            ? null
                            : _analyzeDescription,
                        icon: _analyzing
                            ? const SizedBox.square(
                                dimension: 16,
                                child: CircularProgressIndicator(
                                  strokeWidth: 2,
                                ),
                              )
                            : const Icon(Icons.auto_awesome, size: 18),
                        label: const Text('Phân tích tâm trạng'),
                      ),
                    ],
                  ),
                ),
              ),
              if (_analysisError != null)
                StatusCard(
                  message: _analysisError!,
                  onRetry: _analyzeDescription,
                  icon: Icons.info_outline,
                ),
              if (_analysis != null)
                EntryFade(child: _AnalysisCard(result: _analysis!)),
              const SizedBox(height: 8),
              if (_loadingMoods)
                const LoadingCards(count: 1)
              else if (_moodError != null)
                StatusCard(message: _moodError!, onRetry: _loadMoods)
              else if (_moods.isEmpty)
                const StatusCard(
                  message: 'Máy chủ chưa có tâm trạng để lựa chọn.',
                )
              else ...[
                Text(
                  'Hoặc chọn cảm xúc của bạn',
                  style: Theme.of(context).textTheme.titleMedium,
                ),
                const SizedBox(height: 12),
                DropdownButtonFormField<int>(
                  key: ValueKey(_selectedMood?.id),
                  initialValue: _selectedMood?.id,
                  isExpanded: true,
                  decoration: const InputDecoration(
                    labelText: 'Chọn tâm trạng',
                    prefixIcon: Icon(Icons.mood),
                  ),
                  items: _moods
                      .map(
                        (mood) => DropdownMenuItem(
                          value: mood.id,
                          child: Text(
                            '${moodEmoji(mood.name)}  ${moodName(mood.name)}',
                          ),
                        ),
                      )
                      .toList(),
                  onChanged: _requestingRecommendations
                      ? null
                      : (id) => setState(() {
                          _analysisCancellation?.cancel();
                          _analysisCancellation = null;
                          _analysisRequestVersion++;
                          _recommendationRequestVersion++;
                          _recommendationNeedsAnalysis = false;
                          _analyzing = false;
                          _analysisError = null;
                          _recommendation = null;
                          _recommendError = null;
                          _selectedMood = _moods
                              .where((mood) => mood.id == id)
                              .firstOrNull;
                          // Preserve the origin receipt while recording an explicit manual decision.
                          if (_selectedMood != null && _analysis != null) {
                            _analysis = _analysis!.withMood(_selectedMood!);
                          }
                        }),
                ),
                const SizedBox(height: 20),
                Text(
                  'Bạn muốn khám phá gì?',
                  style: Theme.of(context).textTheme.titleSmall,
                ),
                const SizedBox(height: 10),
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
                          if (_analysis?.analysisId != null &&
                              _submittedAnalysisId == _analysis?.analysisId) {
                            _invalidateAnalysis();
                            _analysisError = 'Đã đổi loại nội dung. Hãy phân tích lại hoặc chọn tâm trạng thủ công.';
                          }
                          _recommendation = null;
                          _recommendError = null;
                        }),
                ),
                const SizedBox(height: 16),
                FilledButton.icon(
                  onPressed: _selectedMood == null || _requestingRecommendations
                      ? null
                      : () {
                          FocusManager.instance.primaryFocus?.unfocus();
                          _requestRecommendations();
                        },
                  icon: _requestingRecommendations
                      ? const SizedBox.square(
                          dimension: 18,
                          child: CircularProgressIndicator(strokeWidth: 2),
                        )
                      : const Icon(Icons.auto_awesome_outlined),
                  label: Text(
                    _requestingRecommendations
                        ? 'Đang tìm gợi ý…'
                        : 'Xác nhận và nhận gợi ý',
                  ),
                ),
              ],
              if (_recommendError != null) ...[
                const SizedBox(height: 12),
                StatusCard(
                  message: _recommendError!,
                  onRetry: _recommendationNeedsAnalysis
                      ? _analyzeDescription
                      : _requestRecommendations,
                ),
              ],
              if (_requestingRecommendations)
                const Padding(
                  padding: EdgeInsets.only(top: 20),
                  child: LoadingCards(),
                ),
              if (_recommendation != null) ...[
                const SizedBox(height: 26),
                SectionHeading(
                  'Gợi ý cho ${moodName(_recommendation!.moodLabel).toLowerCase()}',
                  subtitle: 'Dựa trên tâm trạng và phản hồi của bạn.',
                ),
                if (_recommendation!.items.isEmpty)
                  const StatusCard(
                    message: 'Chưa có nội dung phù hợp. Thử đổi tâm trạng hoặc loại nội dung.',
                  ),
              ],
            ],
          ),
        ),
      ),
      if (_recommendation != null)
        SliverPadding(
          padding: const EdgeInsets.symmetric(horizontal: 20),
          sliver: SliverList.builder(
            itemCount: _recommendation!.items.length,
            itemBuilder: (_, i) {
              final item = _recommendation!.items[i];
              return ContentListTile(
                item: item.content,
                onTap: () => _openDetails(item.content),
                footnote: 'Khớp tâm trạng ${item.match.round()}%',
              );
            },
          ),
        ),
      const SliverToBoxAdapter(child: SizedBox(height: 28)),
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

  (String, String) get _copy {
    if (result.status == MoodAnalysisStatus.matched) {
      return (
        moodName(result.label),
        result.source == MoodAnalysisSource.manual
            ? 'Tiếp tục để nhận gợi ý phim và nhạc.'
            : 'Bạn có thể chọn lại nếu cảm xúc này chưa phù hợp.',
      );
    }
    if (result.status == MoodAnalysisStatus.unsupportedLanguage) {
      return (
        'Hãy dùng tiếng Việt hoặc Anh',
        'Bạn cũng có thể chọn tâm trạng bên dưới để tiếp tục.',
      );
    }
    if (result.status == MoodAnalysisStatus.unavailable) {
      return (
        'Chưa thể phân tích lúc này',
        'Bạn có thể thử lại sau hoặc tự chọn tâm trạng bên dưới.',
      );
    }
    return switch (result.reasonCode) {
      'low_confidence' => (
        'Bạn có thể tự chọn cảm xúc',
        'AI chưa đủ chắc chắn. Chọn tâm trạng bên dưới để tiếp tục.',
      ),
      'mixed_emotions' => (
        'Bạn đang có nhiều cảm xúc',
        'Chọn cảm xúc rõ nhất lúc này để nhận gợi ý phù hợp.',
      ),
      'unsupported_emotion' || 'mood_not_available' => (
        'Chưa có gợi ý phù hợp',
        'Bạn có thể chọn cảm xúc gần nhất với mình ở bên dưới.',
      ),
      _ => (
        'Chưa rõ cảm xúc của bạn',
        'Thử mô tả rõ hơn một chút hoặc chọn tâm trạng bên dưới.',
      ),
    };
  }

  @override
  Widget build(BuildContext context) {
    final (title, message) = _copy;
    final matched = result.status == MoodAnalysisStatus.matched;
    final manual = result.source == MoodAnalysisSource.manual;
    final icon = switch (result.status) {
      MoodAnalysisStatus.matched =>
        manual ? Icons.check_rounded : Icons.auto_awesome_rounded,
      MoodAnalysisStatus.unavailable => Icons.cloud_off_outlined,
      MoodAnalysisStatus.unsupportedLanguage => Icons.translate_rounded,
      _ => Icons.chat_bubble_outline_rounded,
    };
    return Semantics(
      container: true,
      liveRegion: true,
      child: Card(
        key: const ValueKey('mood-analysis-result'),
        color: AppColors.surfaceSecondary,
        child: Padding(
          padding: const EdgeInsets.all(16),
          child: Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Container(
                width: 40,
                height: 40,
                decoration: BoxDecoration(
                  color: AppColors.accentPrimary.withValues(alpha: .12),
                  borderRadius: BorderRadius.circular(12),
                ),
                child: Icon(icon, color: AppColors.accentPrimary, size: 21),
              ),
              const SizedBox(width: 12),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    if (matched) ...[
                      Text(
                        manual ? 'Bạn đã chọn' : 'Tâm trạng gợi ý',
                        style: Theme.of(context).textTheme.bodySmall,
                      ),
                      const SizedBox(height: 3),
                    ],
                    Text(
                      title,
                      style: matched
                          ? Theme.of(context).textTheme.titleLarge
                          : Theme.of(context).textTheme.titleSmall,
                    ),
                    const SizedBox(height: 6),
                    Text(
                      message,
                      style: Theme.of(context).textTheme.bodySmall?.copyWith(
                        color: AppColors.textSecondary,
                        height: 1.5,
                      ),
                    ),
                  ],
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}
