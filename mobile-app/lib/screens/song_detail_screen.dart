import 'package:flutter/material.dart';

import 'content_detail_screen.dart';

class SongDetailScreen extends StatelessWidget {
  const SongDetailScreen({super.key, required this.contentId});
  final String contentId;
  @override
  Widget build(BuildContext context) =>
      ContentDetailScreen(contentId: contentId, type: 'Music');
}
