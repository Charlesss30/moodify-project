import 'spotify_audio_guard.dart';

import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:webview_flutter/webview_flutter.dart';

Widget buildSpotifyEmbed(String url) {
  if ((defaultTargetPlatform != TargetPlatform.android &&
          defaultTargetPlatform != TargetPlatform.iOS) ||
      WebViewPlatform.instance == null) {
    return const Text('Mở Spotify để nghe trên thiết bị này.');
  }
  return _SpotifyPlayer(url: url);
}

class _SpotifyPlayer extends StatefulWidget {
  const _SpotifyPlayer({required this.url});
  final String url;
  @override
  State<_SpotifyPlayer> createState() => _SpotifyPlayerState();
}

class _SpotifyPlayerState extends State<_SpotifyPlayer>
    with WidgetsBindingObserver {
  late final WebViewController _controller;
  String? _error;
  bool _loading = true;
  bool _backgrounded = false;
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    _controller = WebViewController()
      ..setJavaScriptMode(JavaScriptMode.unrestricted)
      ..setBackgroundColor(const Color(0xFF121212))
      ..setNavigationDelegate(
        NavigationDelegate(
          onNavigationRequest: (request) {
            final uri = Uri.tryParse(request.url);
            if (!request.isMainFrame ||
                request.url == 'about:blank' ||
                (uri?.scheme == 'https' &&
                    uri?.host == 'open.spotify.com' &&
                    uri!.path.startsWith('/embed/track/'))) {
              return NavigationDecision.navigate;
            }
            return NavigationDecision.prevent;
          },
          onPageFinished: (url) {
            if (mounted && url != 'about:blank') {
              _controller
                  .runJavaScript(spotifyAudioGuard)
                  .catchError((Object _) {});
              setState(() => _loading = false);
            }
          },
          onWebResourceError: (e) {
            if (mounted && e.isForMainFrame == true) {
              setState(() {
                _error = 'Không tải được Spotify player.';
                _loading = false;
              });
            }
          },
        ),
      );
    _load();
  }

  Future<void> _load() async {
    setState(() {
      _error = null;
      _loading = true;
    });
    try {
      await _controller.loadRequest(Uri.parse(widget.url));
    } catch (_) {
      if (mounted) {
        setState(() {
          _error = 'Không tải được Spotify player.';
          _loading = false;
        });
      }
    }
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.paused) {
      _backgrounded = true;
      _controller
          .loadRequest(Uri.parse('about:blank'))
          .catchError((Object _) {});
    } else if (state == AppLifecycleState.resumed && _backgrounded) {
      _backgrounded = false;
      _load();
    }
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    _controller.loadRequest(Uri.parse('about:blank')).catchError((Object _) {});
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => Column(
    children: [
      if (_loading) const LinearProgressIndicator(),
      SizedBox(height: 152, child: WebViewWidget(controller: _controller)),
      if (_error != null) ...[
        Text(_error!),
        TextButton(onPressed: _load, child: const Text('Thử lại Spotify')),
      ],
    ],
  );
}
