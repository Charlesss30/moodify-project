import '../../models/mobile_models.dart';

enum MoodAnalysisStatus {
  matched,
  unknown,
  ambiguous,
  unavailable,
  unsupportedLanguage,
}

enum MoodAnalysisSource { manual, ruleBased, ai }

class MoodAnalysisCandidate {
  const MoodAnalysisCandidate({
    required this.label,
    required this.score,
    this.moodId,
    this.decisionEligible = false,
  });

  final String label;
  final double score;
  final int? moodId;
  final bool decisionEligible;
}

class MoodAnalysisResult {
  const MoodAnalysisResult({
    required this.status,
    required this.label,
    required this.source,
    required this.analyzerVersion,
    required this.needsConfirmation,
    required this.reason,
    this.moodId,
    this.valence,
    this.arousal,
    this.confidence,
    this.analysisId,
    this.mappingVersion,
    this.candidates = const [],
    this.decisionCandidates = const [],
    this.language,
    this.usesVaModel = false,
  });

  final MoodAnalysisStatus status;
  final int? moodId;
  final String label;
  final double? valence;
  final double? arousal;
  final MoodAnalysisSource source;
  final String analyzerVersion;
  final bool needsConfirmation;
  final String reason;
  final double? confidence;
  final String? analysisId;
  final String? mappingVersion;
  final List<MoodAnalysisCandidate> candidates;
  final List<MoodAnalysisCandidate> decisionCandidates;
  final String? language;
  final bool usesVaModel;

  MoodAnalysisResult withMood(Mood mood) => MoodAnalysisResult(
    status: MoodAnalysisStatus.matched,
    moodId: mood.id,
    label: mood.name,
    valence: mood.valence,
    arousal: mood.arousal,
    source: MoodAnalysisSource.manual,
    analyzerVersion: analyzerVersion,
    needsConfirmation: false,
    reason: 'Bạn đã chọn tâm trạng này.',
    analysisId: analysisId,
    mappingVersion: mappingVersion,
    candidates: candidates,
    decisionCandidates: decisionCandidates,
    language: language,
    usesVaModel: usesVaModel,
  );

  MoodAnalysisResult withMappedMood(Mood mood) => MoodAnalysisResult(
    status: status,
    moodId: mood.id,
    label: mood.name,
    valence: valence ?? mood.valence,
    arousal: arousal ?? mood.arousal,
    source: source,
    analyzerVersion: analyzerVersion,
    needsConfirmation: true,
    reason: reason,
    confidence: confidence,
    analysisId: analysisId,
    mappingVersion: mappingVersion,
    candidates: candidates,
    decisionCandidates: decisionCandidates,
    language: language,
    usesVaModel: usesVaModel,
  );

  /// Response from the deployed PhoBERT / RoBERTa VA gateway.
  factory MoodAnalysisResult.fromVaApi(
    Map<String, dynamic> json, {
    required String expectedLanguage,
  }) {
    Never invalid() =>
        throw const FormatException('Kết quả model song ngữ không hợp lệ.');
    double coordinate(Object? value) {
      if (value is! num || !value.isFinite || value < -1 || value > 1) {
        invalid();
      }
      return value.toDouble();
    }

    final language = json['language'];
    final version = json['modelVersion'];
    final mood = json['mood'];
    if ((expectedLanguage != 'vi' && expectedLanguage != 'en') ||
        language != expectedLanguage ||
        json['status'] != 'matched' ||
        version is! String ||
        !RegExp(r'^[A-Za-z0-9][A-Za-z0-9_-]{2,79}$').hasMatch(version) ||
        mood is! Map) {
      invalid();
    }
    final moodData = Map<String, dynamic>.from(mood);
    final moodId = moodData['tamTrangID'];
    final moodName = moodData['tenTamTrang'];
    if (moodId is! int ||
        moodId <= 0 ||
        moodName is! String ||
        moodName.trim().isEmpty) {
      invalid();
    }
    final valence = coordinate(json['valence']);
    final arousal = coordinate(json['arousal']);
    double? confidence;
    var accepted = true;
    final candidates = <MoodAnalysisCandidate>[];
    if (language == 'vi') {
      const labels = {
        'Anger',
        'Disgust',
        'Enjoyment',
        'Fear',
        'Other',
        'Sadness',
        'Surprise',
      };
      final rawConfidence = json['confidence'];
      final rawAccepted = json['accepted'];
      final rawCandidates = json['emotionCandidates'];
      if (!labels.contains(json['emotion']) ||
          rawConfidence is! num ||
          !rawConfidence.isFinite ||
          rawConfidence < 0 ||
          rawConfidence > 1 ||
          rawAccepted is! bool ||
          rawCandidates is! List ||
          rawCandidates.isEmpty ||
          rawCandidates.length > 3) {
        invalid();
      }
      confidence = rawConfidence.toDouble();
      accepted = rawAccepted;
      for (final candidate in rawCandidates) {
        if (candidate is! Map) invalid();
        final label = candidate['label'];
        final score = candidate['score'];
        if (label is! String ||
            !labels.contains(label) ||
            score is! num ||
            !score.isFinite ||
            score < 0 ||
            score > 1) {
          invalid();
        }
        candidates.add(
          MoodAnalysisCandidate(label: label, score: score.toDouble()),
        );
      }
      if (candidates.map((item) => item.label).toSet().length !=
          candidates.length) {
        invalid();
      }
    }
    return MoodAnalysisResult(
      status: accepted
          ? MoodAnalysisStatus.matched
          : MoodAnalysisStatus.unknown,
      moodId: accepted ? moodId : null,
      label: accepted ? moodName : 'Chưa đủ tin cậy',
      source: MoodAnalysisSource.ai,
      analyzerVersion: version,
      language: language,
      usesVaModel: true,
      valence: valence,
      arousal: arousal,
      confidence: confidence,
      needsConfirmation: true,
      reason: accepted
          ? 'Đây là tâm trạng được gợi ý. Bạn có thể xác nhận hoặc chọn lại.'
          : 'Mô tả chưa đạt ngưỡng tin cậy. Hãy diễn đạt rõ hơn hoặc chọn tâm trạng bên dưới.',
      candidates: List.unmodifiable(candidates),
    );
  }

