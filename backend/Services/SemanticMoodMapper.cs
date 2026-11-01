using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using mood_recommendation.Models;

namespace mood_recommendation.Services;

public sealed record MoodContext(string Status, string? MoodKey, string Reason, IReadOnlyList<string>? ExcludedMoodKeys = null);
public sealed record MoodDecision(TamTrang? Mood, string Method, string? ReasonCode);
public sealed record MoodModelInput(string Text, string Scope);

/// <summary>
/// Conservative, versioned text rules constrain the app taxonomy; they are not model predictions.
/// Original model scores are preserved. Mixed, negated, historical and desired-content descriptions
/// must not silently become an unrelated nearest-centre mood.
/// </summary>
public static class SemanticMoodMapper
{
    public const string Version = "context-mapping-v5";
    public static readonly string[] Methods = ["explicit_self_description","model_emotion","model_va_general","model_negation"];
    private static readonly TimeSpan Limit = TimeSpan.FromMilliseconds(100);
    private static Regex R(string pattern) => new(pattern,RegexOptions.CultureInvariant|RegexOptions.IgnoreCase,Limit);
    private static readonly Regex Quotes=R("\"[^\"]*\"|“[^”]*”");
    private static readonly Regex Clauses=R(@"[,;.!?\r\n]+|\b(?:but|however|nhung|tuy nhien|ma)\b");
    private static readonly Regex Current=R(@"\b(?:today|now|currently|right now|hom nay|bay gio|hien tai|luc nay)\b");
    private static readonly Regex Past=R(@"\b(?:yesterday|used to|last (?:week|month|year)|was|were|felt|earlier|this morning|hom qua|truoc day|da tung|ngay truoc|luc sang|sang nay|vua nay)\b");
    private static readonly Regex Goal=R(@"\b(?:i (?:just |really )?(?:want|need|wish|would like)|(?:toi|minh|tao|tui|to|em|anh) (?:chi |rat |dang )?(?:muon|can)|looking for|want|need|wish|i hope|toi hy vong|muon|tim (?:nhac|phim|bai hat)|muon (?:nghe|xem|tim|cam thay)|(?:ban (?:co the )?|hay )(?:goi y|de xuat)|(?:goi y|de xuat) (?:giup|cho) (?:toi|minh|tao|tui)|(?:can|could) you (?:please )?(?:recommend|suggest|find)|please (?:recommend|suggest|find)|(?:recommend|suggest) (?:me|a|some))\b");
    // A request can precede the user's own state: "I want music because I feel sad".
    // Resume only at an explicit self subject; keep tense/uncertainty markers for the normal guards.
    private static readonly Regex RequestResume=R(@"\b(?:because|since|as|boi vi|vi|do)\s+(?=(?:(?:hom nay|bay gio|hien tai|hom qua|ngay mai|today|now|yesterday|tomorrow)\s+)?(?:i\b|i'm\b|toi\b|minh\b|tao\b|tui\b|to\b))");
    private static readonly Regex NegationMarker=R(@"\b(?:khong|chang|cha|chua|het|not|never|neither|nor|no longer)\b");
    private static readonly Regex RequestConnector=R(@"\s+\b(?:nen|so|therefore|and|va)\s*$");
    private static readonly Regex NegationVi=R(@"\b(?:khong|chang|cha|chua|het|dau con)(?:\s+(?:he|the|cam|thay|duoc|con|that|su|rat|qua|co|chut|gi|nhan|lai|may|dang|phai|la)){0,8}\s*$");
    private static readonly Regex NegationEn=R(@"\b(?:not|never|no longer|neither|nor|hardly|don't|dont|didn't|didnt|cannot|can't|isn't|isnt|wasn't|wasnt|aren't|arent)(?:\s+(?:really|very|so|that|at|all|quite|particularly|especially|even|feeling|feel|any|more|much|exactly|too|longer|be)){0,8}\s*$");
    private static readonly Regex DoubleNegation=R(@"\b(?:khong|not)(?:\s+(?:phai|la|toi|that|su|he|really|that|i|am)){0,6}\s+(?:khong|not)\b");
    private static readonly Regex ComparativeVi=R(@"\bchua (?:bao gio|tung)\b.{0,120}\b(?:nhu|hon|den the|the nay)\b");
    private static readonly Regex ComparativeEn=R(@"\bnever\b.{0,50}\b(?:so|this|happier|happiest|sadder|saddest)\b");
    private static readonly Regex ViShortNegation=R(@"\b(?:k|ko|khum|hong|kg)\b");
    private static readonly Regex SelfEn=R(@"\b(?:i (?:am|'m|feel|am feeling|have been feeling|have (?:never )?(?:felt|been)|miss|love)|i'm|i've (?:never )?(?:felt|been)|im|my (?:mood|heart)|it makes me feel|made me feel)\b");
    private static readonly Regex SelfVi=R(@"\b(?:toi|minh|tao|tui|to|ban than|cam thay|tam trang)\b");
    private static readonly Regex OtherSubject=R(@"\b(?:he|she|they|you|anh ay|co ay|ban ay|nguoi ay|my (?:friend|mother|father|sister|brother|partner|boss|colleague|coworker|roommate)|(?:ban|me|bo|chi|anh trai|nguoi yeu|dong nghiep) (?:cua )?(?:toi|minh)|ban)\b");
    private static readonly Regex Reported=R(@"\b(?:i (?:heard|read|said)|toi (?:nghe|doc|noi) (?:rang|la))\b");
    private static readonly Regex Content=R(@"\b(?:song|music|movie|film|story|nhac|phim|bai hat|cau chuyen)\b");
    private static readonly Regex FutureState=R(@"\b(?:i will|i'll|i shall|(?:toi|minh|tao|tui) se)\b");
    private static readonly Regex Uncertain=R(@"\b(?:if i|whether|not sure|maybe|perhaps|gia su|neu toi|co le|khong chac|khong biet (?:toi|minh|co)|i (?:am )?(?:(?:just|only) )?(?:imagine|pretend|imagining|pretending)|(?:toi|minh|tao|tui) (?:(?:chi|dang) )*(?:tuong tuong|gia vo))\b");
    private static readonly Regex ContentAction=R(@"\b(?:watch(?:ing|ed)?|read(?:ing)?|listen(?:ing)?|xem|doc|nghe)\b");
    private static readonly Regex OwnFeeling=R(@"\b(?:i(?:'m| am| feel)|(?:makes?|made) me(?: feel)?|(?:toi|minh|tao|tui|to)|cam thay|lam toi|feel)(?:\s+(?:very|really|so|quite|extremely|particularly|rat|that|su|dang|hoi|cam|thay)){0,6}\s*$");
    private static readonly Regex Sarcasm=R(@"🙄|/s(?:\s|$)|yeah[,]?\s*right|as if|mia mai");
    private static readonly Regex Reaction=R(@"\b(?:(?:makes?|made) me(?: feel)?|lam (?:toi|minh|tao|tui)(?: (?:cam|thay))*)\s*$");
    private static readonly Regex NonMoodPhrase=R(@"\b(?:buon (?:ngu|non)|vui long|chuc mung (?:sinh nhat|nam moi)|happy (?:birthday|new year)|merry christmas)\b");
    private static readonly Regex FutureTime=R(@"\b(?:tomorrow|next (?:week|month|year)|ngay mai|tuan sau|thang sau|sau nay)\b");
    private static readonly Regex TrailingTime=R(@"^\s+(?:(?:vao|on)\s+)?(?:yesterday|tomorrow|hom qua|ngay mai)\b");
    private static readonly Regex FocusBoundary=R(@"(?<=\s)\b(?:and|va|con|while|vi|because|when|khi)\b|(?=\b(?:(?:makes?|made) me|lam (?:toi|minh|tao|tui))\b)");
    private static readonly Regex ContentAfterCue=R(@"^\s+(?:(?:little|new|old)\s+)?(?:song|music|movie|film|story)\b");
    private static readonly Regex NonRomantic=R(@"\bin love with (?:music|movies|films|this song|this movie|my work|coding)\b");

