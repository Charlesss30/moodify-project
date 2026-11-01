import 'package:flutter/material.dart';

import '../core/api_service.dart';
import '../services/notifications/push_notifications.dart';
import '../widgets/app_widgets.dart';

class NotificationSettingsScreen extends StatefulWidget {
  const NotificationSettingsScreen({super.key});
  @override
  State<NotificationSettingsScreen> createState() =>
      _NotificationSettingsScreenState();
}

class _NotificationSettingsScreenState
    extends State<NotificationSettingsScreen> {
  Map<String, dynamic>? _settings;
  bool _busy = false, _loading = true;
  String? _error;
  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final data = await ApiService.request('/Notifications/settings');
      if (data is! Map || data['enabled'] is! bool) {
        throw const ApiException('Chưa đọc được cài đặt thông báo.');
      }
      if (mounted) setState(() => _settings = Map<String, dynamic>.from(data));
    } on ApiException catch (error) {
      if (mounted) setState(() => _error = error.message);
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  bool get _enabled => _settings?['enabled'] == true;
  bool get _available =>
      _settings?['deliveryAvailable'] == true && PushNotifications.available;

  Future<void> _save(Map<String, dynamic> changes) async {
    if (_busy || _settings == null) return;
    final next = {
      ..._settings!,
      ...changes,
      'offsetMinutes': DateTime.now().timeZoneOffset.inMinutes,
    };
    setState(() {
      _busy = true;
      _error = null;
    });
    final wasEnabled = _enabled;
    final session = ApiService.sessionRevision;
    var serverSaved = false;
    try {
      String? fid;
      if (next['enabled'] == true) {
        fid = await PushNotifications.prepare();
      }
      if (!mounted || session != ApiService.sessionRevision) return;
      final response = await ApiService.request(
        '/Notifications/settings',
        method: 'PUT',
        body: {
          for (final key in [
            'enabled',
            'dailyMoodReminder',
            'weeklySummary',
            'hour',
            'minute',
            'offsetMinutes',
          ])
            key: next[key],
        },
      );
      serverSaved = true;
      if (fid != null) {
        await PushNotifications.register(fid);
      } else {
        await PushNotifications.disable();
      }
      if (mounted && response is Map) {
        setState(() => _settings = Map<String, dynamic>.from(response));
      }
    } on ApiException catch (error) {
      if (serverSaved &&
          session == ApiService.sessionRevision &&
          !wasEnabled &&
          next['enabled'] == true) {
        try {
          await ApiService.request(
            '/Notifications/settings',
            method: 'PUT',
            body: {...next, 'enabled': false},
          );
        } on ApiException {
          // A future explicit retry can reconcile server settings.
        }
        await PushNotifications.endSession();
      }
      if (mounted) setState(() => _error = error.message);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _test() async {
    if (_busy) return;
    setState(() {
      _busy = true;
      _error = null;
    });
    final session = ApiService.sessionRevision;
    try {
      final fid = await PushNotifications.prepare();
      if (!mounted || session != ApiService.sessionRevision) return;
      await PushNotifications.register(fid);
      await ApiService.request('/Notifications/test', method: 'POST');
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('Đã gửi yêu cầu thông báo thử.')),
        );
      }
    } on ApiException catch (error) {
      if (mounted) setState(() => _error = error.message);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(title: const Text('Thông báo')),
    body: ListView(
      padding: const EdgeInsets.fromLTRB(20, 4, 20, 28),
      children: [
        const SectionHeading(
          'Một lời nhắc nhẹ nhàng',
          subtitle: 'Bạn quyết định có nhận nhắc ghi nhận cảm xúc và nhìn lại tuần hay không.',
        ),
        if (_loading) const LoadingCards(count: 1),
        if (_error != null) StatusCard(message: _error!, onRetry: _load),
        if (_settings != null) ...[
          if (!_available)
            const StatusCard(
              message: 'Thông báo chưa sẵn sàng trên thiết bị này. Các chức năng khác vẫn sử dụng bình thường.',
              icon: Icons.notifications_off_outlined,
            ),
          Card(
            child: Column(
              children: [
                SwitchListTile(
                  title: const Text('Nhận thông báo'),
                  subtitle: const Text('Mặc định tắt; chỉ gửi khi bạn đồng ý.'),
                  value: _enabled,
                  onChanged: _busy || (!_available && !_enabled)
                      ? null
                      : (value) => _save({'enabled': value}),
                ),
                const Divider(),
                SwitchListTile(
                  title: const Text('Nhắc ghi nhận tâm trạng'),
                  subtitle: const Text('Một lần mỗi ngày vào giờ bạn chọn.'),
                  value: _settings!['dailyMoodReminder'] == true,
                  onChanged: _busy || !_enabled || !_available
                      ? null
                      : (value) => _save({'dailyMoodReminder': value}),
                ),
                SwitchListTile(
                  title: const Text('Nhìn lại tuần'),
                  subtitle: const Text('Nhắc mở thống kê vào Chủ nhật.'),
                  value: _settings!['weeklySummary'] == true,
                  onChanged: _busy || !_enabled || !_available
                      ? null
                      : (value) => _save({'weeklySummary': value}),
                ),
                ListTile(
                  leading: const Icon(Icons.schedule),
                  title: const Text('Giờ nhận lời nhắc'),
                  subtitle: Text(
                    '${(_settings!['hour'] as num? ?? 20).toInt().toString().padLeft(2, '0')}:${(_settings!['minute'] as num? ?? 0).toInt().toString().padLeft(2, '0')} · giờ địa phương',
                  ),
                  enabled: !_busy && _enabled && _available,
                  onTap: !_enabled || !_available || _busy
                      ? null
                      : () async {
                          final time = await showTimePicker(
                            context: context,
                            initialTime: TimeOfDay(
                              hour: (_settings!['hour'] as num? ?? 20).toInt(),
                              minute: (_settings!['minute'] as num? ?? 0)
                                  .toInt(),
                            ),
                          );
                          if (time != null && mounted) {
                            await _save({
                              'hour': time.hour,
                              'minute': time.minute,
                            });
                          }
                        },
                ),
              ],
            ),
          ),
          const SizedBox(height: 12),
          OutlinedButton.icon(
            onPressed: _busy || !_enabled || !_available ? null : _test,
            icon: const Icon(Icons.notifications_active_outlined),
            label: const Text('Gửi thông báo thử'),
          ),
          if (_busy) const LinearProgressIndicator(),
        ],
      ],
    ),
  );
}
