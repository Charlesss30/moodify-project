import 'dart:async';
import 'dart:math' as math;
import 'package:flutter/material.dart';
import 'package:just_audio/just_audio.dart';
import '../core/api_service.dart';

class SongDetailScreen extends StatefulWidget {
  final String contentId;
  const SongDetailScreen({super.key, required this.contentId});
  @override
  State<SongDetailScreen> createState() => _SongDetailScreenState();
}

class _SongDetailScreenState extends State<SongDetailScreen> {
  final AudioPlayer _player = AudioPlayer();
  StreamSubscription<Duration>? _positionSubscription;
  Map<String, dynamic>? _content;
  String? _error;
  bool _loading = true;
  bool _busy = false;
  bool _needsUrl = true;
  Duration _limit = const Duration(seconds: 30);

  @override
  void initState() {
    super.initState();
    _positionSubscription = _player.positionStream.listen((position) {
      if (position >= _limit && _player.playing) unawaited(_player.pause());
    });
    _load();
  }

  Future<void> _load() async {
    setState(() { _loading = true; _error = null; });
    try {
      final data = await ApiService.request('/Music/${Uri.encodeComponent(widget.contentId)}');
      if (mounted) setState(() => _content = Map<String, dynamic>.from(data as Map));
    } catch (e) {
      if (mounted) setState(() => _error = e.toString());
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  Future<void> _toggle() async {
    if (_busy) return;
    if (_player.playing) { await _player.pause(); return; }
    setState(() { _busy = true; _error = null; });
    try {
      if (_needsUrl) {
        final data = await ApiService.request('/Music/${Uri.encodeComponent(widget.contentId)}/preview');
        if (!mounted) return;
        _limit = Duration(seconds: math.min((data['previewSeconds'] as num).toInt(), 30));
        await _player.setUrl(data['url'] as String);
        _needsUrl = false;
      }
      if (!mounted) return;
      if (_player.position >= _limit || _player.processingState == ProcessingState.completed) {
        await _player.seek(Duration.zero);
      }
      unawaited(_player.play().catchError((Object e) {
        if (mounted) setState(() { _needsUrl = true; _error = 'Không phát được bài hát. Bấm nghe thử để lấy link mới.'; });
      }));
    } catch (e) {
      if (mounted) setState(() { _needsUrl = true; _error = e.toString(); });
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  void dispose() {
    _positionSubscription?.cancel();
    unawaited(_player.dispose());
    super.dispose();
  }

  String _time(int seconds) => '${seconds ~/ 60}:${(seconds % 60).toString().padLeft(2, '0')}';

  @override
  Widget build(BuildContext context) {
    final song = (_content?['nhac'] as Map?) ?? {};
    final canPreview = song['source'] == 'ZingMP3';
    return Scaffold(
      appBar: AppBar(title: const Text('Thông tin bài hát')),
      body: _loading ? const Center(child: CircularProgressIndicator()) : ListView(
        padding: const EdgeInsets.all(24),
        children: [
          if (_content != null) ...[
            if (_content!['hinhAnh'] != null)
              ClipRRect(borderRadius: BorderRadius.circular(18), child: Image.network(
                _content!['hinhAnh'] as String, height: 260, fit: BoxFit.cover,
                errorBuilder: (_, error, stack) => const SizedBox(height: 160, child: Icon(Icons.music_note, size: 60)),
              )),
            const SizedBox(height: 20),
            Text(_content!['tieuDe'] as String, style: Theme.of(context).textTheme.headlineSmall),
            const SizedBox(height: 8),
            Text(song['tenNgheSi']?.toString() ?? 'Chưa cập nhật nghệ sĩ'),
            const SizedBox(height: 12),
            if (song['album'] != null) Text('Album: ${song['album']}'),
            if (song['genre'] != null) Text('Thể loại: ${song['genre']}'),
            if (song['releaseDate'] != null) Text('Phát hành: ${song['releaseDate']}'),
            if (song['duration'] != null) Text('Thời lượng: ${_time((song['duration'] as num).toInt())}'),
            if (canPreview) ...[
              const SizedBox(height: 20),
              const Text('Nghe thử tối đa 30 giây · Nguồn Zing MP3'),
              StreamBuilder<Duration>(
                stream: _player.positionStream,
                builder: (context, snapshot) {
                  final maxSeconds = math.min((_player.duration ?? _limit).inSeconds, _limit.inSeconds).toDouble();
                  final seconds = (snapshot.data ?? Duration.zero).inMilliseconds / 1000;
                  return Column(children: [
                    Slider(value: seconds.clamp(0, maxSeconds), max: maxSeconds > 0 ? maxSeconds : 30,
                      onChanged: _needsUrl || _busy ? null : (value) => _player.seek(Duration(milliseconds: (value * 1000).round()))),
                    Text('${_time(seconds.clamp(0, maxSeconds).floor())} / ${_time(maxSeconds.floor())}'),
                  ]);
                },
              ),
              StreamBuilder<PlayerState>(
                stream: _player.playerStateStream,
                builder: (context, snapshot) => FilledButton.icon(
                  onPressed: _busy ? null : _toggle,
                  icon: _busy ? const SizedBox(width: 18, height: 18, child: CircularProgressIndicator(strokeWidth: 2))
                    : Icon(snapshot.data?.playing == true ? Icons.pause : Icons.play_arrow),
                  label: Text(_busy ? 'Đang tải…' : snapshot.data?.playing == true ? 'Tạm dừng' : 'Nghe thử'),
                ),
              ),
            ] else const Padding(padding: EdgeInsets.only(top: 20), child: Text('Bài hát chưa có nguồn nghe thử.')),
          ],
          if (_error != null) ...[
            const SizedBox(height: 16),
            Text(_error!, style: TextStyle(color: Theme.of(context).colorScheme.error)),
            if (_content == null) TextButton(onPressed: _load, child: const Text('Thử lại')),
          ],
        ],
      ),
    );
  }
}
