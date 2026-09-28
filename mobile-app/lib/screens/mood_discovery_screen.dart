import 'song_detail_screen.dart';
import 'package:flutter/material.dart';
import '../core/api_service.dart';

class MoodDiscoveryScreen extends StatefulWidget {
  final bool showHistory;
  const MoodDiscoveryScreen({super.key, this.showHistory = false});
  @override
  State<MoodDiscoveryScreen> createState() => _MoodDiscoveryScreenState();
}

class _MoodDiscoveryScreenState extends State<MoodDiscoveryScreen> {
  List<dynamic> _moods = [];
  List<dynamic> _items = [];
  int? _selectedMood;
  String _contentType = 'Movie';
  bool _busy = true;
  String? _error;
  bool _requested = false;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    setState(() { _busy = true; _error = null; });
    try {
      if (widget.showHistory) {
        final data = await ApiService.history();
        if (mounted) setState(() => _items = data['items'] as List<dynamic>);
      } else {
        final data = await ApiService.moods();
        if (mounted) {
          setState(() {
            _moods = data as List<dynamic>;
            _selectedMood = _moods.isEmpty ? null : _moods.first['tamTrangID'] as int;
          });
        }
      }
    } catch (e) {
      if (mounted) setState(() => _error = e.toString());
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _recommend() async {
    if (_selectedMood == null) return;
    setState(() { _busy = true; _error = null; _items = []; });
    try {
      final data = await ApiService.recommend(_selectedMood!, _contentType);
      if (mounted) setState(() { _items = data['items'] as List<dynamic>; _requested = true; });
    } catch (e) {
      if (mounted) setState(() => _error = e.toString());
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _rate(Map<String, dynamic> content) async {
    final stars = await showDialog<int>(
      context: context,
      builder: (context) => SimpleDialog(
        title: Text('Bạn thấy "${content['tieuDe']}" phù hợp thế nào?'),
        children: List.generate(5, (i) => SimpleDialogOption(
          onPressed: () => Navigator.pop(context, i + 1),
          child: Text('${i + 1} sao'),
        )),
      ),
    );
    if (stars == null || !mounted) return;
    try {
      await ApiService.rate(content['noiDungID'] as String, stars, '');
      if (mounted) ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text('Đã lưu đánh giá.')));
    } catch (e) {
      if (mounted) ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(e.toString())));
    }
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(title: Text(widget.showHistory ? 'Lịch sử tâm trạng' : 'Gợi ý cho bạn')),
    body: ListView(
      padding: const EdgeInsets.all(20),
      children: [
        if (!widget.showHistory) ...[
          const Text('Chọn tâm trạng hiện tại và loại nội dung bạn muốn khám phá.'),
          const SizedBox(height: 16),
          DropdownButtonFormField<int>(
            key: ValueKey(_selectedMood),
            initialValue: _selectedMood,
            decoration: const InputDecoration(labelText: 'Tâm trạng'),
            items: _moods.map((m) => DropdownMenuItem<int>(
              value: m['tamTrangID'] as int, child: Text(m['tenTamTrang'] as String),
            )).toList(),
            onChanged: _busy ? null : (v) => setState(() => _selectedMood = v),
          ),
          const SizedBox(height: 12),
          SegmentedButton<String>(
            segments: const [
              ButtonSegment(value: 'Movie', label: Text('Phim'), icon: Icon(Icons.movie_outlined)),
              ButtonSegment(value: 'Music', label: Text('Nhạc'), icon: Icon(Icons.music_note)),
            ],
            selected: {_contentType},
            onSelectionChanged: _busy ? null : (values) => setState(() => _contentType = values.first),
          ),
          const SizedBox(height: 12),
          FilledButton(onPressed: _busy || _selectedMood == null ? null : _recommend, child: const Text('Lấy gợi ý')),
          if (!_busy && _moods.isEmpty && _error == null)
            const Text('Chưa có tâm trạng. Quản trị viên cần bổ sung danh sách tâm trạng.'),
        ],
        if (_busy) const Padding(padding: EdgeInsets.all(20), child: Center(child: CircularProgressIndicator())),
        if (_error != null) ...[
          Text(_error!, style: TextStyle(color: Theme.of(context).colorScheme.error)),
          TextButton(onPressed: _load, child: const Text('Tải lại')),
        ],
        if (!_busy && _items.isEmpty && (_requested || widget.showHistory) && _error == null)
          Text(widget.showHistory ? 'Chưa có lịch sử tâm trạng.' : 'Thư viện chưa có nội dung phù hợp.'),
        if (widget.showHistory)
          ..._items.map((item) => Card(child: ListTile(
            title: Text(item['tenTamTrang'] as String),
            subtitle: Text('${DateTime.parse(item['thoiGian'] as String).toLocal()}\nValence: ${item['valence']} · Arousal: ${item['arousal']}'),
            isThreeLine: true,
          )))
        else
          ..._items.map((item) {
            final content = item['content'] as Map<String, dynamic>;
            final image = content['hinhAnh'] as String?;
            return Card(child: Padding(
              padding: const EdgeInsets.all(12),
              child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                if (image != null && image.isNotEmpty)
                  Image.network(image, height: 180, width: double.infinity, fit: BoxFit.cover,
                    errorBuilder: (_, error, stack) => const SizedBox(height: 80, child: Center(child: Icon(Icons.image_not_supported_outlined)))),
                const SizedBox(height: 8),
                Text(content['tieuDe'] as String, style: Theme.of(context).textTheme.titleMedium),
                if (content['artist'] != null) Text(content['artist'] as String),
                Text('Phù hợp tâm trạng: ${item['match']}%'),
                if (['Music', 'Song', 'Nhac', 'Nhạc'].contains(content['loaiNoiDung']))
                  FilledButton.icon(
                    onPressed: () => Navigator.push(context, MaterialPageRoute(builder: (_) =>
                      SongDetailScreen(contentId: content['noiDungID'] as String))),
                    icon: const Icon(Icons.headphones),
                    label: const Text('Thông tin & nghe thử'),
                  ),
                if (content['moTa'] != null) Text(content['moTa'] as String, maxLines: 4, overflow: TextOverflow.ellipsis),
                TextButton.icon(onPressed: () => _rate(content), icon: const Icon(Icons.star_outline), label: const Text('Đánh giá')),
              ]),
            ));
          }),
      ],
    ),
  );
}
