/// Lightweight automatic routing for Vietnamese/English mood descriptions.
///
/// Uses Vietnamese accents and common words; ambiguous short or mixed-language
/// text may still be misidentified. No additional model is loaded on the phone.
String detectMoodLanguage(String text) {
  final normalized = text.toLowerCase();
  final hasAccents = RegExp(
    r'[àáảãạăằắẳẵặâầấẩẫậèéẻẽẹêềếểễệìíỉĩịòóỏõọôồốổỗộơờớởỡợùúủũụưừứửữựỳýỷỹỵđ\u0300-\u036f]',
  ).hasMatch(normalized);
  final words = RegExp(r"[a-z]+(?:'[a-z]+)?")
      .allMatches(_foldMoodText(normalized))
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
  final accentedVietnameseWord = RegExp(r"[^\s.,!?;:]+")
      .allMatches(normalized)
      .any(
        (word) =>
            _foldMoodText(word.group(0)!) != word.group(0) &&
            vietnamese.contains(_foldMoodText(word.group(0)!)),
      );
  final viScore = words.where(vietnamese.contains).length;
  final enScore = words.where(english.contains).length;
  if (accentedVietnameseWord) return 'vi';
  if (enScore > 0 && viScore == 0) return 'en';
  if (hasAccents && enScore == 0) return 'vi';
  // Latin-only text without Vietnamese evidence is usually English. Emoji-only
  // input defaults to Vietnamese, whose preprocessor supports common emoji.
  if (viScore == 0 && enScore == 0) return words.isEmpty ? 'vi' : 'en';
  return enScore > viScore ? 'en' : 'vi';
}

String _foldMoodText(String text) {
  const groups = {
    'a': 'àáảãạăằắẳẵặâầấẩẫậ',
    'e': 'èéẻẽẹêềếểễệ',
    'i': 'ìíỉĩị',
    'o': 'òóỏõọôồốổỗộơờớởỡợ',
    'u': 'ùúủũụưừứửữự',
    'y': 'ỳýỷỹỵ',
    'd': 'đ',
  };
  var folded = text.replaceAll('’', "'");
  for (final group in groups.entries) {
    folded = folded.replaceAll(RegExp('[${group.value}]'), group.key);
  }
  return folded.replaceAll(RegExp(r'[\u0300-\u036f]'), '');
}
