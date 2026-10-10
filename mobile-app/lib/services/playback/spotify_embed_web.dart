import 'package:flutter/material.dart';
import 'package:web/web.dart' as web;

Widget buildSpotifyEmbed(String url) => _Embed(key: ValueKey(url), url: url);

class _Embed extends StatefulWidget {
  const _Embed({super.key, required this.url});
  final String url;
  @override
  State<_Embed> createState() => _EmbedState();
}

class _EmbedState extends State<_Embed> {
  web.HTMLIFrameElement? _frame;
  @override
  Widget build(BuildContext context) => SizedBox(
    height: 152,
    child: HtmlElementView.fromTagName(
      tagName: 'iframe',
      onElementCreated: (element) {
        final frame = element as web.HTMLIFrameElement;
        _frame = frame;
        frame.src = widget.url;
        frame.title = 'Spotify player';
        frame.style.width = '100%';
        frame.style.height = '152px';
        frame.style.border = '0';
        frame.style.borderRadius = '12px';
        frame.allow = 'encrypted-media; fullscreen; picture-in-picture';
      },
    ),
  );
  @override
  void dispose() {
    _frame?.src = 'about:blank';
    _frame?.remove();
    super.dispose();
  }
}
