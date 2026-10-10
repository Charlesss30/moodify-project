import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:webview_flutter/webview_flutter.dart';

Widget buildYoutubeEmbed(String url) {
  if ((defaultTargetPlatform != TargetPlatform.android &&
          defaultTargetPlatform != TargetPlatform.iOS) ||
      WebViewPlatform.instance == null) {
    return const Center(
      child: Text('Dùng nút Mở trên YouTube để xem trên thiết bị này.'),
    );
  }
  return _YoutubeView(key: ValueKey(url), url: url);
}

class _YoutubeView extends StatefulWidget {
  const _YoutubeView({super.key, required this.url});
  final String url;
  @override
  State<_YoutubeView> createState() => _YoutubeViewState();
}

class _YoutubeViewState extends State<_YoutubeView> {
  late final WebViewController _controller;
  bool _failed = false;
  @override
  void initState() {
    super.initState();
    _controller = WebViewController()
      ..setJavaScriptMode(JavaScriptMode.unrestricted)
      ..setBackgroundColor(Colors.black)
      ..setNavigationDelegate(
        NavigationDelegate(
          onNavigationRequest: (r) {
            final uri = Uri.tryParse(r.url);
            return !r.isMainFrame ||
                    r.url == 'about:blank' ||
                    (uri?.scheme == 'https' &&
                        uri?.host == 'www.youtube.com' &&
                        uri!.path.startsWith('/embed/'))
                ? NavigationDecision.navigate
                : NavigationDecision.prevent;
          },
          onWebResourceError: (e) {
            if (mounted && e.isForMainFrame == true) {
              setState(() => _failed = true);
            }
          },
        ),
      );
    _load();
  }

  Future<void> _load() async {
    try {
      // Application identity as required by YouTube for native WebViews (avoids missing-referrer error 153).
      const appId = String.fromEnvironment(
        'YOUTUBE_APP_ID',
        defaultValue: 'com.example.moodify_application',
      );
      await _controller.loadRequest(
        Uri.parse(widget.url),
        headers: {'Referer': 'https://$appId/'},
      );
    } catch (_) {
      if (mounted) setState(() => _failed = true);
    }
  }

  @override
  void dispose() {
    _controller.loadRequest(Uri.parse('about:blank')).catchError((Object _) {});
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => _failed
      ? const Center(
          child: Text(
            'Không tải được player. Hãy mở trailer trên YouTube bên dưới.',
          ),
        )
      : WebViewWidget(controller: _controller);
}
