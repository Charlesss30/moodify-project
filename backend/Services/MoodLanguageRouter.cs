using System.Text;
using System.Text.RegularExpressions;

namespace mood_recommendation.Services;

/// <summary>Versioned, deterministic routing. This is language selection, not emotion inference.</summary>
public static partial class MoodLanguageRouter
{
    public const string Version = "vi-en-heuristic-v2";
    private const string Accents = "àáảãạăằắẳẵặâầấẩẫậèéẻẽẹêềếểễệìíỉĩịòóỏõọôồốổỗộơờớởỡợùúủũụưừứửữựỳýỷỹỵđ";
    private static readonly HashSet<string> Vietnamese = new(
        "toi minh buon vui khong hom nay dang met chan cam thay muon hanh phuc gian lang tam trang binh yen nang luong hao hung phim nhac qua rat that duoc nhung khi".Split(' '));
    private static readonly HashSet<string> English = new(
        "i i'm im feel feeling felt am my the is are was today happy sad calm excited anxious angry tired lonely scared peaceful love not very about and but because with want overwhelmed heartbroken thrilled terrified".Split(' '));

    public static string? Detect(string text)
    {
        string normalized;
        try { normalized = text.Normalize(NormalizationForm.FormC).ToLowerInvariant(); }
        catch (ArgumentException) { return null; }
        // Reject unsupported scripts instead of sending them silently to English.
        if (normalized.Any(c => char.IsLetter(c) && c is not (>= 'a' and <= 'z') && !Accents.Contains(c))) return null;
        var words = Words().Matches(SemanticMoodMapper.Normalize(normalized)).Select(x => x.Value).ToArray();
        if (words.Length == 0) return "vi"; // Emoji: Vietnamese preprocessor; may abstain if normalization is empty.
        var vi = words.Count(Vietnamese.Contains);
        var en = words.Count(English.Contains);
        var accented=normalized.Any(c=>Accents.Contains(c));
        var accentedVietnameseWord=Regex.Matches(normalized,"[a-z"+Accents+"]+",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100))
            .Any(word=>word.Value.Any(c=>Accents.Contains(c))&&Vietnamese.Contains(SemanticMoodMapper.Normalize(word.Value)));
        if(accentedVietnameseWord)return "vi";
        // An English self report can contain café, résumé or a Vietnamese place name.
        if(en>0&&vi==0)return "en";
        if(accented&&en==0)return "vi";
        return vi == 0 && en == 0 ? "en" : en > vi ? "en" : "vi";
    }

    [GeneratedRegex("[a-z]+(?:'[a-z]+)?", RegexOptions.CultureInvariant)]
    private static partial Regex Words();
}
