String moodName(String raw) =>
    const {
      'happy': 'Vui vẻ',
      'sad': 'Buồn',
      'relaxed': 'Thư giãn',
      'excited': 'Hào hứng',
      'focused': 'Tập trung',
      'romantic': 'Lãng mạn',
      'nostalgic': 'Hoài niệm',
      'angry': 'Giận dữ',
      'calm': 'Bình yên',
    }[raw.trim().toLowerCase()] ??
    raw;
String moodEmoji(String raw) =>
    const {
      'happy': '😊',
      'sad': '😢',
      'relaxed': '😌',
      'excited': '🤩',
      'focused': '🎯',
      'romantic': '🥰',
      'nostalgic': '🌅',
      'angry': '😠',
      'calm': '😌',
    }[raw.trim().toLowerCase()] ??
    '🙂';
String shortDate(String? value, {bool year = false}) {
  final date = DateTime.tryParse(value ?? '')?.toLocal();
  if (date == null) return 'Chưa rõ ngày';
  final day = date.day.toString().padLeft(2, '0');
  final month = date.month.toString().padLeft(2, '0');
  return year ? '$day/$month/${date.year}' : '$day/$month';
}

String timeOfDay(DateTime? value) => value == null
    ? ''
    : '${value.hour.toString().padLeft(2, '0')}:${value.minute.toString().padLeft(2, '0')}';
String moodDescription(double valence, double arousal) {
  final feeling = valence > .2
      ? 'Thiên về tích cực'
      : valence < -.2
      ? 'Thiên về trầm lắng'
      : 'Cảm xúc trung tính';
  final energy = arousal > .2
      ? 'năng lượng cao'
      : arousal < -.2
      ? 'năng lượng thấp'
      : 'năng lượng vừa';
  return '$feeling · $energy';
}
