import 'package:flutter_test/flutter_test.dart';
import 'package:moodify_application/models/mobile_models.dart';
import 'package:moodify_application/services/mood_analysis/mood_analyzer.dart';

void main() {
  const analyzer = RuleBasedMoodAnalyzer();
  const moods = [
    Mood(
      id: 11,
      name: 'Vui vẻ',
      minValence: 0.5,
      maxValence: 1,
      minArousal: 0.4,
      maxArousal: 0.9,
    ),
    Mood(
      id: 12,
      name: 'Buồn bã',
      minValence: -1,
      maxValence: -0.2,
      minArousal: -0.9,
      maxArousal: 0.1,
    ),
    Mood(
      id: 13,
      name: 'Thư giãn',
      minValence: 0.2,
      maxValence: 0.8,
      minArousal: -0.9,
      maxArousal: -0.1,
    ),
    Mood(
      id: 14,
      name: 'Hào hứng',
      minValence: 0.4,
      maxValence: 0.9,
      minArousal: 0.6,
      maxArousal: 1,
    ),
  ];

  test('matches an accented positive mood and server ID', () {
    final result = analyzer.analyze('hôm nay vui quá', moods);

    expect(result.status, MoodAnalysisStatus.matched);
    expect(result.moodId, 11);
    expect(result.label, 'Vui vẻ');
    expect(result.valence, 0.75);
    expect(result.arousal, closeTo(0.65, 0.0001));
    expect(result.source, MoodAnalysisSource.ruleBased);
    expect(result.confidence, isNull);
    expect(result.needsConfirmation, isTrue);
  });

  test('does not turn a negated happy keyword into a positive match', () {
    final result = analyzer.analyze('tôi không vui', moods);

    expect(result.status, MoodAnalysisStatus.unknown);
    expect(result.moodId, isNull);
  });

  test('matches a relaxation phrase', () {
    final result = analyzer.analyze('muốn thư giãn', moods);

    expect(result.status, MoodAnalysisStatus.matched);
    expect(result.moodId, 13);
  });

  test('returns unknown for empty input', () {
    expect(analyzer.analyze('   ', moods).status, MoodAnalysisStatus.unknown);
  });

  test('returns unknown for text outside the configured dictionary', () {
    expect(
      analyzer.analyze('mai tôi đi siêu thị', moods).status,
      MoodAnalysisStatus.unknown,
    );
  });

  test('asks for manual selection when distinct feelings conflict', () {
    final result = analyzer.analyze('vừa buồn vừa hào hứng', moods);

    expect(result.status, MoodAnalysisStatus.ambiguous);
    expect(result.moodId, isNull);
    expect(result.needsConfirmation, isTrue);
  });
}