    private static readonly (string Key,Regex Vi,Regex En)[] Cues=[
        ("Happy",R(@"\b(?:vui(?! long)(?: ve)?|hanh phuc|hai long)\b"),R(@"\b(?:happy|happier|happiest|happiness|joyful|cheerful|glad|contented|content with (?:my|life)|delighted|hopeful)\b")),
        ("Sad",R(@"\b(?:buon(?! (?:ngu|non|cuoi))(?: ba)?|dau long|co don|tuyet vong|dau kho|khoc)\b"),R(@"\b(?:sad|unhappy|miserable|heartbroken|lonely|downhearted|cry|crying)\b")),
        ("Relaxed",R(@"\b(?:binh yen|thu gian|thanh than|thanh thoi|yen binh|nhe nhom)\b"),R(@"\b(?:relaxed|relaxing|peaceful|calm|at ease|serene)\b")),
        ("Excited",R(@"\b(?:hao hung|hao huc|phan khich|ron rang|nong long|tran day nang luong)\b"),R(@"\b(?:excited|excitement|thrilled|pumped|ecstatic|full of energy|can't wait|cannot wait)\b")),
        ("Focused",R(@"\b(?:tap trung|chu tam|chuyen tam)\b"),R(@"\b(?:focused|concentrating|concentrated|in the zone)\b")),
        ("Romantic",R(@"\b(?:(?:toi|minh|tao|tui) dang yeu(?! cau)|lang man|rung dong|nho nguoi yeu|yeu (?:nguoi ay|anh ay|co ay|ban gai|ban trai))\b"),R(@"\b(?:in love|romantic|love (?:you|him|her|my partner|my boyfriend|my girlfriend))\b")),
        ("Nostalgic",R(@"\b(?:hoai niem|boi hoi|nho (?:tuoi tho|ngay xua|qua khu|nhung ngay|ky uc))\b"),R(@"\b(?:nostalgic|nostalgia|miss (?:my childhood|the old days)|old memories)\b")),
        ("unsupported",R(@"\b(?:tuc gian|gian du|phan no|buc boi|cau gian|lo lang|lo au|so hai|hoang so|cang thang|hoi hop)\b"),R(@"\b(?:angry|furious|irritated|annoyed|anxious|scared|afraid|stressed|terrified|worried|nervous)\b"))
    ];

