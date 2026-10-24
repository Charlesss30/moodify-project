import 'mobile_models.dart';

Map<String, dynamic> _map(dynamic value) =>
    value is Map ? Map<String, dynamic>.from(value) : <String, dynamic>{};

class PersonalRating {
  const PersonalRating({
    required this.id,
    required this.content,
    required this.stars,
    required this.available,
    this.comment,
    this.createdAt,
    this.recommendationId,
  });
  final int id, stars;
  final ContentItem content;
  final bool available;
  final String? comment, recommendationId;
  final DateTime? createdAt;
  factory PersonalRating.fromJson(Map<String, dynamic> json) => PersonalRating(
    id: (json['danhGiaID'] as num?)?.toInt() ?? 0,
    content: ContentItem.fromJson(_map(json['content'])),
    stars: (json['soSao'] as num?)?.toInt() ?? 0,
    available: json['available'] == true,
    comment: json['nhanXet'] as String?,
    recommendationId: json['recommendationId'] as String?,
    createdAt: DateTime.tryParse(json['thoiGian']?.toString() ?? '')?.toLocal(),
  );
}

class SavedEntry {
  const SavedEntry({
    required this.content,
    required this.available,
    this.createdAt,
  });
  final ContentItem content;
  final bool available;
  final DateTime? createdAt;
  factory SavedEntry.fromJson(Map<String, dynamic> json) => SavedEntry(
    content: ContentItem.fromJson(_map(json['content'])),
    available: json['available'] == true,
    createdAt: DateTime.tryParse(json['createdAt']?.toString() ?? '')
        ?.toLocal(),
  );
}

class WeeklySummary {
  const WeeklySummary({
    required this.moodDays,
    required this.openedContents,
    required this.ratings,
  });
  final int moodDays, openedContents, ratings;
  factory WeeklySummary.fromJson(Map<String, dynamic> json) {
    final summary = _map(json['summary']);
    if (summary['moodDays'] is! num ||
        summary['openedContents'] is! num ||
        summary['ratings'] is! num) {
      throw const FormatException('Missing weekly summary');
    }
    return WeeklySummary(
      moodDays: (summary['moodDays'] as num).toInt(),
      openedContents: (summary['openedContents'] as num).toInt(),
      ratings: (summary['ratings'] as num).toInt(),
    );
  }
}
