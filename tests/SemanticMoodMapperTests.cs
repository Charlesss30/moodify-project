using mood_recommendation.Models;
using mood_recommendation.Services;

public static class SemanticMoodMapperTests
{
    public static int Run()
    {
        var checks=0;
        void Check(bool ok,string message){if(!ok)throw new Exception(message);checks++;}
        var cases=new (string Lang,string Text,string? Key,string? Reason)[]{
            ("vi","Hôm nay tao vui","Happy",null),("en","I am happy today","Happy",null),
            ("vi","Hôm nay mình vui vẻ và hạnh phúc","Happy",null),("en","I feel cheerful and glad","Happy",null),
            ("vi","hom nay tui vui qua","Happy",null),("en","I'm content with my life","Happy",null),
            ("vi","Tôi thấy bình yên khi uống trà","Relaxed",null),("en","I feel calm and at ease","Relaxed",null),
            ("vi","Tôi đang thư giãn và rất vui","Relaxed",null),("en","I am happy and relaxed","Relaxed",null),
            ("vi","Tao phấn khích chờ buổi diễn","Excited",null),("en","I am thrilled about the show","Excited",null),
            ("vi","Tôi đang rất háo hức","Excited",null),("en","I can't wait for the concert","Excited",null),
            ("vi","Mình vui và hào hứng lắm","Excited",null),("en","I am happy and excited","Excited",null),
            ("vi","Tôi đang tập trung học","Focused",null),("en","I am focused on this task","Focused",null),
            ("vi","Tôi thấy lãng mạn khi nghĩ về người yêu","Romantic",null),("en","I'm in love with my partner","Romantic",null),
            ("vi","Tôi nhớ tuổi thơ","Nostalgic",null),("en","I miss my childhood","Nostalgic",null),
            ("vi","Tôi đang hoài niệm ngày xưa","Nostalgic",null),("en","I feel nostalgic about home","Nostalgic",null),
            ("vi","Tôi buồn và cô đơn","Sad",null),("en","I feel lonely and sad","Sad",null),
            ("vi","Tôi không vui",null,"negated_emotion"),("en","I am not happy",null,"negated_emotion"),
            ("vi","Tôi không hề vui",null,"negated_emotion"),("en","I'm not very happy",null,"negated_emotion"),
            ("vi","Tôi không còn buồn",null,"negated_emotion"),("en","I am no longer sad",null,"negated_emotion"),
            ("vi","Tôi không buồn, tôi đang vui","Happy",null),("en","I am not sad, I feel happy","Happy",null),
            ("vi","Hôm qua tôi buồn nhưng hôm nay tôi vui","Happy",null),("en","I was sad yesterday but today I am happy","Happy",null),
            ("vi","Hôm qua tôi vui",null,"not_current_self_description"),("en","I was happy yesterday",null,"not_current_self_description"),
            ("vi","Tôi muốn nghe nhạc vui",null,"not_current_self_description"),("en","I want happy music",null,"not_current_self_description"),
            ("vi","Tôi muốn cảm thấy hạnh phúc",null,"not_current_self_description"),("en","I want to feel happy",null,"not_current_self_description"),
            ("vi","Tôi buồn nhưng muốn nghe nhạc vui","Sad",null),("en","I feel sad but want happy music","Sad",null),
            ("vi","Tôi vừa buồn vừa vui",null,"mixed_emotions"),("en","I feel happy and sad",null,"mixed_emotions"),
            ("vi","Tôi vui nhưng lo lắng",null,"unsupported_emotion"),("en","I am happy but anxious",null,"unsupported_emotion"),
            ("vi","Tôi tức giận",null,"unsupported_emotion"),("en","I am furious",null,"unsupported_emotion"),
            ("vi","Tôi sợ hãi",null,"unsupported_emotion"),("en","I feel scared",null,"unsupported_emotion"),
            ("vi","Bạn tôi vui",null,"not_current_self_description"),("en","My friend is happy",null,"not_current_self_description"),
            ("en","I feel sad but want happy music to improve my mood.","Sad",null),
            ("en","I feel sad and need a cheerful movie for my mood","Sad",null),
            ("en","She is happy",null,"not_current_self_description"),
            ("vi","Anh ấy vui",null,"not_current_self_description"),
            ("en","I read: \"happy today\"",null,"not_current_self_description"),
            ("en","I am watching a happy movie",null,"not_current_self_description"),
            ("en","I am listening to a sad song",null,"not_current_self_description"),
            ("vi","Tôi vui quá 🙄",null,"ambiguous_statement"),
            ("en","Yeah right, I am happy 🙄",null,"ambiguous_statement"),
            ("vi","Tôi chưa từng cảm thấy vui như hôm nay","Happy",null),
            ("en","I have never felt happier than today","Happy",null),
            ("vi","Tôi không chỉ vui mà còn rất hào hứng","Excited",null),
            ("en","I am not only happy but also excited","Excited",null),
            ("vi","Hôm nay t k vui",null,"negated_emotion"),
            ("vi","Tôi không thấy buồn và đang vui","Happy",null),
            ("en","I am not sad and I feel happy","Happy",null),
            ("vi","Ngày mai tôi sẽ vui",null,"not_current_self_description"),
            ("en","Tomorrow I will be happy",null,"not_current_self_description"),
            ("vi","Nếu tôi vui thì tôi nghe nhạc",null,"uncertain_statement"),
            ("en","If I am happy I will listen to music",null,"uncertain_statement"),
            ("vi","Bộ phim vui",null,"not_current_self_description"),("en","A happy song",null,"not_current_self_description")
        };
        var moods=new List<TamTrang>{
            new(){TamTrangID=101,TenTamTrang="Happy",MinValence=.5m,MaxValence=1m,MinArousal=.2m,MaxArousal=1m},
            new(){TamTrangID=102,TenTamTrang="Sad",MinValence=-1m,MaxValence=-.2m,MinArousal=-1m,MaxArousal=.2m},
            new(){TamTrangID=103,TenTamTrang="Relaxed",MinValence=.1m,MaxValence=.8m,MinArousal=-1m,MaxArousal=.1m},
            new(){TamTrangID=104,TenTamTrang="Excited",MinValence=.4m,MaxValence=1m,MinArousal=.5m,MaxArousal=1m},
            new(){TamTrangID=105,TenTamTrang="Focused",MinValence=0m,MaxValence=.6m,MinArousal=.2m,MaxArousal=.8m},
            new(){TamTrangID=106,TenTamTrang="Romantic",MinValence=.3m,MaxValence=1m,MinArousal=-.2m,MaxArousal=.5m},
            new(){TamTrangID=107,TenTamTrang="Nostalgic",MinValence=-.3m,MaxValence=.5m,MinArousal=-.5m,MaxArousal=.3m}
        };
        foreach(var item in cases){
            var result=SemanticMoodMapper.ReadContext(item.Text,item.Lang);
            Check(result.MoodKey==item.Key,$"Context: {item.Text}: expected {item.Key??item.Reason}, got {result}");
            if(item.Reason is not null)Check(result.Reason==item.Reason,"Wrong refusal reason: "+item.Text);
            var decision=SemanticMoodMapper.Decide(item.Text,item.Lang,item.Key=="Sad"?"Sadness":"Enjoyment",
                item.Key=="Sad"?-.6:.52,item.Key=="Sad"?-.4:.26,moods);
            if(result.Status=="negated")
                Check(decision.Mood is null||!result.ExcludedMoodKeys!.Contains(SemanticMoodMapper.Canonical(decision.Mood.TenTamTrang)!),"Returned a negated mood: "+item.Text);
            else Check(SemanticMoodMapper.Canonical(decision.Mood?.TenTamTrang??"")==item.Key,"Decision lost context: "+item.Text);
        }
        var happy=SemanticMoodMapper.Decide("I am happy today","en",null,.519,.256,moods);
        Check(happy.Mood?.TamTrangID==101,"Happy became Romantic from VA distance.");
        var calm=SemanticMoodMapper.Decide("Tôi thư giãn","vi","Enjoyment",.63,.61,moods);
        Check(calm.Mood?.TamTrangID==103,"Calm became Happy from the Enjoyment anchor.");
        var target=SemanticMoodMapper.Project(.63m,.61m,calm.Mood!);
        Check(target==(.63m,.1m),"Calm recommendation retained high-arousal coordinates.");
        Check(SemanticMoodMapper.Decide("Tôi vui","vi","Enjoyment",.7,.7,moods.Where(m=>m.TenTamTrang!="Happy").ToArray()).Mood is null,
            "Excluded mood silently changed into another policy mood.");
        Check(SemanticMoodMapper.Decide("A new experience","en",null,.52,.26,moods).Mood?.TenTamTrang=="Happy",
            "VA alone manufactured romance.");
        Check(SemanticMoodMapper.Decide("A new experience","en",null,.1,0,moods).Mood is null,"Neutral point manufactured nostalgia.");
        Check(SemanticMoodMapper.Decide("Something happened","vi","Fear",-.7,.9,moods).Mood is null,"Fear became sadness.");
        foreach(var (lang,text,emotion,v,a,expected) in new[] {
            ("vi","Hôm nay tôi không được vui","Sadness",-.71,-.44,"Sad"),
            ("vi","Tôi chẳng thấy vui chút nào","Sadness",-.70,-.44,"Sad"),
            ("en","I do not feel happy today",(string?)null,-.48,.17,"Sad"),
            ("vi","Tôi không vui","Enjoyment",.70,.60,(string?)null),
            ("en","I am not happy today",(string?)null,.70,.60,(string?)null),
            ("vi","Tôi không còn buồn nữa","Sadness",-.63,-.40,(string?)null),
            ("vi","Tôi không buồn","Enjoyment",.70,.60,"Happy"),
            ("en","I am not sad",(string?)null,.01,.01,(string?)null),
            ("vi","Tôi không vui cũng không buồn","Sadness",-.70,-.44,(string?)null),
            ("vi","Không phải là tôi không vui","Sadness",-.70,-.44,(string?)null)
        }) {
            var decision=SemanticMoodMapper.Decide(text,lang,emotion,v,a,moods);
            Check(SemanticMoodMapper.Canonical(decision.Mood?.TenTamTrang??"")==expected,"Negation evidence failed: "+text);
        }
        Check(SemanticMoodMapper.Decide("I am happy","en",null,-.6,.1,moods).ReasonCode=="model_context_conflict",
            "Strong model/text polarity conflict silently overwrote the model.");
        Check(SemanticMoodMapper.Decide("Tôi buồn","vi","Enjoyment",.7,.6,moods).Mood is null,
            "Strong model/text polarity conflict silently rewrote negative mood.");
        var localized=moods.Select(m=>new TamTrang{TamTrangID=m.TamTrangID,TenTamTrang=m.TenTamTrang=="Happy"?"Vui vẻ":m.TenTamTrang}).ToArray();
        Check(SemanticMoodMapper.Decide("I'm happy","en",null,.5,.2,localized).Mood?.TamTrangID==101,"Localized mood identity broke.");
        Check(SemanticMoodMapper.Canonical("HÀO HỨNG")=="Excited","Unicode normalization failed.");
        var focusCases=new (string Language,string Text,string Expected,string? Key)[] {
            ("vi","Vì tôi đang buồn nên tôi muốn có một bài gì đó giúp làm nổi lên cái buồn của tui. Bạn hiểu mà đúng k?","Vì tôi đang buồn","Sad"),
            ("en","I am sad so I want a cheerful song to help me feel better. You know what I mean?","I am sad","Sad"),
            ("vi","Tôi đang buồn nên tôi muốn nghe một bài hát vui để nhẹ lòng hơn.","Tôi đang buồn","Sad"),
            ("en","I feel sad and need a cheerful movie to improve my mood.","I feel sad","Sad"),
            ("vi","Tôi đang buồn. Tôi muốn tìm một phim vui.","Tôi đang buồn","Sad"),
            ("en","I feel sad. I want a happy movie.","I feel sad","Sad"),
            ("vi","Tôi muốn nghe nhạc vui vì tôi đang buồn.","tôi đang buồn","Sad"),
            ("en","I want a happy song because I feel sad today.","I feel sad today","Sad"),
            ("vi","Tôi muốn nghe nhạc vui vì hôm nay tôi đang buồn.","hôm nay tôi đang buồn","Sad"),
            ("en","I want happy music because today I feel sad.","today I feel sad","Sad"),
            ("vi","Tôi vui nên tôi muốn nghe nhạc sôi động.","Tôi vui","Happy"),
            ("en","I am happy so I need an upbeat song.","I am happy","Happy"),
            ("vi","Hôm qua tôi buồn. Hôm nay tôi vui nên tôi muốn nghe nhạc.","Hôm nay tôi vui","Happy"),
            ("en","I was sad yesterday. Today I am happy and want some music.","Today I am happy","Happy"),
            ("vi","Tôi không được vui nên tôi muốn tìm một bài hát.","Tôi không được vui",null),
            ("en","I am not happy so I want a song.","I am not happy",null),
            ("vi","Tôi đang thư giãn nên tôi muốn nghe nhạc nhẹ.","Tôi đang thư giãn","Relaxed"),
            ("en","I’m relaxed and want some quiet music.","I’m relaxed","Relaxed"),
            ("vi","Tôi muốn nghe nhạc vì tôi cần một bài hát vì tôi đang buồn.","tôi đang buồn","Sad")
        };
        foreach(var item in focusCases) {
            var input=SemanticMoodMapper.PrepareModelInput(item.Text,item.Language);
            Check(input.Scope=="focused_self_description","Request did not focus current state: "+item.Text);
            Check(input.Text==item.Expected,"Wrong original input span: "+item.Text+" => "+input.Text);
            Check(SemanticMoodMapper.ReadContext(item.Text,item.Language).MoodKey==item.Key,"Full description lost state: "+item.Text);
        }
        foreach(var (language,text) in new[] {
            ("vi","Tôi buồn nhưng cũng vui nên tôi muốn nghe nhạc."),
            ("en","I feel sad and happy so I want music."),
            ("vi","Tôi tức giận nên tôi muốn nghe nhạc vui."),
            ("en","I feel angry so I want happy music."),
            ("vi","Tôi muốn nghe nhạc vui."),
            ("en","I want happy music."),
            ("vi","Hôm qua tôi buồn nên tôi muốn nghe nhạc."),
            ("en","I was sad yesterday so I want a song."),
            ("vi","Nếu tôi buồn thì tôi muốn nghe nhạc."),
            ("en","If I am sad I want music."),
            ("vi","Bạn tôi buồn nên tôi muốn tìm nhạc vui."),
            ("en","My friend is sad so I want happy music."),
            ("vi","Tôi vui 🙄 nên tôi muốn nghe nhạc."),
            ("en","Yeah right, I am happy 🙄 so I want music."),
            ("vi","Tôi muốn tìm phim vì hôm qua tôi buồn."),
            ("en","I want a film because yesterday I was sad."),
            ("vi","Tôi buồn nên tôi muốn nghe nhạc vì tôi đang vui."),
            ("en","I am sad so I want music because I am happy now."),
            ("vi","Tôi muốn được hạnh phúc."),
            ("en","I want to feel happy.")
        }) {
            var input=SemanticMoodMapper.PrepareModelInput(text,language);
            Check(input.Scope=="full_text"&&input.Text==text,"Focus hid a qualifier/conflicting state: "+text);
            Check(SemanticMoodMapper.ReadContext(text,language).Status=="clarify","A non-current/mixed description became explicit: "+text);
        }
        foreach(var (language,text,expected) in new[] {
            ("vi","Tôi đang buồn, bạn có thể gợi ý một bài nhạc vui không?","Tôi đang buồn"),
            ("en","I am sad, can you recommend a happy song?","I am sad"),
            ("vi","Tôi đang buồn hãy gợi ý nhạc vui cho tôi.","Tôi đang buồn"),
            ("en","I am sad please suggest a happy film.","I am sad"),
            ("vi","Tôi buồn, gợi ý cho tôi nhạc vui.","Tôi buồn"),
            ("en","I feel sad recommend me some happy music.","I feel sad")
        }) {
            var input=SemanticMoodMapper.PrepareModelInput(text,language);
            Check(input.Scope=="focused_self_description"&&input.Text==expected,"Direct request obscured state: "+text);
        }
        foreach(var (language,text) in new[] {
            ("en","I want music because I imagine I am happy."),
            ("vi","Tôi muốn nhạc vì tôi tưởng tượng tôi đang vui."),
            ("en","I want happy music because I pretend to feel sad."),
            ("vi","Tôi muốn nhạc vì tôi giả vờ buồn.")
        }) {
            Check(SemanticMoodMapper.ReadContext(text,language).Status=="clarify"
                &&SemanticMoodMapper.PrepareModelInput(text,language).Scope=="full_text",
                "An imagined state became a current self report: "+text);
        }
        foreach(var (language,text,expected) in new[] {
            ("vi","Hôm qua tôi buồn nhưng hôm nay tôi vui.","hôm nay tôi vui"),
            ("en","I was sad yesterday but today I am happy.","today I am happy"),
            ("vi","Tôi không buồn, tôi đang vui","tôi đang vui"),
            ("en","I am not sad, I am happy today.","I am happy today"),
            ("vi","Bạn tôi vui. Tôi đang buồn.","Tôi đang buồn"),
            ("en","My friend is happy. I feel sad.","I feel sad"),
            ("vi","Bạn nói: “vui quá”. Tôi đang buồn.","Tôi đang buồn"),
            ("en","She said: “happy today”. I feel sad.","I feel sad")
        }) {
            var input=SemanticMoodMapper.PrepareModelInput(text,language);
            Check(input.Scope=="focused_self_description"&&input.Text==expected,
                "Non-current/other-subject text diluted self description: "+text);
        }
        var codeSwitch="Tôi muốn nhạc vì I feel sad.";
        Check(SemanticMoodMapper.PrepareModelInput(codeSwitch,"vi").Scope=="full_text",
            "Focus sent an English-only fragment to the Vietnamese model.");
        var decomposed=focusCases[0].Text.Normalize(System.Text.NormalizationForm.FormD);
        var decomposedInput=SemanticMoodMapper.PrepareModelInput(decomposed,"vi");
        Check(decomposedInput.Text==focusCases[0].Expected.Normalize(System.Text.NormalizationForm.FormD),
            "Input focus corrupted decomposed Vietnamese.");
        var repeated="Tôi đang buồn. Tôi buồn nên tôi muốn nhạc.";
        Check(SemanticMoodMapper.PrepareModelInput(repeated,"vi").Text=="Tôi đang buồn. Tôi buồn",
            "Multiple agreeing self reports were discarded.");
        var longRequest="Tôi đang buồn nên tôi muốn nghe nhạc "+string.Join(" ",Enumerable.Repeat("gợi ý một bài hát",150))+".";
        Check(SemanticMoodMapper.PrepareModelInput(longRequest,"vi").Text=="Tôi đang buồn","Long request diluted explicit state.");
        return checks;
    }
}