    public static string Normalize(string text) {
        var b=new StringBuilder();
        foreach(var c in text.ToLowerInvariant().Normalize(NormalizationForm.FormD))
            if(CharUnicodeInfo.GetUnicodeCategory(c)!=UnicodeCategory.NonSpacingMark)
                b.Append(c=='đ'?'d':c=='’'?'\'':c);
        return b.ToString().Normalize(NormalizationForm.FormC);
    }

    public static MoodContext ReadContext(string text,string language) {
        var normalized=Normalize(Quotes.Replace(text," "));
        if(language=="vi")normalized=ViShortNegation.Replace(normalized,"khong");
        var found=new HashSet<string>(StringComparer.Ordinal);
        var excluded=new HashSet<string>(StringComparer.Ordinal);
        var ambiguousNegation=false;
        var nonCurrent=Quotes.IsMatch(text);
        var uncertain=false;
        var sarcasm=Sarcasm.IsMatch(normalized);
        foreach(var part in StateParts(normalized)) {
            if(part.RequestRelated)nonCurrent=true;
            var clause=part.Text.Trim();
            if(clause.Length==0)continue;
            if(Uncertain.IsMatch(clause)){uncertain=true;continue;}
            var others=OtherSubject.Matches(clause).Cast<Match>()
                .Where(other=>!(language=="vi"&&other.Value=="he"&&NegationVi.IsMatch(clause[..other.Index]))).ToArray();
            var selves=SelfVi.Matches(clause).Cast<Match>().Concat(SelfEn.Matches(clause).Cast<Match>())
                .Where(own=>!(language=="en"&&own.Value=="to"))
                .Where(own=>!others.Any(other=>own.Index>=other.Index&&own.Index+own.Length<=other.Index+other.Length))
                .Where(own=>own.Value is not ("cam thay" or "tam trang" or "ban than")
                    ||!others.Any(other=>other.Index<own.Index)).ToArray();
            if(Reported.IsMatch(clause)){nonCurrent=true;continue;}
            var nonMood=NonMoodPhrase.Matches(clause).Cast<Match>().ToArray();
            if(nonMood.Length>0)nonCurrent=true;
            if((Past.IsMatch(clause)||FutureState.IsMatch(clause)||FutureTime.IsMatch(clause))
                &&!Current.IsMatch(clause))nonCurrent=true;
            foreach(var (key,vi,en) in Cues) {
                foreach(var cue in vi.Matches(clause).Cast<Match>().Concat(en.Matches(clause).Cast<Match>())) {
                    var prefix=clause[..cue.Index];
                    if(key=="Romantic"&&NonRomantic.IsMatch(clause))continue;
                    if(nonMood.Any(span=>cue.Index>=span.Index&&cue.Index<span.Index+span.Length))continue;
                    var own=selves.Where(m=>m.Index+m.Length<=cue.Index).OrderByDescending(m=>m.Index).FirstOrDefault();
                    var other=others.Where(m=>m.Index+m.Length<=cue.Index).OrderByDescending(m=>m.Index).FirstOrDefault();
                    var reaction=Reaction.IsMatch(prefix);
                    if(other is not null&&(own is null||other.Index>own.Index)&&!reaction){nonCurrent=true;continue;}
                    if(IsNonCurrent(clause,cue)){nonCurrent=true;continue;}
                    var contentBefore=Content.Matches(prefix).Cast<Match>().LastOrDefault();
                    var contentAfter=ContentAfterCue.IsMatch(clause[(cue.Index+cue.Length)..]);
                    var directlyFelt=(own is not null&&OwnFeeling.IsMatch(prefix))||reaction;
                    if(!directlyFelt&&(contentAfter||contentBefore is not null&&(own is null||contentBefore.Index>own.Index)
                        ||Content.IsMatch(clause)&&ContentAction.IsMatch(clause))){nonCurrent=true;continue;}
                    if(own is null&&!reaction&&Content.IsMatch(clause)){nonCurrent=true;continue;}
                    if(own is null&&!reaction&&clause.Split(' ',StringSplitOptions.RemoveEmptyEntries).Length>8)continue;
                    // "can't wait" is an excitement expression, not a negation of it.
                    var comparative=ComparativeVi.IsMatch(clause)||ComparativeEn.IsMatch(clause);
                    if(!comparative&&(NegationVi.IsMatch(prefix)||NegationEn.IsMatch(prefix))) {
                        if(DoubleNegation.IsMatch(prefix))ambiguousNegation=true;
                        excluded.Add(key);continue;
                    }
                    found.Add(key);
                }
            }
        }
        if(sarcasm)return new("clarify",null,"ambiguous_statement");
        if(ambiguousNegation)return new("clarify",null,"ambiguous_negation");
        if(uncertain&&found.Count==0)return new("clarify",null,"uncertain_statement");
        if(found.Overlaps(excluded))return new("clarify",null,"mixed_emotions");
        if(found.Contains("unsupported"))return new("clarify",null,"unsupported_emotion");
        // A more specific positive state can contain "happy" without becoming a mixed emotion.
        if(found.Count==2&&found.Contains("Happy")&&found.Any(x=>x is "Excited" or "Relaxed" or "Romantic"))
            found.Remove("Happy");
        if(found.Count>1)return new("clarify",null,"mixed_emotions");
        if(found.Count==1)return new("explicit",found.Single(),"explicit_self_description");
        return excluded.Count>0 ? new("negated",null,"negated_emotion",excluded.ToArray())
            : nonCurrent ? new("clarify",null,"not_current_self_description") : new("none",null,"no_explicit_state");
    }

