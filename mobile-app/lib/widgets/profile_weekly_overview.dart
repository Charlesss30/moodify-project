import 'dart:async';

import 'package:flutter/material.dart';

import '../core/api_service.dart';
import '../core/navigation.dart';
import '../models/personal_models.dart';
import 'app_widgets.dart';

class ProfileWeeklyOverview extends StatefulWidget {
  const ProfileWeeklyOverview({
    super.key,
    required this.onOpenStatistics,
    this.active = true,
  });
  final VoidCallback onOpenStatistics;
  final bool active;
  @override
  State<ProfileWeeklyOverview> createState() => _ProfileWeeklyOverviewState();
}

class _ProfileWeeklyOverviewState extends State<ProfileWeeklyOverview>
    with RouteAware, WidgetsBindingObserver {
  WeeklySummary? _summary;
  String? _error;
  bool _loading = false, _dirty = false, _covered = false;
  ModalRoute<dynamic>? _route;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    ApiService.activityRevision.addListener(_invalidate);
    if (widget.active) {
      _load();
    } else {
      _dirty = true;
    }
  }

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    final route = ModalRoute.of(context);
    if (_route != route) {
      appRouteObserver.unsubscribe(this);
      _route = route;
      if (route != null) {
        appRouteObserver.subscribe(this, route);
      }
    }
  }

  @override
  void didUpdateWidget(covariant ProfileWeeklyOverview oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (widget.active && !oldWidget.active) {
      _invalidate();
    }
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed) {
      _invalidate();
    }
  }

  @override
  void didPushNext() {
    _covered = true;
  }

  @override
  void didPopNext() {
    _covered = false;
    if (_dirty && widget.active) {
      _load();
    }
  }

  void _invalidate() {
    _dirty = true;
    if (widget.active && !_covered) {
      _load();
    }
  }

  Future<void> _load() async {
    if (_loading || !mounted) {
      return;
    }
    _dirty = false;
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final summary = await ApiService.weeklySummary();
      if (mounted) {
        setState(() => _summary = summary);
      }
    } on ApiException catch (error) {
      if (mounted) {
        setState(() => _error = error.message);
      }
    } finally {
      if (mounted) {
        setState(() => _loading = false);
        if (_dirty && widget.active && !_covered) {
          unawaited(_load());
        }
      }
    }
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    ApiService.activityRevision.removeListener(_invalidate);
    appRouteObserver.unsubscribe(this);
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final summary = _summary;
    return Card(
      clipBehavior: Clip.antiAlias,
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                Expanded(
                  child: Text(
                    'Tổng quan 7 ngày',
                    style: Theme.of(context).textTheme.titleSmall,
                  ),
                ),
                IconButton(
                  tooltip: 'Cập nhật tổng quan',
                  onPressed: _loading ? null : _load,
                  icon: const Icon(Icons.refresh, size: 19),
                ),
              ],
            ),
            if (_loading && summary == null)
              const Padding(
                padding: EdgeInsets.symmetric(vertical: 24),
                child: Center(child: CircularProgressIndicator(strokeWidth: 2)),
              )
            else if (summary != null) ...[
              const SizedBox(height: 8),
              Row(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  _metric(
                    Icons.calendar_today_outlined,
                    summary.moodDays,
                    'Ngày ghi nhận',
                  ),
                  _metric(
                    Icons.play_circle_outline,
                    summary.openedContents,
                    'Nội dung đã mở',
                  ),
                  _metric(Icons.star_outline, summary.ratings, 'Lượt đánh giá'),
                ],
              ),
              const SizedBox(height: 12),
              Text(
                'Bao gồm hôm nay · Mỗi nội dung đã mở được tính một lần.',
                style: Theme.of(context).textTheme.bodySmall,
              ),
              if (_loading)
                const Padding(
                  padding: EdgeInsets.only(top: 8),
                  child: LinearProgressIndicator(),
                ),
            ],
            if (_error != null) StatusCard(message: _error!, onRetry: _load),
            const SizedBox(height: 8),
            TextButton.icon(
              onPressed: widget.onOpenStatistics,
              icon: const Icon(Icons.insights_outlined, size: 18),
              label: const Text('Thống kê tâm trạng'),
            ),
          ],
        ),
      ),
    );
  }

  Widget _metric(IconData icon, int value, String label) => Expanded(
    child: Semantics(
      label: '$value $label',
      excludeSemantics: true,
      child: Column(
        children: [
          Icon(icon, size: 21, color: Theme.of(context).colorScheme.primary),
          const SizedBox(height: 8),
          Text('$value', style: Theme.of(context).textTheme.titleLarge),
          const SizedBox(height: 4),
          Text(
            label,
            textAlign: TextAlign.center,
            style: Theme.of(context).textTheme.bodySmall,
          ),
        ],
      ),
    ),
  );
}
