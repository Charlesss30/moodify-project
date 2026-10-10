import 'package:flutter/material.dart';

import 'spotify_embed_native.dart'
    if (dart.library.js_interop) 'spotify_embed_web.dart'
    as platform;

bool validSpotifyUrl(String? value, {bool embed = false}) {
  final uri = Uri.tryParse(value ?? '');
  return uri != null &&
      uri.scheme == 'https' &&
      uri.host == 'open.spotify.com' &&
      uri.userInfo.isEmpty &&
      (!uri.hasPort || uri.port == 443) &&
      RegExp(
        embed ? r'^/embed/track/[a-zA-Z0-9]{22}$' : r'^/track/[a-zA-Z0-9]{22}$',
      ).hasMatch(uri.path);
}

Widget spotifyEmbed(String url) => validSpotifyUrl(url, embed: true)
    ? platform.buildSpotifyEmbed(url)
    : const Text('Địa chỉ trình phát không hợp lệ.');