  factory MoodAnalysisResult.fromApi(Map<String, dynamic> json) {
    if (json['source'] != 'ai' ||
        json['language'] != 'en' ||
        json['scoreType'] != 'uncalibrated_sigmoid_not_accuracy' ||
        json['requiresConfirmation'] != true) {
      throw const FormatException('Hợp đồng phân tích AI không hợp lệ.');
    }
    final rawStatus = json['status'];
    final status = switch (rawStatus) {
      'matched' => MoodAnalysisStatus.matched,
      'ambiguous' => MoodAnalysisStatus.ambiguous,
      'unsupported_language' => MoodAnalysisStatus.unsupportedLanguage,
      'unavailable' => MoodAnalysisStatus.unavailable,
      'unknown' => MoodAnalysisStatus.unknown,
      _ => throw const FormatException('Trạng thái phân tích AI không hợp lệ.'),
    };
    final rawCandidates = json['candidates'];
    List<MoodAnalysisCandidate> parseCandidates(
      Object? raw,
      String errorMessage,
    ) {
      if (raw is! List) throw FormatException(errorMessage);
      final parsed = <MoodAnalysisCandidate>[];
      for (final value in raw) {
        if (value is! Map) throw FormatException(errorMessage);
        final candidate = Map<String, dynamic>.from(value);
        final label = candidate['label'];
        final score = candidate['score'];
        final moodId = candidate['moodId'];
        final eligible = candidate['decisionEligible'];
        if (label is! String ||
            score is! num ||
            !score.isFinite ||
            score < 0 ||
            score > 1 ||
            (moodId != null && moodId is! int) ||
            (eligible != null && eligible is! bool)) {
          throw FormatException(errorMessage);
        }
        parsed.add(
          MoodAnalysisCandidate(
            label: label,
            score: score.toDouble(),
            moodId: moodId as int?,
            decisionEligible: eligible as bool? ?? false,
          ),
        );
      }
      if (parsed.map((candidate) => candidate.label).toSet().length !=
          parsed.length) {
        throw FormatException(errorMessage);
      }
      return List.unmodifiable(parsed);
    }

    final candidates = parseCandidates(
      rawCandidates,
      'Ứng viên hiển thị AI không hợp lệ.',
    );
    final decisionCandidates = parseCandidates(
      json['decisionCandidates'],
      'Ứng viên đủ ngưỡng AI không hợp lệ.',
    );
    final rawMoodId = json['suggestedMoodId'];
    final analysisId = json['analysisId'];
    final modelVersion = json['modelVersion'];
    final mappingVersion = json['mappingVersion'];
    final reason = json['message'];
    if ((rawMoodId != null && rawMoodId is! int) ||
        (analysisId != null && analysisId is! String) ||
        (modelVersion != null && modelVersion is! String) ||
        (mappingVersion != null && mappingVersion is! String) ||
        (reason != null && reason is! String)) {
      throw const FormatException('Phản hồi phân tích AI không hợp lệ.');
    }
    if ((status == MoodAnalysisStatus.matched && rawMoodId == null) ||
        (status != MoodAnalysisStatus.matched && rawMoodId != null) ||
        candidates.length > 5 ||
        decisionCandidates.length > 28 ||
        decisionCandidates.any((candidate) => !candidate.decisionEligible) ||
        candidates.any(
          (candidate) =>
              candidate.decisionEligible !=
              decisionCandidates.any(
                (qualified) => qualified.label == candidate.label,
              ),
        )) {
      throw const FormatException('Ánh xạ phân tích AI không hợp lệ.');
    }
    return MoodAnalysisResult(
      status: status,
      moodId: rawMoodId as int?,
      label: status == MoodAnalysisStatus.matched
          ? 'Tâm trạng được gợi ý'
          : 'Chưa xác định',
      source: MoodAnalysisSource.ai,
      analyzerVersion: modelVersion as String? ?? 'unknown',
      needsConfirmation: json['requiresConfirmation'] != false,
      reason: reason as String? ?? 'Hãy chọn tâm trạng thủ công.',
      analysisId: analysisId as String?,
      mappingVersion: mappingVersion as String?,
      candidates: List.unmodifiable(candidates),
      decisionCandidates: decisionCandidates,
    );
  }
}

