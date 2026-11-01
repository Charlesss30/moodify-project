using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using mood_recommendation.Data;
using mood_recommendation.Models;
using mood_recommendation.Services;

/// <summary>
/// Diagnostic semantic examples against live, unchanged VI/EN weights through the mobile HTTP route.
/// Transport assertions and semantic agreement are separate. Examples are not independent gold data.
/// </summary>
public static class BilingualMoodDiagnostics
{
    private sealed record Sample(string Id, string Language, string Text, string? Mood, string? Emotion = null, string Category = "baseline", bool ExpectAbstain = false);
    private static readonly Sample[] Samples =
    [
        new("vi-01","vi","hôm nay tao vui","Happy","Enjoyment"),
        new("en-01","en","I am happy today.","Happy"),
        new("vi-02","vi","Hôm nay tôi cảm thấy vui vẻ.","Happy","Enjoyment"),
        new("en-02","en","Today I feel happy.","Happy"),
        new("vi-03","vi","Hôm nay tôi rất vui và hài lòng với cuộc sống.","Happy","Enjoyment"),
        new("en-03","en","Today I am happy and content with my life.","Happy"),
        new("vi-04","vi","Tôi cực kỳ hào hứng, tràn đầy năng lượng và mong chờ buổi hòa nhạc tối nay!","Excited","Enjoyment"),
        new("en-04","en","I am extremely excited, full of energy and cannot wait for tonight's concert!","Excited"),
        new("vi-05","vi","Hôm nay tôi rất buồn, chỉ muốn khóc.","Sad","Sadness"),
        new("en-05","en","Today I feel very sad and just want to cry.","Sad"),
        new("vi-06","vi","Tôi thấy bình yên và thư giãn khi ngồi uống trà.","Relaxed","Enjoyment"),
        new("en-06","en","I feel peaceful and relaxed while sitting with a cup of tea.","Relaxed"),
        new("vi-07","vi","Tôi rất tức giận vì bị đối xử bất công.",null,"Anger"),
        new("en-07","en","I am very angry because I was treated unfairly.",null),
        new("vi-08","vi","Tôi lo lắng và sợ hãi về chuyện ngày mai.",null,"Fear"),
        new("en-08","en","I am anxious and scared about tomorrow.",null),
        new("vi-09","vi","Hôm nay tôi không vui, tôi thấy rất buồn.","Sad","Sadness"),
        new("en-09","en","I am not happy today; I feel very sad.","Sad"),
        new("vi-10","vi","Hôm qua tôi buồn nhưng hôm nay tôi vui.","Happy","Enjoyment"),
        new("en-10","en","I was sad yesterday but today I am happy.","Happy"),
        new("vi-11","vi","Tôi đang buồn nhưng muốn tìm nhạc vui để cải thiện tâm trạng.","Sad","Sadness"),
        new("en-11","en","I feel sad but want happy music to improve my mood.","Sad"),
        new("vi-12","vi","Tôi vừa vui vừa lo lắng về công việc mới.",null),
        new("en-12","en","I feel both happy and anxious about my new job.",null),
        new("vi-13","vi","hom nay tao vui","Happy","Enjoyment"),
        new("en-13","en","I feel happy.","Happy"),
        new("vi-14","vi","Hôm nay tôi vui quá 😊","Happy","Enjoyment"),
        new("en-14","en","I am so happy today 😊","Happy"),
        new("vi-15","vi","Tôi vui lắm!","Happy","Enjoyment"),
        new("en-15","en","I am happy!","Happy"),
        new("vi-16","vi","Tôi vui vì đã hoàn thành công việc hôm nay.","Happy","Enjoyment"),
        new("en-16","en","I am happy because I finished my work today.","Happy"),
        new("vi-17","vi","Tôi đang yêu và thấy thật lãng mạn khi nghĩ về người ấy.","Romantic","Enjoyment"),
        new("en-17","en","I am in love and feel romantic when I think about my partner.","Romantic"),
        new("vi-18","vi","Tôi đang tập trung cao độ để học và không muốn bị làm phiền.","Focused"),
        new("en-18","en","I am focused on studying and do not want to be disturbed.","Focused"),
        new("vi-19","vi","Tôi nhớ tuổi thơ, những ký ức cũ khiến tôi bồi hồi hoài niệm.","Nostalgic"),
        new("en-19","en","I miss my childhood and feel nostalgic about those old memories.","Nostalgic"),
        new("vi-20","vi","Tôi rất phấn khích và háo hức chờ chuyến đi sắp tới!","Excited","Enjoyment"),
        new("en-20","en","I am thrilled and excited about my upcoming trip!","Excited")
    ];

