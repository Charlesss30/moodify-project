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
  String get _resolvedLanguage =>
      detectMoodLanguage(_descriptionController.text);

  void _invalidateAnalysis() {
    _analysisRequestVersion++;
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
      final language = _resolvedLanguage;
      final rawResult = await ApiService.analyzeMood(text, language: language);
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
      final analysis = _analysis;
      final useAi =
          analysis?.source == MoodAnalysisSource.ai &&
          analysis?.usesVaModel == true &&
          analysis?.status == MoodAnalysisStatus.matched &&
          analysis?.moodId == mood.id;
      final result = await ApiService.recommend(
        mood.id,
        type,
        source: useAi ? 'ai_va' : 'manual',
        valence: useAi ? analysis?.valence : null,
        arousal: useAi ? analysis?.arousal : null,
        modelVersion: useAi ? analysis?.analyzerVersion : null,
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
                          helperText: _descriptionController.text.trim().isEmpty
                              ? 'Tự nhận diện tiếng Việt / English.'
                              : _resolvedLanguage == 'vi'
                              ? 'Model: PhoBERT · Tiếng Việt'
                              : 'Model: RoBERTa VA · English',
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
                          _invalidateAnalysis();
                          _selectedMood = _moods
                              .where((mood) => mood.id == id)
                              .firstOrNull;
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
                          if (_analysis != null || _analyzing) {
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
                  onRetry: _requestRecommendations,
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

  @override
  Widget build(BuildContext context) {
    const emotionNames = {
      'Anger': 'Giận dữ',
      'Disgust': 'Ghê tởm',
      'Enjoyment': 'Vui vẻ',
      'Fear': 'Sợ hãi',
      'Other': 'Khác / trung tính',
      'Sadness': 'Buồn',
      'Surprise': 'Bất ngờ',
    };
    final candidateLabels = result.candidates
        .take(3)
        .map((candidate) => emotionNames[candidate.label] ?? candidate.label)
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
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                Icon(icon, color: AppColors.accentPrimary),
                const SizedBox(width: 10),
                Expanded(
                  child: Text(
                    result.status == MoodAnalysisStatus.matched
                        ? 'Tâm trạng gợi ý: ${moodName(result.label)}'
                        : result.label,
                    style: Theme.of(context).textTheme.titleSmall,
                  ),
                ),
              ],
            ),
            const SizedBox(height: 8),
            Text(result.reason),
            Theme(
              data: Theme.of(context)
                  .copyWith(dividerColor: Colors.transparent),
              child: ExpansionTile(
                key: const PageStorageKey('discovery-analysis-details'),
                title: const Text(
                  'Thông tin phân tích',
                  style: TextStyle(fontSize: 12),
                ),
                tilePadding: EdgeInsets.zero,
                expandedCrossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  if (result.confidence != null)
                    Text(
                      'Điểm tin cậy phân loại: ${(result.confidence! * 100).toStringAsFixed(1)}%',
                    ),
                  if (candidateLabels.isNotEmpty)
                    Text('Các sắc thái nhận diện: $candidateLabels'),
                  if (result.confidence != null)
                    const Text(
                      'Điểm tin cậy không phải tỷ lệ dự đoán đúng.',
                      style: TextStyle(
                        color: AppColors.textSecondary,
                        fontSize: 12,
                      ),
                    ),
                  const SizedBox(height: 4),
                  Text(
                    'Phiên bản: ${result.analyzerVersion}',
                    style: const TextStyle(
                      color: AppColors.textSecondary,
                      fontSize: 12,
                    ),
                  ),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }
}