abstract interface class MoodAnalyzer {
  MoodAnalysisResult analyze(String text, List<Mood> moods);
}

class RuleBasedMoodAnalyzer implements MoodAnalyzer {
  const RuleBasedMoodAnalyzer();

  static const version = 'keyword-v1';

  static const _groups = <_MoodGroup>[
    _MoodGroup('joyful', [
      'vui vẻ',
      'hạnh phúc',
      'vui',
      'phấn khởi',
      'happy',
      'joyful',
    ]),
    _MoodGroup('sad', [
      'buồn bã',
      'buồn',
      'cô đơn',
      'thất vọng',
      'sad',
      'down',
    ]),
    _MoodGroup('relaxed', [
      'thư giãn',
      'bình yên',
      'nhẹ nhàng',
      'relax',
      'chill',
    ]),
    _MoodGroup('excited', [
      'hào hứng',
      'phấn khích',
      'tràn năng lượng',
      'excited',
      'hype',
    ]),
    _MoodGroup('focused', ['tập trung', 'làm việc', 'focus', 'focused']),
    _MoodGroup('romantic', ['lãng mạn', 'romantic', 'tình yêu']),
    _MoodGroup('nostalgic', ['hoài niệm', 'nhớ nhung', 'nostalgic']),
  ];

  static const _moodAliases = <String, List<String>>{
    'joyful': ['vui', 'happy', 'hạnh phúc', 'joy'],
    'sad': ['buồn', 'sad', 'trầm'],
    'relaxed': ['thư giãn', 'relax', 'calm', 'bình yên'],
    'excited': ['hào hứng', 'excited', 'phấn khích', 'năng lượng'],
    'focused': ['tập trung', 'focus', 'focused'],
    'romantic': ['lãng mạn', 'romantic', 'tình yêu'],
    'nostalgic': ['hoài niệm', 'nostalgic', 'nhớ nhung'],
  };

  static const _negations = ['khong', 'chang', 'chua', 'not', 'never'];