    private static bool IsNonCurrent(string clause,Match cue) {
        if(ComparativeVi.IsMatch(clause)||ComparativeEn.IsMatch(clause))return false;
        var prefix=clause[..cue.Index];
        var current=Current.Matches(prefix).Cast<Match>().Select(m=>m.Index).DefaultIfEmpty(-1).Max();
        var past=Past.Matches(prefix).Cast<Match>().Select(m=>m.Index).DefaultIfEmpty(-1).Max();
        var future=FutureState.Matches(prefix).Cast<Match>().Concat(FutureTime.Matches(prefix).Cast<Match>())
            .Select(m=>m.Index).DefaultIfEmpty(-1).Max();
        return Math.Max(past,future)>current||TrailingTime.IsMatch(clause[(cue.Index+cue.Length)..]);
    }

    private sealed record StatePart(string Text,int Start,bool RequestRelated);

    // Keep original offsets. Requested content is not the current mood; a causal self report is.
    private static IEnumerable<StatePart> StateParts(string text) {
        var cursor=0;
        foreach(var separator in Clauses.Matches(text).Cast<Match>()) {
            foreach(var part in RequestParts(text[cursor..separator.Index],cursor))yield return part;
            cursor=separator.Index+separator.Length;
        }
        foreach(var part in RequestParts(text[cursor..],cursor))yield return part;
    }

