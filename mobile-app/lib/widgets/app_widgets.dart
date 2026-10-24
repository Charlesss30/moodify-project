import 'package:flutter/material.dart';

import '../core/colors.dart';
import '../models/mobile_models.dart';

/// Bound image decoding to the displayed size; reuse Flutter's memory image cache.
class ContentArtwork extends StatelessWidget {
  const ContentArtwork({
    super.key,
    required this.url,
    this.music = false,
    this.width,
    this.height,
    this.radius = 14,
  });
  final String? url;
  final bool music;
  final double? width, height;
  final double radius;
  @override
  Widget build(BuildContext context) {
    final uri = Uri.tryParse(url ?? '');
    final valid =
        uri != null && (uri.scheme == 'https' || uri.scheme == 'http');
    final fallback = ColoredBox(
      color: AppColors.surfaceSecondary,
      child: Center(
        child: Icon(
          music ? Icons.music_note_rounded : Icons.movie_outlined,
          color: AppColors.textSecondary,
          size: 32,
        ),
      ),
    );
    return ClipRRect(
      borderRadius: BorderRadius.circular(radius),
      child: SizedBox(
        width: width,
        height: height,
        child: valid
            ? LayoutBuilder(
                builder: (context, constraints) {
                  final logicalWidth = width ?? constraints.maxWidth;
                  final decodeWidth = logicalWidth.isFinite
                      ? (logicalWidth * MediaQuery.devicePixelRatioOf(context))
                            .round()
                            .clamp(80, 1200)
                      : 600;
                  return Image.network(
                    url!,
                    fit: BoxFit.cover,
                    cacheWidth: decodeWidth,
                    errorBuilder: (_, _, _) => fallback,
                    frameBuilder: (_, child, frame, synchronous) =>
                        frame == null && !synchronous ? fallback : child,
                  );
                },
              )
            : fallback,
      ),
    );
  }
}

class SectionHeading extends StatelessWidget {
  const SectionHeading(this.title, {super.key, this.subtitle, this.trailing});
  final String title;
  final String? subtitle;
  final Widget? trailing;
  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.only(bottom: 14),
    child: Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(title, style: Theme.of(context).textTheme.titleLarge),
              if (subtitle != null) ...[
                const SizedBox(height: 4),
                Text(subtitle!, style: Theme.of(context).textTheme.bodySmall),
              ],
            ],
          ),
        ),
        ?trailing,
      ],
    ),
  );
}

class StatusCard extends StatelessWidget {
  const StatusCard({
    super.key,
    required this.message,
    this.title,
    this.onRetry,
    this.icon = Icons.info_outline,
    this.actionLabel = 'Thử lại',
  });
  final String message, actionLabel;
  final String? title;
  final VoidCallback? onRetry;
  final IconData icon;
  @override
  Widget build(BuildContext context) => Card(
    child: Padding(
      padding: const EdgeInsets.all(18),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Icon(
                icon,
                color: Theme.of(context).colorScheme.primary,
                size: 22,
              ),
              const SizedBox(width: 12),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    if (title != null) ...[
                      Text(
                        title!,
                        style: Theme.of(context).textTheme.titleSmall,
                      ),
                      const SizedBox(height: 4),
                    ],
                    Text(message, style: Theme.of(context).textTheme.bodySmall),
                  ],
                ),
              ),
            ],
          ),
          if (onRetry != null)
            Padding(
              padding: const EdgeInsets.only(top: 10),
              child: TextButton.icon(
                onPressed: onRetry,
                icon: const Icon(Icons.refresh, size: 18),
                label: Text(actionLabel),
              ),
            ),
        ],
      ),
    ),
  );
}

/// Static placeholders avoid a continuously ticking shimmer.
class LoadingCards extends StatelessWidget {
  const LoadingCards({super.key, this.count = 3});
  final int count;
  @override
  Widget build(BuildContext context) => Semantics(
    label: 'Đang tải nội dung',
    liveRegion: true,
    child: Column(
      children: List.generate(
        count,
        (_) => Padding(
          padding: const EdgeInsets.only(bottom: 12),
          child: Container(
            height: 92,
            padding: const EdgeInsets.all(14),
            decoration: BoxDecoration(
              color: AppColors.surface,
              borderRadius: BorderRadius.circular(18),
            ),
            child: Row(
              children: [
                Container(
                  width: 56,
                  decoration: BoxDecoration(
                    color: AppColors.surfaceSecondary,
                    borderRadius: BorderRadius.circular(10),
                  ),
                ),
                const SizedBox(width: 14),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      const SizedBox(height: 8),
                      Container(
                        height: 12,
                        decoration: BoxDecoration(
                          color: AppColors.surfaceSecondary,
                          borderRadius: BorderRadius.circular(4),
                        ),
                      ),
                      const SizedBox(height: 12),
                      FractionallySizedBox(
                        widthFactor: .6,
                        child: Container(
                          height: 10,
                          decoration: BoxDecoration(
                            color: AppColors.surfaceSecondary,
                            borderRadius: BorderRadius.circular(4),
                          ),
                        ),
                      ),
                    ],
                  ),
                ),
              ],
            ),
          ),
        ),
      ),
    ),
  );
}

class ContentListTile extends StatelessWidget {
  const ContentListTile({
    super.key,
    required this.item,
    required this.onTap,
    this.footnote,
  });
  final ContentItem item;
  final VoidCallback onTap;
  final String? footnote;
  @override
  Widget build(BuildContext context) => Card(
    clipBehavior: Clip.antiAlias,
    child: InkWell(
      onTap: onTap,
      child: Padding(
        padding: const EdgeInsets.all(12),
        child: Row(
          children: [
            ContentArtwork(
              url: item.imageUrl,
              music: item.isMusic,
              width: 64,
              height: item.isMusic ? 64 : 88,
              radius: 10,
            ),
            const SizedBox(width: 14),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    item.title,
                    maxLines: 2,
                    overflow: TextOverflow.ellipsis,
                    style: Theme.of(context).textTheme.titleSmall,
                  ),
                  if ((item.isMusic ? item.artist : item.director) != null) ...[
                    const SizedBox(height: 3),
                    Text(
                      item.isMusic
                          ? item.artist!
                          : 'Đạo diễn: ${item.director}',
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      style: Theme.of(context).textTheme.bodySmall,
                    ),
                  ],
                  if (item.genre != null)
                    Text(
                      item.genre!,
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      style: Theme.of(context).textTheme.bodySmall,
                    ),
                  if (footnote != null) ...[
                    const SizedBox(height: 6),
                    Text(
                      footnote!,
                      style: TextStyle(
                        fontSize: 12,
                        color: Theme.of(context).colorScheme.primary,
                      ),
                    ),
                  ],
                ],
              ),
            ),
            const SizedBox(width: 4),
            const Icon(
              Icons.chevron_right,
              color: AppColors.textSecondary,
              size: 20,
            ),
          ],
        ),
      ),
    ),
  );
}

class EntryFade extends StatelessWidget {
  const EntryFade({super.key, required this.child});
  final Widget child;
  @override
  Widget build(BuildContext context) {
    if (MediaQuery.disableAnimationsOf(context)) return child;
    return TweenAnimationBuilder<double>(
      tween: Tween(begin: 0, end: 1),
      duration: const Duration(milliseconds: 220),
      curve: Curves.easeOutCubic,
      child: child,
      builder: (_, value, child) => Opacity(
        opacity: value,
        child: Transform.translate(
          offset: Offset(0, (1 - value) * 8),
          child: child,
        ),
      ),
    );
  }
}
