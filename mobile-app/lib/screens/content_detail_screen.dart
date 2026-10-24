import 'package:flutter/foundation.dart';

import 'dart:async';
import 'dart:math';

import '../widgets/rating_dialog.dart';
import 'mood_discovery_screen.dart';
import '../services/playback/spotify_embed.dart';
import '../services/playback/youtube_embed.dart';

import 'package:flutter/material.dart';
import 'package:url_launcher/url_launcher.dart';

import '../core/api_service.dart';
import '../core/colors.dart';
import '../core/navigation.dart';
import '../core/presentation.dart';
import '../widgets/app_widgets.dart';
import '../models/mobile_models.dart';

class ContentDetailScreen extends StatefulWidget {
  const ContentDetailScreen({
    super.key,
    required this.contentId,
    required this.type,
    this.recommendationId,
  });

  final String contentId;
  final String type;
  final String? recommendationId;

  @override
  State<ContentDetailScreen> createState() => _ContentDetailScreenState();
}

class _ContentDetailScreenState extends State<ContentDetailScreen>
    with RouteAware {
  ModalRoute<dynamic>? _observedRoute;
  bool _covered = false;

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    final route = ModalRoute.of(context);
    if (route != _observedRoute) {
      appRouteObserver.unsubscribe(this);
      _observedRoute = route;
      if (route != null) appRouteObserver.subscribe(this, route);
    }
  }

  @override
  void didPushNext() {
    if (mounted) setState(() => _covered = true);
  }

  @override
  void didPopNext() {
    if (mounted) {
      setState(() => _covered = false);
      if (_seenSavedRevision != ApiService.savedRevision.value) {
        unawaited(_loadSaved());
      }
    }
  }

  @override
  void dispose() {
    appRouteObserver.unsubscribe(this);
    super.dispose();
  }

  ContentItem? _content;
  String? _error;
  bool _loading = true;
  bool _ratingOpen = false;
  bool _openingTrailer = false;
  bool? _saved;
  bool _saving = false, _checkingSaved = false;
  String? _savedError;
  int _seenSavedRevision = -1;
  bool _detailRecorded = false, _playerRecorded = false;

  Future<void> _record(String action) async {
    final random = Random.secure();
    final id = List.generate(
      16,
      (_) => random.nextInt(256).toRadixString(16).padLeft(2, '0'),
    ).join();
    try {
      await ApiService.interaction(
        widget.contentId,
        action,
        id,
        recommendationId: widget.recommendationId,
      );
    } catch (_) {
      /* Telemetry must not block content or playback. */
    }
  }

  bool _playerLoading = false;
  String? _embedUrl, _sourceUrl, _playerError;

  @override
  void initState() {
    super.initState();
    _load();
    _loadSaved();
  }

  Future<void> _load() async {
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final content = await ApiService.content(widget.type, widget.contentId);
      if (!mounted) return;
      setState(() => _content = content);
      if (!_detailRecorded) {
        _detailRecorded = true;
        unawaited(_record('detail_open'));
      }
      if (content.isMusic) await _loadPlayer();
    } on ApiException catch (error) {
      if (mounted) setState(() => _error = error.message);
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  Future<void> _loadSaved() async {
    if (_checkingSaved || _saving) {
      return;
    }
    setState(() {
      _checkingSaved = true;
      _savedError = null;
    });
    try {
      final saved = await ApiService.isSaved(widget.contentId);
      if (mounted) {
        setState(() {
          _saved = saved;
          _seenSavedRevision = ApiService.savedRevision.value;
        });
      }
    } on ApiException catch (error) {
      if (mounted) {
        setState(() => _savedError = error.message);
      }
    } finally {
      if (mounted) {
        setState(() => _checkingSaved = false);
      }
    }
  }

  Future<void> _toggleSaved() async {
    if (_saving || _checkingSaved) {
      return;
    }
    if (_saved == null) {
      await _loadSaved();
      return;
    }
    final next = !_saved!;
    setState(() {
      _saving = true;
      _savedError = null;
    });
    try {
      await ApiService.setSaved(widget.contentId, next);
      if (mounted) {
        setState(() {
          _saved = next;
          _seenSavedRevision = ApiService.savedRevision.value;
        });
      }
    } on ApiException catch (error) {
      if (mounted) {
        setState(() => _savedError = error.message);
      }
    } finally {
      if (mounted) {
        setState(() => _saving = false);
      }
    }
  }

  Future<void> _loadPlayer() async {
    if (_playerLoading) return;
    setState(() {
      _playerLoading = true;
      _playerError = null;
    });
    if (validSpotifyUrl(_content?.sourceUrl)) _sourceUrl = _content!.sourceUrl;
    try {
      final data = await ApiService.musicPreview(widget.contentId);
      if (!mounted) return;
      if (data['mode'] != 'embed' ||
          !validSpotifyUrl(data['embedUrl'] as String?, embed: true)) {
        throw const ApiException('Trình phát Spotify chưa khả dụng.');
      }
      setState(() {
        _embedUrl = data['embedUrl'] as String;
        final id = Uri.parse(_embedUrl!).pathSegments.last;
        _sourceUrl = 'https://open.spotify.com/track/$id';
      });
      if (!_playerRecorded) {
        _playerRecorded = true;
        unawaited(_record('player_loaded'));
      }
    } on ApiException catch (e) {
      if (mounted) setState(() => _playerError = e.message);
    } finally {
      if (mounted) setState(() => _playerLoading = false);
    }
  }

  Future<void> _rate() async {
    if (_ratingOpen || _content == null) return;
    setState(() => _ratingOpen = true);
    final stars = await showDialog<int>(
      context: context,
      barrierDismissible: false,
      builder: (_) => RatingDialog(
        contentId: widget.contentId,
        title: _content!.title,
        recommendationId: widget.recommendationId,
      ),
    );
    if (!mounted) return;
    setState(() => _ratingOpen = false);
    if (stars == null) return;
    ScaffoldMessenger.of(
      context,
    ).showSnackBar(const SnackBar(content: Text('Đã lưu đánh giá của bạn.')));
    if (stars == 1) {
      final explore = await showDialog<bool>(
        context: context,
        builder: (context) => AlertDialog(
          title: const Text('Chưa đúng tâm trạng?'),
          content: const Text(
            'Bạn muốn khám phá tâm trạng khác để tìm gợi ý mới?',
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.pop(context, false),
              child: const Text('Để sau'),
            ),
            FilledButton(
              onPressed: () => Navigator.pop(context, true),
              child: const Text('Khám phá'),
            ),
          ],
        ),
      );
      if (explore == true && mounted) {
        setState(() => _embedUrl = null);
        await Navigator.of(context).push(
          MaterialPageRoute<void>(builder: (_) => const MoodDiscoveryScreen()),
        );
        if (mounted && _content!.isMusic) await _loadPlayer();
      }
    }
  }

  Future<void> _openSpotify() async {
    if (!validSpotifyUrl(_sourceUrl)) return;
    final savedEmbedUrl = _embedUrl;
    setState(() => _embedUrl = null);
    try {
      if (!kIsWeb) {
        final trackId = Uri.parse(_sourceUrl!).pathSegments.last;
        try {
          if (await launchUrl(
            Uri.parse('spotify:track:$trackId'),
            mode: LaunchMode.externalApplication,
          )) {
            unawaited(_record('external_open'));
            return;
          }
        } catch (_) {
          /* Fall back to the official HTTPS link. */
        }
      }
      if (!await launchUrl(
        Uri.parse(_sourceUrl!),
        mode: LaunchMode.externalApplication,
      )) {
        throw Exception();
      }
      unawaited(_record('external_open'));
    } catch (_) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(
            content: Text('Không mở được Spotify trên thiết bị này.'),
          ),
        );
      }
    } finally {
      if (mounted) setState(() => _embedUrl = savedEmbedUrl);
    }
  }

  @override
  Widget build(BuildContext context) {
    final content = _content;
    final imdbId = content?.imdbId;
    final trailerId = youtubeVideoId(content?.trailerUrl);
    final validImdb =
        imdbId != null && RegExp(r'^tt[0-9]{7,10}$').hasMatch(imdbId);
    return Scaffold(
      appBar: AppBar(
        title: Text(
          widget.type == 'Music' ? 'Chi tiết bài hát' : 'Chi tiết phim',
        ),
      ),
      body: _loading && content == null
          ? const Center(child: CircularProgressIndicator())
          : _error != null && content == null
          ? _DetailError(message: _error!, onRetry: _load)
          : content == null
          ? const Center(child: Text('Không tìm thấy nội dung.'))
          : ListView(
              padding: const EdgeInsets.fromLTRB(20, 8, 20, 28),
              children: [
                _DetailHeader(content: content),
                const SizedBox(height: 12),
                Align(
                  alignment: Alignment.centerLeft,
                  child: OutlinedButton.icon(
                    onPressed: _saving || _checkingSaved ? null : _toggleSaved,
                    icon: _saving || _checkingSaved
                        ? const SizedBox(
                            width: 17,
                            height: 17,
                            child: CircularProgressIndicator(strokeWidth: 2),
                          )
                        : Icon(
                            _saved == true
                                ? Icons.bookmark
                                : Icons.bookmark_border,
                          ),
                    label: Text(
                      _checkingSaved
                          ? 'Đang kiểm tra…'
                          : _saved == null
                          ? 'Kiểm tra mục đã lưu'
                          : _saved!
                          ? 'Đã lưu'
                          : content.isMusic
                          ? 'Lưu để nghe sau'
                          : 'Lưu để xem sau',
                    ),
                  ),
                ),
                if (_savedError != null)
                  Padding(
                    padding: const EdgeInsets.only(top: 8),
                    child: Text(
                      _savedError!,
                      style: TextStyle(
                        color: Theme.of(context).colorScheme.error,
                      ),
                    ),
                  ),
                const SizedBox(height: 22),
                if (!content.isMusic && trailerId != null) ...[
                  const SizedBox(height: 20),
                  Text(
                    'Trailer',
                    style: Theme.of(context).textTheme.titleMedium,
                  ),
                  const SizedBox(height: 8),
                  if (!_covered && !_openingTrailer && !_ratingOpen)
                    YoutubePlayer(
                      key: ValueKey('youtube-inline-$trailerId'),
                      videoId: trailerId,
                    ),
                  TextButton.icon(
                    onPressed: _openingTrailer
                        ? null
                        : () async {
                            setState(() => _openingTrailer = true);
                            // Remove the embedded player before launching a second player externally.
                            await WidgetsBinding.instance.endOfFrame;
                            try {
                              final opened = await launchUrl(
                                Uri.https('www.youtube.com', '/watch', {
                                  'v': trailerId,
                                }),
                                mode: LaunchMode.externalApplication,
                              );
                              if (opened) {
                                unawaited(_record('trailer_open'));
                              } else {
                                throw Exception();
                              }
                            } catch (_) {
                              if (context.mounted) {
                                ScaffoldMessenger.of(context).showSnackBar(
                                  const SnackBar(
                                    content: Text('Không mở được YouTube.'),
                                  ),
                                );
                              }
                            } finally {
                              if (mounted) {
                                setState(() => _openingTrailer = false);
                              }
                            }
                          },
                    icon: const Icon(Icons.open_in_new),
                    label: const Text('Mở trên YouTube'),
                  ),
                ],
                if (validImdb) ...[
                  const SizedBox(height: 22),
                  FilledButton.tonalIcon(
                    onPressed: () async {
                      final uri = Uri.https('www.imdb.com', '/title/$imdbId/');
                      try {
                        final opened = await launchUrl(
                          uri,
                          mode: LaunchMode.externalApplication,
                        );
                        if (opened) unawaited(_record('external_open'));
                        if (!opened && context.mounted) {
                          ScaffoldMessenger.of(context).showSnackBar(
                            const SnackBar(
                              content: Text('Không mở được trang IMDb.'),
                            ),
                          );
                        }
                      } catch (_) {
                        if (context.mounted) {
                          ScaffoldMessenger.of(context).showSnackBar(
                            const SnackBar(
                              content: Text('Không mở được trang IMDb.'),
                            ),
                          );
                        }
                      }
                    },
                    icon: const Icon(Icons.open_in_new),
                    label: const Text('Mở trang IMDb'),
                  ),
                ],
                if (content.isMusic) ...[
                  const SizedBox(height: 22),
                  if (_playerLoading) const LinearProgressIndicator(),
                  if (!_covered && !_ratingOpen && _embedUrl != null)
                    KeyedSubtree(
                      key: const ValueKey('spotify-inline-player'),
                      child: spotifyEmbed(_embedUrl!),
                    ),
                  if (_playerError != null) ...[
                    Text(_playerError!),
                    TextButton(
                      onPressed: _playerLoading ? null : _loadPlayer,
                      child: const Text('Thử lại trình phát'),
                    ),
                  ],
                  if (_sourceUrl != null)
                    TextButton.icon(
                      onPressed: _openSpotify,
                      icon: const Icon(Icons.open_in_new),
                      label: const Text('Mở trên Spotify'),
                    ),
                  const Text(
                    'Nghe trực tiếp bằng player phía trên. Thời lượng phát do Spotify cung cấp.',
                    style: TextStyle(fontSize: 12),
                  ),
                ],

                if (content.description != null) ...[
                  const SizedBox(height: 22),
                  const SectionHeading('Về nội dung này'),
                  Text(
                    content.description!,
                    style: Theme.of(context).textTheme.bodyMedium
                        ?.copyWith(height: 1.6, color: AppColors.textSecondary),
                  ),
                ],
                if (content.album != null)
                  _DetailRow(label: 'Album', value: content.album!),
                if (content.durationSeconds != null &&
                    content.durationSeconds! > 0)
                  _DetailRow(
                    label: 'Thời lượng',
                    value: _duration(content.durationSeconds!),
                  ),
                if (content.valence != null && content.arousal != null) ...[
                  const SizedBox(height: 16),
                  Card(
                    child: ExpansionTile(
                      key: PageStorageKey('content-tone-${widget.contentId}'),
                      title: const Text(
                        'Sắc thái nội dung',
                        style: TextStyle(fontSize: 14),
                      ),
                      subtitle: Text(
                        moodDescription(content.valence!, content.arousal!),
                        style: Theme.of(context).textTheme.bodySmall,
                      ),
                      childrenPadding: const EdgeInsets.fromLTRB(16, 0, 16, 16),
                      children: [
                        Text(
                          'Valence ${content.valence!.toStringAsFixed(2)} · Arousal ${content.arousal!.toStringAsFixed(2)}',
                          style: Theme.of(context).textTheme.bodySmall,
                        ),
                        const Text(
                          'Nhãn mô tả nội dung dùng cho gợi ý, không phải kết quả đo cảm xúc của bạn.',
                          style: TextStyle(fontSize: 12),
                        ),
                      ],
                    ),
                  ),
                ],
                const SizedBox(height: 24),
                Text(
                  'Trải nghiệm của bạn',
                  style: Theme.of(context).textTheme.titleMedium,
                ),
                const SizedBox(height: 8),
                const Text(
                  'Chia sẻ cảm nhận sau khi nghe hoặc tìm hiểu nội dung.',
                ),
                const SizedBox(height: 8),
                FilledButton.tonalIcon(
                  onPressed: _ratingOpen ? null : _rate,
                  icon: const Icon(Icons.star_outline),
                  label: const Text('Đánh giá nội dung'),
                ),
              ],
            ),
    );
  }

  String _duration(int seconds) =>
      '${seconds ~/ 60}:${(seconds % 60).toString().padLeft(2, '0')}';
}

