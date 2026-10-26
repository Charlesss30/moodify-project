import 'package:flutter/material.dart';

import '../core/api_service.dart';

class RatingDialog extends StatefulWidget {
  const RatingDialog({
    super.key,
    required this.contentId,
    required this.title,
    this.recommendationId,
  });
  final String contentId, title;
  final String? recommendationId;
  @override
  State<RatingDialog> createState() => _RatingDialogState();
}

class _RatingDialogState extends State<RatingDialog> {
  final _comment = TextEditingController();
  int _stars = 5;
  bool _saving = false;
  String? _error;
  @override
  void dispose() {
    _comment.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    if (_saving) return;
    setState(() {
      _saving = true;
      _error = null;
    });
    try {
      await ApiService.rate(
        widget.contentId,
        _stars,
        _comment.text.trim(),
        recommendationId: widget.recommendationId,
      );
      if (!mounted) return;
      setState(() => _saving = false);
      Navigator.pop(context, _stars);
    } on ApiException catch (e) {
      if (mounted) {
        setState(() {
          _saving = false;
          _error = e.message;
        });
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colors = theme.colorScheme;
    final satisfaction = const [
      'Rất chưa hài lòng',
      'Chưa hài lòng',
      'Bình thường',
      'Hài lòng',
      'Rất hài lòng',
    ][_stars - 1];

    return PopScope(
      canPop: !_saving,
      child: AlertDialog(
        insetPadding: const EdgeInsets.symmetric(horizontal: 20, vertical: 24),
        titlePadding: const EdgeInsets.fromLTRB(20, 22, 20, 0),
        contentPadding: const EdgeInsets.fromLTRB(20, 20, 20, 0),
        actionsPadding: const EdgeInsets.all(20),
        scrollable: true,
        title: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text('Đánh giá trải nghiệm', style: theme.textTheme.titleLarge),
            const SizedBox(height: 6),
            Text(
              widget.title,
              maxLines: 2,
              overflow: TextOverflow.ellipsis,
              style: theme.textTheme.bodyMedium?.copyWith(
                color: colors.onSurfaceVariant,
              ),
            ),
          ],
        ),
        content: SizedBox(
          width: 360,
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Container(
                padding: const EdgeInsets.fromLTRB(8, 10, 8, 14),
                decoration: BoxDecoration(
                  color: colors.surfaceContainer,
                  borderRadius: BorderRadius.circular(18),
                  border: Border.all(
                    color: colors.outline.withValues(alpha: .35),
                  ),
                ),
                child: Column(
                  children: [
                    Row(
                      children: List.generate(
                        5,
                        (i) => Expanded(
                          child: IconButton(
                            tooltip: '${i + 1} sao',
                            padding: EdgeInsets.zero,
                            onPressed: _saving
                                ? null
                                : () => setState(() => _stars = i + 1),
                            iconSize: 30,
                            icon: Icon(
                              i < _stars
                                  ? Icons.star_rounded
                                  : Icons.star_outline_rounded,
                              color: i < _stars
                                  ? const Color(0xFFFFC857)
                                  : colors.onSurfaceVariant,
                            ),
                          ),
                        ),
                      ),
                    ),
                    const SizedBox(height: 4),
                    Semantics(
                      liveRegion: true,
                      child: AnimatedSwitcher(
                        duration: const Duration(milliseconds: 160),
                        child: Text(
                          '$_stars/5 · $satisfaction',
                          key: ValueKey(_stars),
                          textAlign: TextAlign.center,
                          style: theme.textTheme.labelLarge?.copyWith(
                            color: colors.onSurface,
                          ),
                        ),
                      ),
                    ),
                  ],
                ),
              ),
              const SizedBox(height: 20),
              Wrap(
                spacing: 8,
                runSpacing: 4,
                crossAxisAlignment: WrapCrossAlignment.center,
                children: [
                  Text('Nhận xét', style: theme.textTheme.titleSmall),
                  Text(
                    'Không bắt buộc',
                    style: theme.textTheme.bodySmall?.copyWith(
                      color: colors.onSurfaceVariant,
                    ),
                  ),
                ],
              ),
              const SizedBox(height: 10),
              TextField(
                controller: _comment,
                hintLocales: const [Locale('vi', 'VN'), Locale('en')],
                keyboardType: TextInputType.multiline,
                textCapitalization: TextCapitalization.sentences,
                enableSuggestions: true,
                enabled: !_saving,
                maxLength: 2000,
                minLines: 3,
                maxLines: 4,
                decoration: const InputDecoration(
                  hintText: 'Chia sẻ cảm nhận của bạn về nội dung này…',
                  hintMaxLines: 2,
                  contentPadding: EdgeInsets.all(14),
                ),
              ),
              if (_error != null) ...[
                const SizedBox(height: 8),
                Container(
                  padding: const EdgeInsets.all(12),
                  decoration: BoxDecoration(
                    color: colors.error.withValues(alpha: .08),
                    borderRadius: BorderRadius.circular(12),
                  ),
                  child: Text(
                    _error!,
                    style: theme.textTheme.bodySmall?.copyWith(
                      color: colors.error,
                    ),
                  ),
                ),
              ],
            ],
          ),
        ),
        actions: [
          SizedBox(
            width: 360,
            child: Row(
              children: [
                Expanded(
                  child: OutlinedButton(
                    onPressed: _saving ? null : () => Navigator.pop(context),
                    style: OutlinedButton.styleFrom(
                      minimumSize: const Size(0, 50),
                      padding: const EdgeInsets.symmetric(
                        horizontal: 10,
                        vertical: 13,
                      ),
                    ),
                    child: const Text('Hủy', textAlign: TextAlign.center),
                  ),
                ),
                const SizedBox(width: 12),
                Expanded(
                  child: FilledButton(
                    onPressed: _saving ? null : _submit,
                    style: FilledButton.styleFrom(
                      minimumSize: const Size(0, 50),
                      padding: const EdgeInsets.symmetric(
                        horizontal: 10,
                        vertical: 13,
                      ),
                    ),
                    child: Text(
                      _saving ? 'Đang gửi…' : 'Gửi đánh giá',
                      textAlign: TextAlign.center,
                    ),
                  ),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}
