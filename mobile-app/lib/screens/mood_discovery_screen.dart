import 'package:flutter/material.dart';

import '../core/presentation.dart';
import '../widgets/app_widgets.dart';
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
                        decoration: const InputDecoration(
                          labelText: 'Mô tả tâm trạng',
                          hintText: 'Ví dụ: I feel happy today',
                          helperText: 'AI hiện hỗ trợ mô tả bằng tiếng Anh.',
                          alignLabelWithHint: true,
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
    final matched = result.status == MoodAnalysisStatus.matched;
    final title = switch (result.status) {
      MoodAnalysisStatus.matched =>
        'Có thể bạn đang ${moodName(result.label).toLowerCase()}',
      MoodAnalysisStatus.ambiguous => 'Cảm xúc của bạn có nhiều sắc thái',
      MoodAnalysisStatus.unavailable => 'Chưa kết nối được AI',
      MoodAnalysisStatus.unsupportedLanguage => 'Ngôn ngữ chưa được hỗ trợ',
      MoodAnalysisStatus.unknown => 'Chưa xác định được tâm trạng',
    };
    final description = switch (result.status) {
      MoodAnalysisStatus.matched => 'Bạn thấy kết quả này phù hợp chứ? Có thể chọn lại tâm trạng trước khi nhận gợi ý.',
      MoodAnalysisStatus.ambiguous =>
        'Hãy chọn cảm xúc gần với bạn nhất ở bên dưới.',
      MoodAnalysisStatus.unavailable =>
        'Bạn vẫn có thể chọn tâm trạng để tiếp tục tìm phim và nhạc.',
      MoodAnalysisStatus.unsupportedLanguage =>
        'Hãy nhập mô tả bằng tiếng Anh hoặc chọn tâm trạng bên dưới.',
      MoodAnalysisStatus.unknown =>
        'Thử mô tả rõ hơn hoặc tự chọn tâm trạng của bạn.',
    };
    return Card(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Padding(
            padding: const EdgeInsets.all(16),
            child: Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Icon(
                  matched ? Icons.auto_awesome : Icons.chat_bubble_outline,
                  color: Theme.of(context).colorScheme.primary,
                ),
                const SizedBox(width: 12),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        title,
                        style: Theme.of(context).textTheme.titleSmall,
                      ),
                      const SizedBox(height: 6),
                      Text(
                        description,
                        style: Theme.of(context).textTheme.bodySmall,
                      ),
                    ],
                  ),
                ),
              ],
            ),
          ),
          Theme(
            data: Theme.of(context).copyWith(dividerColor: Colors.transparent),
            child: ExpansionTile(
              key: const PageStorageKey('discovery-analysis-details'),
              title: const Text(
                'Thông tin phân tích',
                style: TextStyle(fontSize: 12),
              ),
              tilePadding: const EdgeInsets.symmetric(horizontal: 16),
              childrenPadding: const EdgeInsets.fromLTRB(16, 0, 16, 16),
              expandedCrossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  result.reason,
                  style: Theme.of(context).textTheme.bodySmall,
                ),
                Text(
                  'Phiên bản: ${result.analyzerVersion}',
                  style: Theme.of(context).textTheme.bodySmall,
                ),
                Text(
                  'Nhãn hiển thị: ${result.candidates.take(3).map((x) => x.label).join(', ')}',
                  style: Theme.of(context).textTheme.bodySmall,
                ),
                Text(
                  'Nhãn đủ ngưỡng: ${result.decisionCandidates.map((x) => x.label).join(', ')}',
                  style: Theme.of(context).textTheme.bodySmall,
                ),
                const Text(
                  'Kết quả thử nghiệm cần bạn xác nhận. Điểm của model không phải phần trăm chính xác.',
                  style: TextStyle(fontSize: 12),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}
