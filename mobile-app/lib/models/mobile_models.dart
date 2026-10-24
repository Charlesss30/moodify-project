class Mood {
  const Mood({
    required this.id,
    required this.name,
    required this.minValence,
    required this.maxValence,
    required this.minArousal,
    required this.maxArousal,
  });

  final int id;
  final String name;
  final double minValence;
  final double maxValence;
  final double minArousal;
  final double maxArousal;

  double get valence => (minValence + maxValence) / 2;
  double get arousal => (minArousal + maxArousal) / 2;

  factory Mood.fromJson(Map<String, dynamic> json) => Mood(
    id: _int(json['tamTrangID'] ?? json['TamTrangID'] ?? json['moodId']),
    name: _string(json['tenTamTrang'] ?? json['TenTamTrang'] ?? json['name']),
    minValence: _double(json['minValence'] ?? json['MinValence']),
    maxValence: _double(json['maxValence'] ?? json['MaxValence']),
    minArousal: _double(json['minArousal'] ?? json['MinArousal']),
    maxArousal: _double(json['maxArousal'] ?? json['MaxArousal']),
  );
}

class ContentItem {
  const ContentItem({
    required this.id,
    required this.title,
    required this.type,
    this.imageUrl,
    this.description,
    this.genre,
    this.rating,
    this.imdbId,
    this.trailerUrl,
    this.ratingSource,
    this.artist,
    this.director,
    this.durationSeconds,
    this.album,
    this.valence,
    this.arousal,
    this.playbackMode,
    this.sourceUrl,
    this.recommendationId,
  });

  final String id;
  final String title;
  final String type;
  final String? imageUrl;
  final String? description;
  final String? genre;
  final double? rating;
  final String? imdbId;
  final String? trailerUrl;
  final String? ratingSource;
  final String? artist;
  final String? director;
  final int? durationSeconds;
  final String? album;
  final double? valence;
  final double? arousal;
  final String? playbackMode;
  final String? sourceUrl;
  final String? recommendationId;

  bool get isMusic => type == 'Music';
  List<String> get genres => (genre ?? '')
      .split(RegExp(r'[,;|]'))
      .map((v) => v.trim())
      .where((v) => v.isNotEmpty)
      .toSet()
      .toList();

  factory ContentItem.fromJson(Map<String, dynamic> json) {
    final music = _map(json['nhac'] ?? json['Nhac']);
    final movie = _map(json['phim'] ?? json['Phim']);
    final rawType = _string(
      json['loaiNoiDung'] ?? json['LoaiNoiDung'] ?? json['contentType'],
    ).toLowerCase();
    final type = const {'music', 'song', 'nhac'}.contains(rawType)
        ? 'Music'
        : 'Movie';
    return ContentItem(
      id: _string(json['noiDungID'] ?? json['NoiDungID'] ?? json['id']),
      title: _string(json['tieuDe'] ?? json['TieuDe'] ?? json['title']),
      type: type,
      imageUrl: _optionalString(
        json['hinhAnh'] ?? json['HinhAnh'] ?? json['imageUrl'],
      ),
      description: _optionalString(
        json['moTa'] ?? json['MoTa'] ?? json['description'],
      ),
      genre: _optionalString(
        json['genre'] ??
            json['theLoai'] ??
            json['TheLoai'] ??
            music['genre'] ??
            music['Genre'] ??
            movie['theLoai'] ??
            movie['TheLoai'],
      ),
      rating: _optionalDouble(
        json['rating'] ??
            json['diemDanhGiaTB'] ??
            json['DiemDanhGiaTB'] ??
            movie['diemDanhGiaTB'] ??
            movie['DiemDanhGiaTB'],
      ),
      ratingSource: _optionalString(
        movie['ratingSource'] ?? json['ratingSource'],
      ),
      trailerUrl: _optionalString(movie['trailerUrl'] ?? json['trailerUrl']),
      imdbId: _optionalString(
        json['imdbId'] ??
            json['imdbID'] ??
            json['IMDBID'] ??
            movie['imdbid'] ??
            movie['imdbID'] ??
            movie['IMDBID'],
      ),
      director: _optionalString(
        json['director'] ?? movie['director'] ?? movie['Director'],
      ),
      artist: _optionalString(
        json['artist'] ?? music['tenNgheSi'] ?? music['TenNgheSi'],
      ),
      durationSeconds: _optionalInt(
        json['duration'] ?? music['duration'] ?? music['Duration'],
      ),
      album: _optionalString(json['album'] ?? music['album'] ?? music['Album']),
      valence: _optionalDouble(json['valence'] ?? json['Valence']),
      arousal: _optionalDouble(json['arousal'] ?? json['Arousal']),
      playbackMode: _optionalString(
        json['playbackMode'] ?? json['PlaybackMode'],
      ),
      recommendationId: _optionalString(json['recommendationId']),
      sourceUrl: _optionalString(
        json['sourceUrl'] ??
            json['SourceUrl'] ??
            music['sourceUrl'] ??
            music['SourceUrl'],
      ),
    );
  }
}