    public static async Task<int> Run(string root, string connection, bool fullAudit = false)
    {
        Console.OutputEncoding = Encoding.UTF8;
        var config = new ConfigurationBuilder().SetBasePath(Path.Combine(root,"backend"))
            .AddJsonFile("appsettings.json").AddJsonFile("appsettings.Development.json",true).Build();
        var samples = fullAudit ? Samples.Concat(new[]{"bilingual-flow-cases.json","mobile-ai-edge-cases.json"}
            .SelectMany(file=>JsonSerializer.Deserialize<Sample[]>(
                File.ReadAllText(Path.Combine(root,"tests/ai-cases",file)),
                new JsonSerializerOptions(JsonSerializerDefaults.Web))!)).ToArray() : Samples;
        var sourceName = config["Mongo:Database"] ?? "mood_recommendation";
        var database = "moodify_test_mood_diagnostic_" + Guid.NewGuid().ToString("N");
        var mongo = new MongoClient(connection);
        var db = new MongoStore(mongo,new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string,string?> { ["Mongo:Database"] = database }).Build());
        var inferenceUrl = Environment.GetEnvironmentVariable("MOODIFY_DIAGNOSTIC_INFERENCE") ?? "http://127.0.0.1:8001/";
        var inferenceUri = new Uri(inferenceUrl);
        if (!inferenceUri.IsLoopback) throw new ArgumentException("Diagnostics require a local inference service.");
        var output = Path.Combine(root,".local-build","bilingual-mood-diagnostic-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ"));
        Directory.CreateDirectory(output);
        var checks = 0;
        void Check(bool value,string message) { if(!value)throw new Exception(message); checks++; }
        using var inference = new HttpClient { BaseAddress=inferenceUri,Timeout=TimeSpan.FromSeconds(20) };
        async Task<JsonElement> Get(string route) {
            using var response=await inference.GetAsync(route);response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<JsonElement>();
        }
        var identity = new Dictionary<string,JsonElement>();
        foreach(var kind in new[]{"vi","va"}) {
            var health=await Get("health/"+kind+"/ready");
            Check(health.GetProperty("status").GetString()=="ready",kind+" model not ready");
            var version=health.GetProperty("model_version").GetString()!;
            identity[kind]=await Get("models/"+kind+"/fingerprint/"+Uri.EscapeDataString(version));
            Check(identity[kind].GetProperty("active").GetBoolean(),kind+" fingerprint not active");
        }
        Process? backend=null;
        try {
            var source=mongo.GetDatabase(sourceName);
            foreach(var name in new[]{"moods","aiBilingualPolicyState","contents","movies","music","genres"}) {
                var docs=await source.GetCollection<BsonDocument>(name).Find(FilterDefinition<BsonDocument>.Empty).ToListAsync();
                if(docs.Count>0)await db.Database.GetCollection<BsonDocument>(name).InsertManyAsync(docs);
            }
            await db.Initialize(Path.Combine(root,"backend/Data/mongo-schema.json"));
            var password=Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
            var account=new TaiKhoan {TenDangNhap="diagnostic",Email="diagnostic@test.invalid",MatKhau=BCrypt.Net.BCrypt.HashPassword(password)};
            await db.Insert(account);
            var listener=new TcpListener(IPAddress.Loopback,0);
            listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();
            var start=new ProcessStartInfo("dotnet") {
                WorkingDirectory=Path.Combine(root,"backend"),UseShellExecute=false,CreateNoWindow=true,
                RedirectStandardOutput=true,RedirectStandardError=true
            };
            start.ArgumentList.Add(Environment.GetEnvironmentVariable("MOODIFY_BACKEND_DLL")
                ?? throw new Exception("Set MOODIFY_BACKEND_DLL to the isolated backend build."));
            foreach(var pair in new Dictionary<string,string> {
                ["ASPNETCORE_ENVIRONMENT"]="Development",["ASPNETCORE_URLS"]=$"http://127.0.0.1:{port}",
                ["Mongo__ConnectionString"]=connection,["Mongo__Database"]=database,["AI__Enabled"]="true",["AI__ServiceUrl"]=inferenceUrl,
                ["Jwt__Key"]=Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)),
                ["Email__Host"]="",["Email__Username"]="",["Email__Password"]="",
                ["Firebase__ProjectId"]="",["Firebase__ServiceAccountPath"]="",
                ["Spotify__ClientId"]="",["Spotify__ClientSecret"]="",["Tmdb__AccessToken"]=""
            }) start.Environment[pair.Key]=pair.Value;
            backend=Process.Start(start) ?? throw new Exception("Backend failed to launch");
            backend.OutputDataReceived+=(_,_)=>{};backend.ErrorDataReceived+=(_,_)=>{};
            backend.BeginOutputReadLine();backend.BeginErrorReadLine();
            using var api=new HttpClient {BaseAddress=new Uri($"http://127.0.0.1:{port}/api/"),Timeout=TimeSpan.FromSeconds(20)};
            var ready=false;
            for(var i=0;i<100;i++) {
                if(backend.HasExited)throw new Exception("Isolated backend exited during startup.");
                try {using var r=await api.GetAsync("Genres");if(r.IsSuccessStatusCode){ready=true;break;}}
                catch(HttpRequestException){} await Task.Delay(100);
            }
            Check(ready,"Isolated backend not ready");
            using(var login=await api.PostAsJsonAsync("Auth/login",new{identifier=account.Email,matKhau=password})) {
                login.EnsureSuccessStatusCode();
                var auth=await login.Content.ReadFromJsonAsync<JsonElement>();
                api.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",auth.GetProperty("token").GetString());
            }
            var moods=await db.TamTrangs.ToListAsync();
            var results=new List<object>();
            var guardedFailures=new List<string>();
            var latencies=new List<(string Language,double Ms)>();
            var agreement=new Dictionary<string,(int Same,int Total)> { ["vi"]=(0,0),["en"]=(0,0) };
            var unknown=new Dictionary<string,int>{["vi"]=0,["en"]=0};
            foreach(var sample in samples) {
                var modelInput=SemanticMoodMapper.PrepareModelInput(sample.Text,sample.Language);
                JsonElement? fullTextPrediction=null;
                if(modelInput.Scope!="full_text") {
                    using var fullResponse=await inference.PostAsJsonAsync(sample.Language=="vi"?"predict-vi":"predict-va",new{text=sample.Text,language=sample.Language});
                    fullResponse.EnsureSuccessStatusCode();
                    fullTextPrediction=await fullResponse.Content.ReadFromJsonAsync<JsonElement>();
                }
                using var rawResponse=await inference.PostAsJsonAsync(sample.Language=="vi"?"predict-vi":"predict-va",new{text=modelInput.Text,language=sample.Language});
                rawResponse.EnsureSuccessStatusCode();
                var raw=await rawResponse.Content.ReadFromJsonAsync<JsonElement>();
                var timer=Stopwatch.StartNew();
                using var response=await api.PostAsJsonAsync("MoodAnalysis/valence-arousal",new{text=sample.Text,language="auto"});
                Check(response.IsSuccessStatusCode,"Mobile analysis route failed for "+sample.Id);
                var actual=await response.Content.ReadFromJsonAsync<JsonElement>();
                timer.Stop();latencies.Add((sample.Language,timer.Elapsed.TotalMilliseconds));
                Check(actual.GetProperty("language").GetString()==sample.Language,"Wrong language for "+sample.Id);
                Check(actual.GetProperty("inputScope").GetString()==modelInput.Scope
                    &&actual.GetProperty("analyzedCharacterCount").GetInt32()==modelInput.Text.Length,"Input scope mismatch: "+sample.Id);
                Check(actual.GetProperty("modelVersion").GetString()==raw.GetProperty("model_version").GetString(),"Wrong model for "+sample.Id);
                foreach(var coordinate in new[]{"valence","arousal"})
                    Check(Math.Abs(raw.GetProperty(coordinate).GetDouble()-actual.GetProperty(coordinate).GetDouble())<1e-6,"Coordinates changed in gateway for "+sample.Id);
                var status=actual.GetProperty("status").GetString();
                var mood=actual.GetProperty("mood");
                var name=mood.ValueKind==JsonValueKind.Null ? null : mood.GetProperty("tenTamTrang").GetString();
                if(sample.ExpectAbstain&&status!="unknown")guardedFailures.Add(sample.Id);
                var moodAgreement=sample.Mood is null ? (bool?)null : name==sample.Mood;
                if(sample.Mood is not null) {var old=agreement[sample.Language];agreement[sample.Language]=(old.Same+(moodAgreement==true?1:0),old.Total+1);}
                if(status=="unknown")unknown[sample.Language]++;
                object? recommendation = null;
                if(status=="matched") {
                    var chosen=moods.Single(m=>m.TamTrangID==mood.GetProperty("tamTrangID").GetInt32());
                    var targetV=actual.GetProperty("recommendationValence").GetDouble();
                    var targetA=actual.GetProperty("recommendationArousal").GetDouble();
                    Check(targetV>=(double)chosen.MinValence&&targetV<=(double)chosen.MaxValence
                        &&targetA>=(double)chosen.MinArousal&&targetA<=(double)chosen.MaxArousal,"Target outside chosen mood: "+sample.Id);
                    using var recResponse=await api.PostAsJsonAsync("Recommendation",new{
                        source="ai_va",analysisId=actual.GetProperty("analysisId").GetString(),
                        tamTrangID=chosen.TamTrangID,mode="hybrid",limit=4,balanced=true});
                    Check(recResponse.IsSuccessStatusCode,"Recommendation failed: "+sample.Id);
                    var rec=await recResponse.Content.ReadFromJsonAsync<JsonElement>();
                    Check(Math.Abs(rec.GetProperty("valence").GetDouble()-targetV)<1e-6
                        &&Math.Abs(rec.GetProperty("arousal").GetDouble()-targetA)<1e-6,"Receipt target not used: "+sample.Id);
                    var saved=await db.LichSuTamTrangs.FirstAsync(h=>h.AnalysisId==actual.GetProperty("analysisId").GetString());
                    Check(saved.AnalysisValence==(decimal)raw.GetProperty("valence").GetDouble()
                        &&saved.AnalysisArousal==(decimal)raw.GetProperty("arousal").GetDouble(),"Raw provenance lost: "+sample.Id);
                    recommendation=new{valence=targetV,arousal=targetA,items=rec.GetProperty("items").GetArrayLength()};
                }
                else Check(actual.GetProperty("analysisId").ValueKind==JsonValueKind.Null,"Uncertain state issued receipt: "+sample.Id);
                var distances=moods.Select(m=>new{moodId=m.TamTrangID,name=m.TenTamTrang,
                    distanceSquared=Math.Pow((double)((m.MinValence+m.MaxValence)/2)-raw.GetProperty("valence").GetDouble(),2)
                        +Math.Pow((double)((m.MinArousal+m.MaxArousal)/2)-raw.GetProperty("arousal").GetDouble(),2)}).OrderBy(x=>x.distanceSquared).ToArray();
                results.Add(new {sample.Id,sample.Text,sample.Category,sample.ExpectAbstain,latencyMs=timer.Elapsed.TotalMilliseconds,expectedLanguage=sample.Language,expectedMood=sample.Mood,expectedEmotion=sample.Emotion,
                    moodAgreement,emotionAgreement=sample.Emotion is null ? (bool?)null : raw.GetProperty("emotion").GetString()==sample.Emotion,
                    modelInput,fullTextPrediction,raw,result=actual,distances,recommendation});
                Console.WriteLine($"{sample.Id}: {name??status} | expected {sample.Mood??"review"} | V={raw.GetProperty("valence").GetDouble():F3}, A={raw.GetProperty("arousal").GetDouble():F3}");
            }
            // Same input repeated with unchanged weights: detect stochastic runtime, not semantic quality.
            var repeats=new List<object>();
            foreach(var sample in samples.Take(2)) {
                double? firstV=null,firstA=null;
                for(var i=0;i<3;i++) {
                    using var res=await inference.PostAsJsonAsync(sample.Language=="vi"?"predict-vi":"predict-va",new{text=sample.Text,language=sample.Language});
                    res.EnsureSuccessStatusCode();var raw=await res.Content.ReadFromJsonAsync<JsonElement>();
                    var v=raw.GetProperty("valence").GetDouble();var a=raw.GetProperty("arousal").GetDouble();
                    Check(firstV is null || Math.Abs(firstV.Value-v)<1e-6&&Math.Abs(firstA!.Value-a)<1e-6,"Repeated predictions changed: "+sample.Id);
                    firstV??=v;firstA??=a;repeats.Add(new{sample.Id,run=i,prediction=raw});
                }
            }
            object[] burstResults=[];
            if(fullAudit) {
                burstResults=await Task.WhenAll(Enumerable.Range(0,4).Select(async run=>{
                    var timer=Stopwatch.StartNew();
                    using var res=await api.PostAsJsonAsync("MoodAnalysis/valence-arousal",new{text="I feel happy today.",language="auto"});
                    var body=await res.Content.ReadFromJsonAsync<JsonElement>();
                    return (object)new{run,statusCode=(int)res.StatusCode,accepted=body.GetProperty("accepted").GetBoolean(),
                        analysisId=body.GetProperty("analysisId").ValueKind,latencyMs=timer.Elapsed.TotalMilliseconds};
                }));
                foreach(var item in burstResults) {
                    var data=JsonSerializer.SerializeToElement(item);
                    var code=data.GetProperty("statusCode").GetInt32();
                    Check(code is 200 or 503,"Concurrent model request returned unexpected server error.");
                    if(code==503)Check(!data.GetProperty("accepted").GetBoolean(),"Busy model issued accepted result.");
                }
            }
            foreach(var (kind,before) in identity) {
                var after=await Get("models/"+kind+"/fingerprint/"+Uri.EscapeDataString(before.GetProperty("version").GetString()!));
                Check(after.GetProperty("active").GetBoolean()
                    &&before.GetProperty("bundleSha256").GetString()==after.GetProperty("bundleSha256").GetString()
                    &&before.GetProperty("activeBundleSha256").GetString()==after.GetProperty("activeBundleSha256").GetString(),
                    "Serving identity changed during diagnosis: "+kind);
            }
            var policies=new mood_recommendation.Services.BilingualPolicyService(db);
            var mismatches=agreement.Values.Sum(x=>x.Total-x.Same);
            double Percentile(IEnumerable<double> values,double p){var sorted=values.Order().ToArray();return sorted[Math.Min(sorted.Length-1,(int)Math.Ceiling(p*sorted.Length)-1)];}
            var report=new {caseCount=samples.Length,guardedFailures,burstResults,latency=latencies.GroupBy(x=>x.Language).ToDictionary(g=>g.Key,g=>new{
                count=g.Count(),medianMs=Percentile(g.Select(x=>x.Ms),.5),p95Ms=Percentile(g.Select(x=>x.Ms),.95)}),checkedAt=DateTimeOffset.UtcNow,status=mismatches>0||guardedFailures.Count>0?"semantic_mismatches_found":"examples_agree",
                scope=$"{samples.Length} authored diagnostic examples; expectations are not independent gold labels. Transport assertions do not measure model accuracy.",
                transportAssertions=checks,semanticAgreement=agreement.ToDictionary(x=>x.Key,x=>new{same=x.Value.Same,total=x.Value.Total}),
                unknown,identity,viPolicy=await policies.ActiveAsync("vi",default),enPolicy=await policies.ActiveAsync("en",default),
                moods=moods.Select(m=>new{id=m.TamTrangID,name=m.TenTamTrang,minV=m.MinValence,maxV=m.MaxValence,minA=m.MinArousal,maxA=m.MaxArousal}),
                results,repeats};
            await File.WriteAllTextAsync(Path.Combine(output,"results.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions(JsonSerializerDefaults.Web){WriteIndented=true}));
            Console.WriteLine($"Transport: {checks} assertions passed. Semantic examples: VI {agreement["vi"].Same}/{agreement["vi"].Total}; EN {agreement["en"].Same}/{agreement["en"].Total}. Mismatches: {mismatches}. Report: {output}");
            return mismatches>0||guardedFailures.Count>0?2:0;
        }
        finally {
            if(backend is {HasExited:false}) {backend.Kill(entireProcessTree:true);await backend.WaitForExitAsync();}
            backend?.Dispose();
            if(!database.StartsWith("moodify_test_mood_diagnostic_",StringComparison.Ordinal)||database==sourceName)
                throw new Exception("Invalid temporary cleanup target.");
            await mongo.DropDatabaseAsync(database);
            Console.WriteLine("Temporary backend/database removed. Main stack and model activation unchanged.");
        }
    }
}