    private static IEnumerable<StatePart> RequestParts(string text,int start) {
        var goal=Goal.Match(text);
        if(!goal.Success){yield return new(text,start,false);yield break;}
        yield return new(text[..goal.Index],start,true);
        var afterGoal=goal.Index+goal.Length;
        var resume=RequestResume.Match(text,afterGoal);
        if(!resume.Success)yield break;
        var next=resume.Index+resume.Length;
        foreach(var part in RequestParts(text[next..],start+next))yield return part;
    }

    /// <summary>
    /// Focus a single inference on original, unambiguous current-self clauses when requests,
    /// historical/other-person context or an exclusion would dilute the input. The complete description still controls all mapping/negation guards.
    /// No labels are turned into invented sentences and no confidence threshold is relaxed.
    /// </summary>
    public static MoodModelInput PrepareModelInput(string text,string language) {
        var full=new MoodModelInput(text.Trim(),"full_text");
        var plain=Normalize(text);
        if(!Goal.IsMatch(plain)&&!Past.IsMatch(plain)&&!OtherSubject.IsMatch(plain)
            &&!NegationMarker.IsMatch(plain)&&!Quotes.IsMatch(text)&&!Content.IsMatch(plain)
            &&!FutureState.IsMatch(plain)&&!FutureTime.IsMatch(plain))return full;
        var context=ReadContext(text,language);
        if(context.Status!="explicit" && !(context.Status=="negated" && context.ExcludedMoodKeys?.Count==1))return full;
        var (normalized,starts,ends)=NormalizeWithOffsets(text);
        // Spaces preserve offsets while quoted content remains outside the self-report selection.
        var masked=Quotes.Replace(normalized,m=>new string(' ',m.Length));
        var selected=new List<string>();
        foreach(var part in StateParts(masked).SelectMany(FocusParts)) {
            var localStart=0;
            var localEnd=part.Text.Length;
            while(localStart<localEnd&&char.IsWhiteSpace(part.Text[localStart]))localStart++;
            while(localEnd>localStart&&char.IsWhiteSpace(part.Text[localEnd-1]))localEnd--;
            var connector=RequestConnector.Match(part.Text[..localEnd]);
            if(connector.Success)localEnd=connector.Index;
            if(localEnd<=localStart)continue;
            var from=part.Start+localStart;
            var to=part.Start+localEnd-1;
            var original=text[starts[from]..ends[to]].Trim();
            var state=ReadContext(original,language);
            if(SameFocusContext(context,state)&&!Quotes.IsMatch(original))
                selected.Add(original);
        }
        if(selected.Count==0)return full;
        var focused=string.Join(". ",selected);
        var verified=ReadContext(focused,language);
        if(MoodLanguageRouter.Detect(focused)!=language)return full;
        return focused.Length<full.Text.Length&&SameFocusContext(context,verified)
            ? new(focused,"focused_self_description") : full;
    }

    // Model-only partitioning: full-description ownership/temporal/negation guards run first.
    // This also isolates "I feel happy" from "my friend is sad" inside one sentence.
    private static IEnumerable<StatePart> FocusParts(StatePart source) {
        var cursor=0;
        foreach(var boundary in FocusBoundary.Matches(source.Text).Cast<Match>()) {
            if(boundary.Index>cursor)
                yield return new(source.Text[cursor..boundary.Index],source.Start+cursor,source.RequestRelated);
            cursor=boundary.Index+boundary.Length;
        }
        if(cursor<source.Text.Length)
            yield return new(source.Text[cursor..],source.Start+cursor,source.RequestRelated);
    }

    private static bool SameFocusContext(MoodContext full,MoodContext candidate) =>
        full.Status==candidate.Status && full.MoodKey==candidate.MoodKey
        && (full.Status!="negated" || new HashSet<string>(full.ExcludedMoodKeys!,StringComparer.Ordinal)
            .SetEquals(candidate.ExcludedMoodKeys!));

