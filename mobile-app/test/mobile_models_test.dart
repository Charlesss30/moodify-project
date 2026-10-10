import 'package:flutter_test/flutter_test.dart';
import 'package:moodify_application/models/mobile_models.dart';

void main() {
  test('director parses in both recommendation and detail payloads', () {
    expect(
      ContentItem.fromJson({'director': 'Director A'}).director,
      'Director A',
    );
    expect(
      ContentItem.fromJson({
        'phim': {'director': 'Director B'},
      }).director,
      'Director B',
    );
  });
  test('parses recommendation content with backend camel-case fields and Song type', () {
    final item = RecommendationItem.fromJson({
      'match': 84.6,
      'content': {
        'noiDungID': 'song-1',
        'tieuDe': 'Track title',
        'loaiNoiDung': 'Song',
        'hinhAnh': 'https://example.test/cover.jpg',
        'artist': 'Artist',
        'duration': 192,
        'album': 'Album',
      },
    });

    expect(item.content.type, 'Music');
    expect(item.content.id, 'song-1');
    expect(item.content.title, 'Track title');
    expect(item.content.artist, 'Artist');
    expect(item.content.durationSeconds, 192);
    expect(item.match, closeTo(84.6, 0.001));
  });

  test(
    'parses Pascal-case movie metadata and tolerates absent optional values',
    () {
      final item = ContentItem.fromJson({
        'NoiDungID': 'movie-1',
        'TieuDe': 'Movie title',
        'LoaiNoiDung': 'Movie',
        'Phim': {
          'TheLoai': 'Drama',
          'DiemDanhGiaTB': 7.5,
          'IMDBID': 'tt1234567',
        },
      });

      expect(item.type, 'Movie');
      expect(item.genre, 'Drama');
      expect(item.rating, 7.5);
      expect(item.imdbId, 'tt1234567');
      expect(item.imageUrl, isNull);
      expect(item.description, isNull);
    },
  );

  test('keeps old mood history distinct from a saved empty recommendation', () {
    final legacy = MoodHistoryEntry.fromJson({
      'lichSuID': 7,
      'tamTrangID': 3,
      'tenTamTrang': 'Thư giãn',
      'thoiGian': '2026-10-02T12:00:00Z',
      'valence': 0.5,
      'arousal': -0.2,
    });
    final emptySnapshot = MoodHistoryEntry.fromJson({
      'lichSuID': 8,
      'tamTrangID': 3,
      'tenTamTrang': 'Thư giãn',
      'thoiGian': '2026-10-02T12:00:00Z',
      'valence': 0.5,
      'arousal': -0.2,
      'recommendedItems': [],
    });

    expect(legacy.recommendedItems, isNull);
    expect(emptySnapshot.recommendedItems, isEmpty);
  });
}
