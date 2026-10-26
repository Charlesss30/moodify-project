/// Lightweight automatic routing for Vietnamese/English mood descriptions.
///
/// Uses Vietnamese accents and common words; ambiguous short or mixed-language
/// text may still be misidentified. No additional model is loaded on the phone.
String detectMoodLanguage(String text) {
  final normalized = text.toLowerCase();
  if (RegExp(
    r'[àáảãạăằắẳẵặâầấẩẫậèéẻẽẹêềếểễệìíỉĩịòóỏõọôồốổỗộơờớởỡợùúủũụưừứửữựỳýỷỹỵđ\u0300-\u036f]',
  ).hasMatch(normalized)) {
    return 'vi';
  }
  final words = RegExp(r"[a-z]+(?:'[a-z]+)?")
      .allMatches(normalized)
      .map((match) => match.group(0)!)
      .toList();
  const vietnamese = {
    'toi',
    'minh',
    'buon',
    'vui',
    'khong',
    'hom',
    'nay',
    'dang',
    'met',
    'chan',
    'cam',
    'thay',
    'muon',
    'hanh',
    'phuc',
    'gian',
    'lang',
    'tam',
    'trang',
    'binh',
    'yen',
    'nang',
    'luong',
    'hao',
    'hung',
    'phim',
    'nhac',
    'qua',
    'rat',
    'that',
    'duoc',
    'nhung',
    'khi',
  };
  const english = {
    'i',
    "i'm",
    'im',
    'feel',
    'feeling',
    'felt',
    'am',
    'my',
    'the',
    'is',
    'are',
    'was',
    'today',
    'happy',
    'sad',
    'calm',
    'excited',
    'anxious',
    'angry',
    'tired',
    'lonely',
    'scared',
    'peaceful',
    'love',
    'not',
    'very',
    'about',
    'and',
    'but',
    'because',
    'with',
    'want',
    'overwhelmed',
    'heartbroken',
    'thrilled',
    'terrified',
  };
  final viScore = words.where(vietnamese.contains).length;
  final enScore = words.where(english.contains).length;
  // Latin-only text without Vietnamese evidence is usually English. Emoji-only
  // input defaults to Vietnamese, whose preprocessor supports common emoji.
  if (viScore == 0 && enScore == 0) return words.isEmpty ? 'vi' : 'en';
  return enScore > viScore ? 'en' : 'vi';
}