  @override
  MoodAnalysisResult analyze(String text, List<Mood> moods) {
    final normalized = _normalize(text);
    if (normalized.isEmpty) {
      return _unknown('Hãy nhập một câu mô tả tâm trạng.');
    }

    final matches = <String>{};
    var sawNegatedMatch = false;
    for (final group in _groups) {
      for (final keyword in group.keywords) {
        final phrase = _normalize(keyword);
        for (final match in RegExp(
          '(?:^| )${RegExp.escape(phrase)}(?= |\$)',
        ).allMatches(normalized)) {
          if (_isNegated(normalized, match.start, match.end)) {
            sawNegatedMatch = true;
          } else {
            matches.add(group.key);
          }
        }
      }
    }

    if (matches.length > 1) {
      return MoodAnalysisResult(
        status: MoodAnalysisStatus.ambiguous,
        label: 'Chưa rõ tâm trạng',
        source: MoodAnalysisSource.ruleBased,
        analyzerVersion: version,
        needsConfirmation: true,
        reason: 'Câu có nhiều sắc thái cảm xúc. Hãy chọn tâm trạng phù hợp.',
      );
    }
    if (matches.isEmpty) {
      return _unknown(
        sawNegatedMatch
            ? 'Từ khóa có phủ định nên chưa thể gợi ý tâm trạng.'
            : 'Chưa tìm thấy từ khóa phù hợp. Hãy chọn tâm trạng thủ công.',
      );
    }

    final groupKey = matches.single;
    final aliases = _moodAliases[groupKey]!;
    final mood = moods
        .where((item) => _matchesMood(item.name, aliases))
        .firstOrNull;
    if (mood == null) {
      return _unknown(
        'Danh sách tâm trạng hiện tại chưa có lựa chọn tương ứng.',
      );
    }

    return MoodAnalysisResult(
      status: MoodAnalysisStatus.matched,
      moodId: mood.id,
      label: mood.name,
      valence: mood.valence,
      arousal: mood.arousal,
      source: MoodAnalysisSource.ruleBased,
      analyzerVersion: version,
      needsConfirmation: true,
      reason: 'Gợi ý theo từ khóa. Hãy xác nhận hoặc chọn lại.',
    );
  }

  MoodAnalysisResult _unknown(String reason) => MoodAnalysisResult(
    status: MoodAnalysisStatus.unknown,
    label: 'Chưa xác định',
    source: MoodAnalysisSource.ruleBased,
    analyzerVersion: version,
    needsConfirmation: true,
    reason: reason,
  );

  bool _matchesMood(String name, List<String> aliases) {
    final normalized = ' ${_normalize(name)} ';
    return aliases
        .map(_normalize)
        .any((alias) => normalized.contains(' $alias '));
  }

  bool _isNegated(String text, int start, int end) {
    final before = text.substring(0, start).trimRight();
    final words = before.split(' ');
    final window = words
        .skip(words.length > 3 ? words.length - 3 : 0)
        .join(' ');
    if (_negations.any((word) => window == word || window.endsWith(' $word'))) {
      return true;
    }
    final after = text.substring(end).trimLeft();
    return after.startsWith('nua') &&
        (_negations.contains(window) || window.endsWith(' khong'));
  }
}

class _MoodGroup {
  const _MoodGroup(this.key, this.keywords);

  final String key;
  final List<String> keywords;
}

String _normalize(String value) {
  final folded = String.fromCharCodes(
    value.toLowerCase().runes.map((rune) {
      final replacement = _diacritics[String.fromCharCode(rune)];
      return replacement?.codeUnitAt(0) ?? rune;
    }),
  );
  return folded.replaceAll(RegExp(r'[^a-z0-9]+'), ' ').trim();
}

const _diacritics = <String, String>{
  'á': 'a',
  'à': 'a',
  'ả': 'a',
  'ã': 'a',
  'ạ': 'a',
  'ă': 'a',
  'ắ': 'a',
  'ằ': 'a',
  'ẳ': 'a',
  'ẵ': 'a',
  'ặ': 'a',
  'â': 'a',
  'ấ': 'a',
  'ầ': 'a',
  'ẩ': 'a',
  'ẫ': 'a',
  'ậ': 'a',
  'é': 'e',
  'è': 'e',
  'ẻ': 'e',
  'ẽ': 'e',
  'ẹ': 'e',
  'ê': 'e',
  'ế': 'e',
  'ề': 'e',
  'ể': 'e',
  'ễ': 'e',
  'ệ': 'e',
  'í': 'i',
  'ì': 'i',
  'ỉ': 'i',
  'ĩ': 'i',
  'ị': 'i',
  'ó': 'o',
  'ò': 'o',
  'ỏ': 'o',
  'õ': 'o',
  'ọ': 'o',
  'ô': 'o',
  'ố': 'o',
  'ồ': 'o',
  'ổ': 'o',
  'ỗ': 'o',
  'ộ': 'o',
  'ơ': 'o',
  'ớ': 'o',
  'ờ': 'o',
  'ở': 'o',
  'ỡ': 'o',
  'ợ': 'o',
  'ú': 'u',
  'ù': 'u',
  'ủ': 'u',
  'ũ': 'u',
  'ụ': 'u',
  'ư': 'u',
  'ứ': 'u',
  'ừ': 'u',
  'ử': 'u',
  'ữ': 'u',
  'ự': 'u',
  'ý': 'y',
  'ỳ': 'y',
  'ỷ': 'y',
  'ỹ': 'y',
  'ỵ': 'y',
  'đ': 'd',
};