class _DetailHeader extends StatelessWidget {
  const _DetailHeader({required this.content});
  final ContentItem content;
  @override
  Widget build(BuildContext context) => LayoutBuilder(
    builder: (context, constraints) {
      final width = (constraints.maxWidth * .34).clamp(88.0, 144.0);
      final artwork = ContentArtwork(
        url: content.imageUrl,
        music: content.isMusic,
        width: width,
        height: content.isMusic ? width : width * 1.5,
      );
      final metadata = Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            content.isMusic ? 'ÂM NHẠC' : 'ĐIỆN ẢNH',
            style: TextStyle(
              fontSize: 10,
              letterSpacing: 1.6,
              fontWeight: FontWeight.w700,
              color: Theme.of(context).colorScheme.primary,
            ),
          ),
          const SizedBox(height: 8),
          Text(content.title, style: Theme.of(context).textTheme.titleLarge),
          const SizedBox(height: 8),
          if (content.isMusic && content.artist != null)
            Text(
              content.artist!,
              style: Theme.of(context).textTheme.bodyMedium
                  ?.copyWith(color: AppColors.textSecondary),
            ),
          if (!content.isMusic)
            Text(
              'Đạo diễn: ${content.director ?? 'Chưa có thông tin'}',
              style: Theme.of(context).textTheme.bodySmall,
            ),
          if (content.genre != null)
            Padding(
              padding: const EdgeInsets.only(top: 8),
              child: Text(
                content.genre!,
                style: Theme.of(context).textTheme.bodySmall,
              ),
            ),
          if (content.rating != null)
            Padding(
              padding: const EdgeInsets.only(top: 10),
              child: Text(
                '★ ${content.rating} · ${content.ratingSource ?? 'Điểm đã lưu'}',
                style: const TextStyle(color: AppColors.warning, fontSize: 12),
              ),
            ),
        ],
      );
      if (MediaQuery.textScalerOf(context).scale(14) > 22) {
        return Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [artwork, const SizedBox(height: 16), metadata],
        );
      }
      return Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          artwork,
          const SizedBox(width: 18),
          Expanded(child: metadata),
        ],
      );
    },
  );
}

class _DetailRow extends StatelessWidget {
  const _DetailRow({required this.label, required this.value});
  final String label;
  final String value;

  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.symmetric(vertical: 7),
    child: Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        SizedBox(
          width: 120,
          child: Text(
            label,
            style: const TextStyle(color: AppColors.textSecondary),
          ),
        ),
        Expanded(child: Text(value)),
      ],
    ),
  );
}

class _DetailError extends StatelessWidget {
  const _DetailError({required this.message, required this.onRetry});
  final String message;
  final VoidCallback onRetry;

  @override
  Widget build(BuildContext context) => Center(
    child: Padding(
      padding: const EdgeInsets.all(28),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          const Icon(Icons.cloud_off, size: 42, color: AppColors.warning),
          const SizedBox(height: 12),
          Text(message, textAlign: TextAlign.center),
          const SizedBox(height: 12),
          OutlinedButton.icon(
            onPressed: onRetry,
            icon: const Icon(Icons.refresh),
            label: const Text('Thử lại'),
          ),
        ],
      ),
    ),
  );
}