    // Text elements preserve original accents, decomposed Unicode, emoji and apostrophes in model input.
    private static (string Text,int[] Starts,int[] Ends) NormalizeWithOffsets(string text) {
        var normalized=new StringBuilder();
        var starts=new List<int>();
        var ends=new List<int>();
        var elements=StringInfo.GetTextElementEnumerator(text);
        while(elements.MoveNext()) {
            var element=elements.GetTextElement();
            var value=Normalize(element);
            normalized.Append(value);
            foreach(var _ in value){starts.Add(elements.ElementIndex);ends.Add(elements.ElementIndex+element.Length);}
        }
        return (normalized.ToString(),starts.ToArray(),ends.ToArray());
    }

    public static string? Canonical(string name) => Normalize(name).Trim() switch {
        "happy" or "vui" or "vui ve" or "hanh phuc"=>"Happy",
        "sad" or "buon"=>"Sad", "relaxed" or "thu gian" or "binh yen"=>"Relaxed",
        "excited" or "hao hung" or "phan khich"=>"Excited", "focused" or "tap trung"=>"Focused",
        "romantic" or "lang man"=>"Romantic", "nostalgic" or "hoai niem"=>"Nostalgic", _=>null
    };

    public static MoodDecision Decide(string text,string language,string? emotion,double v,double a,IReadOnlyCollection<TamTrang> eligible) {
        var context=ReadContext(text,language);
        if(context.Status=="clarify")return new(null,"needs_clarification",context.Reason);
        string? key=context.MoodKey;
        var method="explicit_self_description";
        if(context.Status=="explicit"
            &&(key is "Happy" or "Excited" or "Relaxed" or "Romantic"&&v<-.2||key=="Sad"&&v>.2))
            return new(null,"needs_clarification","model_context_conflict");
        if(context.Status is "none" or "negated") {
            method=context.Status=="negated"?"model_negation":language=="vi"?"model_emotion":"model_va_general";
            if(language=="vi")key=emotion switch {"Enjoyment"=>"Happy","Sadness"=>"Sad",_=>null};
            else {
                // VA alone cannot imply romance, memories, or concentration.
                var general=eligible.Where(m=>Canonical(m.TenTamTrang) is "Happy" or "Sad" or "Relaxed" or "Excited")
                    .Where(m=>v>.2 ? Canonical(m.TenTamTrang)!="Sad" : v<-.2&&Canonical(m.TenTamTrang)=="Sad"&&a<=.2)
                    .Where(m=>v>=(double)m.MinValence&&v<=(double)m.MaxValence&&a>=(double)m.MinArousal&&a<=(double)m.MaxArousal)
                    .OrderBy(m=>Math.Pow((double)((m.MinValence+m.MaxValence)/2)-v,2)+Math.Pow((double)((m.MinArousal+m.MaxArousal)/2)-a,2))
                    .ThenBy(m=>m.TamTrangID).FirstOrDefault();
                key=general is null?null:Canonical(general.TenTamTrang);
            }
        }
        if(context.Status=="negated") {
            // Evidence may support another mood; negation itself never supplies its opposite.
            var excluded=context.ExcludedMoodKeys!;
            var coherent=key is "Happy" or "Excited" or "Relaxed" ? v>.2 : key=="Sad"&&v<-.2;
            if(!coherent||key is null||excluded.Contains(key)
                ||excluded.Contains("Happy")&&key!="Sad"||excluded.Contains("unsupported"))
                return new(null,"needs_clarification","negation_model_conflict");
        }
        var matches=eligible.Where(m=>Canonical(m.TenTamTrang)==key&&key is not null).ToArray();
        return matches.Length==1 ? new(matches[0],method,null)
            : new(null,"needs_clarification",key is null?"unresolved_mood":"mood_not_available");
    }

    // The recommendation target respects the chosen mood without rewriting the model's raw VA.
    public static (decimal Valence,decimal Arousal) Project(decimal v,decimal a,TamTrang mood) =>
        (Math.Clamp(v,mood.MinValence,mood.MaxValence),Math.Clamp(a,mood.MinArousal,mood.MaxArousal));
}