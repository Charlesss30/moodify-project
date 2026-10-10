import 'package:flutter_test/flutter_test.dart';
import 'package:moodify_application/services/mood_analysis/mood_analyzer.dart';

void main() {
  test('parses a mapped English AI suggestion without inventing accuracy', () {
    final result = MoodAnalysisResult.fromApi({
      'analysisId': 'abc123',
      'status': 'matched',
      'source': 'ai',
      'modelVersion': 'roberta_large_asl_en_01',
      'mappingVersion': 'emotion-mood-map-v1',
      'language': 'en',
      'candidates': [
        {
          'label': 'joy',
          'score': 0.97,
          'moodId': 7,
          'decisionEligible': true,
        },
      ],
      'decisionCandidates': [
        {
          'label': 'joy',
          'score': 0.97,
          'moodId': 7,
          'decisionEligible': true,
        },
      ],
      'suggestedMoodId': 7,
      'requiresConfirmation': true,
      'scoreType': 'uncalibrated_sigmoid_not_accuracy',
      'message': 'Confirm this experimental suggestion.',
    });

    expect(result.source, MoodAnalysisSource.ai);
    expect(result.status, MoodAnalysisStatus.matched);
    expect(result.moodId, 7);
    expect(result.analysisId, 'abc123');
    expect(result.confidence, isNull);
    expect(result.needsConfirmation, isTrue);
    expect(result.candidates.single.score, 0.97);
    expect(result.decisionCandidates.single.label, 'joy');
    expect(result.decisionCandidates.single.decisionEligible, isTrue);
  });

  test('rejects invalid model scores instead of displaying them', () {
    expect(
      () => MoodAnalysisResult.fromApi({
        'source': 'ai',
        'language': 'en',
        'scoreType': 'uncalibrated_sigmoid_not_accuracy',
        'requiresConfirmation': true,
        'status': 'matched',
        'candidates': [
          {'label': 'joy', 'score': 1.1},
        ],
        'decisionCandidates': <Map<String, Object>>[],
      }),
      throwsFormatException,
    );
  });

  test('keeps unsupported language distinct from a mood result', () {
    final result = MoodAnalysisResult.fromApi({
      'source': 'ai',
      'language': 'en',
      'scoreType': 'uncalibrated_sigmoid_not_accuracy',
      'status': 'unsupported_language',
      'candidates': <Map<String, Object>>[],
      'decisionCandidates': <Map<String, Object>>[],
      'requiresConfirmation': true,
      'message': 'English only.',
    });

    expect(result.status, MoodAnalysisStatus.unsupportedLanguage);
    expect(result.moodId, isNull);
    expect(result.analysisId, isNull);
  });
}
