import 'package:flutter_test/flutter_test.dart';
import 'package:moodify_application/models/mobile_models.dart';
import 'package:moodify_application/services/mood_analysis/mood_analyzer.dart';

import 'bilingual_mood_flow_test.dart' show prediction;

Map<String, dynamic> six(String language) => {
  ...prediction(language),
  'modelType': 'emotion_classification_va_adapter',
  'scoreType': 'softmax_not_accuracy',
  'taxonomy': 'moodify-six-v1',
  'calibrationStatus': 'uncalibrated',
  'modelAccepted': null,
  'emotion': 'Love',
  'confidence': 0.94,
  'emotionCandidates': [
    {'label': 'Love', 'score': 0.94},
    {'label': 'Enjoyment', 'score': 0.03},
    {'label': 'Sadness', 'score': 0.02},
  ],
};

void main() {
  for (final language in ['vi', 'en']) {
    test(
      '$language six-class suggestion retains server receipt and uncertainty',
      () {
        final result = MoodAnalysisResult.fromVaApi(
          six(language),
          expectedLanguage: 'auto',
        );
        expect(result.status, MoodAnalysisStatus.matched);
        expect(result.confidence, 0.94);
        expect(result.needsConfirmation, isTrue);
        expect(result.modelAccepted, isNull);
        expect(result.calibrationStatus, 'uncalibrated');
        expect(result.analysisId, startsWith('va_'));
        final chosen = result.withMood(
          const Mood(
            id: 5,
            name: 'Happy',
            minValence: .6,
            maxValence: 1,
            minArousal: .3,
            maxArousal: .7,
          ),
        );
        expect(chosen.taxonomy, 'moodify-six-v1');
        expect(chosen.calibrationStatus, 'uncalibrated');
      },
    );
    test('$language six-class cannot accept a seven-class-only label', () {
      final data = six(language)..['emotion'] = 'Other';
      expect(
        () => MoodAnalysisResult.fromVaApi(data, expectedLanguage: 'auto'),
        throwsFormatException,
      );
    });
    test('$language six-class cannot bypass receipt or server acceptance', () {
      for (final change in <Map<String, Object?>>[
        {'analysisId': null},
        {'accepted': null},
        {'modelAccepted': false},
        {'taxonomy': 'unknown-taxonomy'},
      ]) {
        expect(
          () => MoodAnalysisResult.fromVaApi({
            ...six(language),
            ...change,
          }, expectedLanguage: 'auto'),
          throwsFormatException,
        );
      }
    });
  }
}
