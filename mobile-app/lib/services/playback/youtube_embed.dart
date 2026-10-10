import 'dart:math' as math;

import 'package:flutter/material.dart';

import 'youtube_embed_native.dart'
    if (dart.library.js_interop) 'youtube_embed_web.dart'
    as platform;

String? youtubeVideoId(String? value) {
  final uri = Uri.tryParse(value ?? '');
  if (uri == null ||
      uri.scheme != 'https' ||
      uri.userInfo.isNotEmpty ||
      (uri.hasPort && uri.port != 443)) {
    return null;
  }
  String? id;
  if (uri.host == 'youtu.be' && uri.pathSegments.length == 1) {
    id = uri.pathSegments.single;
  }
  if (['www.youtube.com', 'youtube.com'].contains(uri.host) &&
      uri.path == '/watch' &&
      uri.queryParametersAll['v']?.length == 1) {
    id = uri.queryParameters['v'];
  }
  return id != null && RegExp(r'^[A-Za-z0-9_-]{11}$').hasMatch(id) ? id : null;
}

class YoutubePlayer extends StatefulWidget {
  const YoutubePlayer({super.key, required this.videoId});
  final String videoId;
  @override
  State<YoutubePlayer> createState() => _YoutubePlayerState();
}

class _YoutubePlayerState extends State<YoutubePlayer>
    with WidgetsBindingObserver {
  bool _active = true;
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed) {
      if (mounted) setState(() => _active = true);
    } else if (state == AppLifecycleState.paused ||
        state == AppLifecycleState.hidden ||
        state == AppLifecycleState.detached) {
      if (mounted) setState(() => _active = false);
    }
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    if (!RegExp(r'^[A-Za-z0-9_-]{11}$').hasMatch(widget.videoId)) {
      return const Text('Trailer không hợp lệ.');
    }
    return LayoutBuilder(
      builder: (context, size) => SizedBox(
        height: math.max(200, size.maxWidth * 9 / 16),
        child: _active
            ? platform.buildYoutubeEmbed(
                'https://www.youtube.com/embed/${widget.videoId}?playsinline=1&autoplay=0&rel=0',
              )
            : const SizedBox.shrink(),
      ),
    );
  }
}
