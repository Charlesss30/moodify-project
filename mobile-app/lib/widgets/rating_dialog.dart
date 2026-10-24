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
  Widget build(BuildContext context) => PopScope(
    canPop: !_saving,
    child: AlertDialog(
      title: Text(
        'Đánh giá ${widget.title}',
        maxLines: 2,
        overflow: TextOverflow.ellipsis,
      ),
      content: SingleChildScrollView(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Wrap(
              alignment: WrapAlignment.center,
              children: List.generate(
                5,
                (i) => IconButton(
                  tooltip: '${i + 1} sao',
                  onPressed: _saving
                      ? null
                      : () => setState(() => _stars = i + 1),
                  icon: Icon(
                    i < _stars ? Icons.star : Icons.star_outline,
                    color: Colors.amber,
                  ),
                ),
              ),
            ),
            TextField(
              controller: _comment,
              hintLocales: const [Locale('vi', 'VN'), Locale('en')],
              keyboardType: TextInputType.multiline,
              textCapitalization: TextCapitalization.sentences,
              enableSuggestions: true,
              enabled: !_saving,
              maxLength: 2000,
              maxLines: 3,
              decoration: const InputDecoration(
                labelText: 'Nhận xét (không bắt buộc)',
              ),
            ),
            if (_error != null)
              Text(
                _error!,
                style: TextStyle(color: Theme.of(context).colorScheme.error),
              ),
          ],
        ),
      ),
      actions: [
        TextButton(
          onPressed: _saving ? null : () => Navigator.pop(context),
          child: const Text('Hủy'),
        ),
        FilledButton(
          onPressed: _saving ? null : _submit,
          child: Text(_saving ? 'Đang gửi…' : 'Gửi đánh giá'),
        ),
      ],
    ),
  );
}