class RecommendationItem {
  const RecommendationItem({required this.content, required this.match});

  final ContentItem content;
  final double match;

  factory RecommendationItem.fromJson(Map<String, dynamic> json) {
    final content = _map(json['content'] ?? json['Content']);
    return RecommendationItem(
      content: ContentItem.fromJson(content),
      match: _double(json['match'] ?? json['Match']).clamp(0, 100).toDouble(),
    );
  }
}

class RecommendationResult {
  const RecommendationResult({
    required this.algorithm,
    required this.moodLabel,
    required this.valence,
    required this.arousal,
    required this.items,
  });

  final String algorithm;
  final String moodLabel;
  final double valence;
  final double arousal;
  final List<RecommendationItem> items;

  factory RecommendationResult.fromJson(Map<String, dynamic> json) =>
      RecommendationResult(
        algorithm: _string(json['algorithm']),
        moodLabel: _string(json['mood']),
        valence: _double(json['valence']),
        arousal: _double(json['arousal']),
        items: _list(json['items'])
            .map((item) => RecommendationItem.fromJson(_map(item)))
            .toList(growable: false),
      );
}

class MoodHistoryEntry {
  const MoodHistoryEntry({
    required this.id,
    required this.moodId,
    required this.moodName,
    required this.createdAt,
    required this.valence,
    required this.arousal,
    this.recommendedItems,
    this.algorithm,
    this.source,
    this.recommendationId,
  });

  final int id;
  final int moodId;
  final String moodName;
  final DateTime? createdAt;
  final double valence;
  final double arousal;
  final List<ContentItem>? recommendedItems;
  final String? algorithm;
  final String? source;
  final String? recommendationId;

  factory MoodHistoryEntry.fromJson(Map<String, dynamic> json) =>
      MoodHistoryEntry(
        id: _int(json['lichSuID'] ?? json['LichSuID']),
        moodId: _int(
          json['tamTrangID'] ?? json['TamTrangID'] ?? json['moodId'],
        ),
        moodName: _string(
          json['tenTamTrang'] ?? json['TenTamTrang'] ?? json['mood'],
        ),
        createdAt: DateTime.tryParse(
          _string(json['thoiGian'] ?? json['ThoiGian'] ?? json['createdAt']),
        )?.toLocal(),
        valence: _double(json['valence'] ?? json['Valence']),
        arousal: _double(json['arousal'] ?? json['Arousal']),
        recommendedItems: json['recommendedItems'] is List
            ? _list(json['recommendedItems'])
                  .map((item) => ContentItem.fromJson(_map(item)))
                  .toList(growable: false)
            : null,
        algorithm: _optionalString(json['algorithm']),
        source: _optionalString(json['source']),
        recommendationId: _optionalString(json['recommendationId']),
      );
}

class PagedResult<T> {
  const PagedResult({
    required this.total,
    required this.page,
    required this.pageSize,
    required this.items,
  });

  final int total;
  final int page;
  final int pageSize;
  final List<T> items;

  factory PagedResult.fromJson(
    Map<String, dynamic> json,
    T Function(Map<String, dynamic>) parseItem,
  ) => PagedResult<T>(
    total: _int(json['total']),
    page: _int(json['page'], fallback: 1),
    pageSize: _int(json['pageSize'], fallback: 50),
    items: _list(json['items'])
        .map((item) => parseItem(_map(item)))
        .toList(growable: false),
  );
}

Map<String, dynamic> _map(dynamic value) =>
    value is Map ? Map<String, dynamic>.from(value) : <String, dynamic>{};
List<dynamic> _list(dynamic value) => value is List ? value : const [];
String _string(dynamic value) => value?.toString() ?? '';
String? _optionalString(dynamic value) =>
    value == null || value.toString().trim().isEmpty ? null : value.toString();
int _int(dynamic value, {int fallback = 0}) => value is num
    ? value.toInt()
    : int.tryParse(value?.toString() ?? '') ?? fallback;
int? _optionalInt(dynamic value) => value == null ? null : _int(value);
double _double(dynamic value, {double fallback = 0}) => value is num
    ? value.toDouble()
    : double.tryParse(value?.toString() ?? '') ?? fallback;
double? _optionalDouble(dynamic value) => value == null ? null : _double(value);
