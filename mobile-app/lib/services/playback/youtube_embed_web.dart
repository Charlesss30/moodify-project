import 'package:flutter/material.dart';
import 'package:web/web.dart' as web;

Widget buildYoutubeEmbed(String url) =>
    _YoutubeFrame(key: ValueKey(url), url: url);

class _YoutubeFrame extends StatefulWidget {
  const _YoutubeFrame({super.key, required this.url});
  final String url;
  @override
  State<_YoutubeFrame> createState() => _YoutubeFrameState();
}

class _YoutubeFrameState extends State<_YoutubeFrame> {
  web.HTMLIFrameElement? _frame;
  @override
  Widget build(BuildContext context) => HtmlElementView.fromTagName(
    tagName: 'iframe',
    onElementCreated: (element) {
      final f = element as web.HTMLIFrameElement;
      _frame = f;
      f.src = widget.url;
      f.title = 'YouTube trailer';
      f.style.width = '100%';
      f.style.height = '100%';
      f.style.border = '0';
      f.allow = 'encrypted-media; fullscreen; picture-in-picture';
      f.setAttribute('allowfullscreen', '');
      f.setAttribute('referrerpolicy', 'strict-origin-when-cross-origin');
    },
  );
  @override
  void dispose() {
    _frame?.src = 'about:blank';
    _frame?.remove();
    super.dispose();
  }
}
