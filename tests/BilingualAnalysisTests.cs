using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using mood_recommendation.Data;
using mood_recommendation.Models;
using mood_recommendation.Services;

public static class BilingualAnalysisTests
{
    public static async Task Run(MongoStore db,
        Func<string,string,object?,int,string?,Task<JsonNode?>> call, Action<bool,string> check,
        FakeInferenceServer inference, Func<Task> restart)
    {
        var account = new TaiKhoan { TenDangNhap="bilingual_fixture", Email="bilingual@test.invalid",
            MatKhau=BCrypt.Net.BCrypt.HashPassword("Bilingual-fixture-1") };
        await db.Insert(account);
        var token = (await call("POST","/Auth/login",new { identifier=account.Email,matKhau="Bilingual-fixture-1" },200,null))!["token"]!.GetValue<string>();
        var other = new TaiKhoan { TenDangNhap="bilingual_other", Email="bilingual-other@test.invalid",
            MatKhau=BCrypt.Net.BCrypt.HashPassword("Bilingual-fixture-2") };
        await db.Insert(other);
        var otherToken = (await call("POST","/Auth/login",new { identifier=other.Email,matKhau="Bilingual-fixture-2" },200,null))!["token"]!.GetValue<string>();
        // Earlier CRUD tests deliberately rename/delete moods. Supply the semantic catalog this suite exercises.
        foreach(var fixture in new[] {
            new TamTrang{TenTamTrang="Happy",MinValence=.5m,MaxValence=1,MinArousal=.2m,MaxArousal=1},
            new TamTrang{TenTamTrang="Sad",MinValence=-1,MaxValence=-.2m,MinArousal=-1,MaxArousal=.2m},
            new TamTrang{TenTamTrang="Relaxed",MinValence=.1m,MaxValence=.8m,MinArousal=-1,MaxArousal=.1m},
            new TamTrang{TenTamTrang="Excited",MinValence=.4m,MaxValence=1,MinArousal=.5m,MaxArousal=1},
            new TamTrang{TenTamTrang="Focused",MinValence=0,MaxValence=.6m,MinArousal=.2m,MaxArousal=.8m},
            new TamTrang{TenTamTrang="Romantic",MinValence=.3m,MaxValence=1,MinArousal=-.2m,MaxArousal=.5m},
            new TamTrang{TenTamTrang="Nostalgic",MinValence=-.3m,MaxValence=.5m,MinArousal=-.5m,MaxArousal=.3m}
        }) {
            if(!await db.TamTrangs.AnyAsync(m=>m.TenTamTrang==fixture.TenTamTrang))await db.Insert(fixture);
        }
        var receipts=db.Database.GetCollection<VaAnalysisReceipt>(BilingualMoodAnalysisService.ReceiptCollection);
        var enabled=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"AI:Enabled","true"}}).Build();
        var factory=new InferenceFactory(inference.BaseAddress);
        var service=new BilingualMoodAnalysisService(factory,db,enabled,TimeProvider.System,new BilingualPolicyService(db));
        var moods=await db.TamTrangs.ToListAsync();
        check(moods.Count>=2,"Bilingual test needs real fixture mood geometry.");
        await call("POST","/MoodAnalysis/valence-arousal",new { text="I feel calm" },401,null);
        await call("POST","/MoodAnalysis/valence-arousal",new { text=" ",language="auto" },400,token);
        await call("POST","/MoodAnalysis/valence-arousal",new { text="test",language="fr" },400,token);

        async Task<JsonNode> Analyze(string text="I feel hopeful today.",string language="auto") =>
            (await call("POST","/MoodAnalysis/valence-arousal",new { text,language },200,token))!;
        object Confirm(JsonNode analysis,string source="ai_va",int? moodId=null) => new {
            source,analysisId=analysis["analysisId"]!.GetValue<string>(),
            tamTrangID=moodId??analysis["mood"]!["tamTrangID"]!.GetValue<int>(),mode="hybrid",limit=3 };
        foreach(var (text,language) in new[] {
            ("Tôi đang rất vui.","vi"),("toi dang rat buon","vi"),("😭","vi"),
            ("Tôi feel sad today","vi"),("I am very tired today.","en"),("Exhausted.","en")
        })
        {
            var negativeFixture=text.Contains("buon")||text.Contains("sad")||text=="😭";
            inference.BilingualScenario=negativeFixture?"negative":"matched";
            var result=await Analyze(text);
            check(result["language"]!.GetValue<string>()==language,"Backend selected the wrong language.");
            check(result["status"]!.GetValue<string>()=="matched" && result["accepted"]!.GetValue<bool>()
                && result["analysisId"]!.GetValue<string>().StartsWith("va_"),"Accepted analysis missing receipt: "+text+" / "+result["reasonCode"]?.GetValue<string>());
            check(result["mappingVersion"]!.GetValue<string>()==BilingualMoodAnalysisService.MappingVersion(BilingualMoodAnalysisService.PolicyVersion(language),result["mappingMethod"]!.GetValue<string>()),"Missing versioned context policy.");
            check((result["confidence"] is not null)==(language=="vi"),"Regression must not invent confidence.");
            var response=await call("POST","/Recommendation",Confirm(result),200,token);
            var id=result["analysisId"]!.GetValue<string>();
            var history=await db.LichSuTamTrangs.FirstAsync(x=>x.AnalysisId==id);
            var chosen=moods.Single(x=>x.TamTrangID==history.TamTrangID);
            var rawV=negativeFixture?-.71m:.734m;var rawA=negativeFixture?-.44m:-.219m;
            var target=SemanticMoodMapper.Project(rawV,rawA,chosen);
            check(history.Valence==target.Valence && history.Arousal==target.Arousal && history.AnalysisValence==rawV && history.AnalysisArousal==rawA,"Recommendation target/raw model provenance lost.");
            check(history.AnalyzerVersion==(language=="vi"?"phobert_fixture":"roberta_va_fixture")
                && history.MappingVersion==result["mappingVersion"]!.GetValue<string>()
                && history.AnalysisLanguage==language && history.AnalysisSource=="ai_va" && history.DecisionSource=="ai_va",
                "History lost trusted bilingual provenance.");
            var stored=await db.Database.GetCollection<BsonDocument>(BilingualMoodAnalysisService.ReceiptCollection).Find(new BsonDocument("_id",id)).FirstAsync();
            check(!stored.Contains("text") && !stored.Contains("inputText"),"Receipt stored raw mood text.");
            var repeated=await call("POST","/Recommendation",Confirm(result),200,token);
            check(response!["recommendationId"]!.GetValue<string>()==repeated!["recommendationId"]!.GetValue<string>()
                && await db.LichSuTamTrangs.CountAsync(x=>x.AnalysisId==id)==1,"Replay duplicated history.");
            await call("POST","/Recommendation",Confirm(result),409,otherToken);
            await call("POST","/Recommendation",new {source="ai_va",analysisId=id,tamTrangID=history.TamTrangID,mode="hybrid",limit=4},409,token);
        }

        // Upgrade a legacy strict receipt validator without rewriting documents or other rules.
        var schemaRoot=Environment.GetEnvironmentVariable("MOODIFY_TEST_ROOT") ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../"));
        var schemaPath=Path.Combine(schemaRoot,"backend/Data/mongo-schema.json");
        var legacy=BsonDocument.Parse(await File.ReadAllTextAsync(schemaPath))["aiVaAnalyses"].DeepClone().AsBsonDocument;
        legacy["$jsonSchema"]["properties"].AsBsonDocument.Remove("inputScope");
        var receiptCount=await receipts.CountDocumentsAsync(FilterDefinition<VaAnalysisReceipt>.Empty);
        await db.Database.RunCommandAsync<BsonDocument>(new BsonDocument{{"collMod","aiVaAnalyses"},{"validator",legacy}});
        await db.Initialize(schemaPath);
        using(var options=await db.Database.ListCollectionsAsync(new ListCollectionsOptions{Filter=new BsonDocument("name","aiVaAnalyses")})) {
            var current=(await options.FirstAsync())["options"]["validator"].AsBsonDocument;
            check(current["$jsonSchema"]["properties"]["inputScope"]["enum"].AsBsonArray.Count==2,"Legacy validator did not accept optional input scope.");
            var stripped=current.DeepClone().AsBsonDocument;
            stripped["$jsonSchema"]["properties"].AsBsonDocument.Remove("inputScope");
            check(stripped.Equals(legacy)&&await receipts.CountDocumentsAsync(FilterDefinition<VaAnalysisReceipt>.Empty)==receiptCount,
                "Validator upgrade changed existing data or unrelated validation.");
        }
        await db.Initialize(schemaPath);
        check(await receipts.CountDocumentsAsync(FilterDefinition<VaAnalysisReceipt>.Empty)==receiptCount,
            "Repeated validator upgrade changed receipts.");

        inference.BilingualScenario="negative";
        foreach(var (text,expectedInput,expectedLanguage) in new[] {
            ("Vì tôi đang buồn nên tôi muốn có một bài gì đó giúp làm nổi lên cái buồn của tui. Bạn hiểu mà đúng k?","Vì tôi đang buồn","vi"),
            ("I am sad so I want a cheerful song to help me feel better.","I am sad","en"),
            ("Tôi muốn nghe nhạc vui vì tôi đang buồn.","tôi đang buồn","vi"),
            ("I want a happy song because I feel sad.","I feel sad","en")
        }) {
            var beforeCalls=inference.BilingualCalls;
            var result=await Analyze(text);
            check(result["status"]!.GetValue<string>()=="matched"&&result["mood"]!["tenTamTrang"]!.GetValue<string>()=="Sad",
                "Request obscured current mood: "+text);
            check(inference.LastBilingualText==expectedInput&&inference.BilingualCalls==beforeCalls+1,
                "Focused analysis must make exactly one prediction on original self-description.");
            check(result["inputScope"]!.GetValue<string>()=="focused_self_description"
                && result["analyzedCharacterCount"]!.GetValue<int>()==expectedInput.Length
                && result["language"]!.GetValue<string>()==expectedLanguage,"Input provenance missing.");
            var id=result["analysisId"]!.GetValue<string>();
            var receipt=await receipts.Find(x=>x.Id==id).FirstAsync();
            check(receipt.InputScope=="focused_self_description"&&receipt.Valence==-.71m,
                "Focused model raw VA/receipt provenance lost.");
            var recommendation=await call("POST","/Recommendation",Confirm(result),200,token);
            check(recommendation!["items"]!.AsArray().Count>0,"Focused analysis did not produce actual recommendations.");
        }
        inference.BilingualScenario="low_negative";
        var focusedLow=await Analyze("Tôi đang buồn nên tôi muốn một bài hát vui.");
        check(focusedLow["reasonCode"]!.GetValue<string>()=="low_confidence"&&focusedLow["analysisId"] is null,
            "Input focus bypassed the model acceptance gate.");
        inference.BilingualScenario="negative";
        var conflictingRequest=await Analyze("Tôi buồn nên tôi muốn nghe nhạc vì tôi đang vui.");
        check(conflictingRequest["reasonCode"]!.GetValue<string>()=="mixed_emotions"
            &&conflictingRequest["inputScope"]!.GetValue<string>()=="full_text"&&conflictingRequest["analysisId"] is null,
            "Input focus hid a conflicting self-report.");

        inference.BilingualScenario="matched";
        foreach(var (text,expected) in new[] {
            ("Tôi vui","Happy"),("I am happy today","Happy"),("Tôi hào hứng","Excited"),("I am excited","Excited"),
            ("Tôi bình yên và thư giãn","Relaxed"),("I feel relaxed","Relaxed"),
            ("Tôi tập trung làm bài","Focused"),("I am focused","Focused"),
            ("Tôi đang hoài niệm","Nostalgic"),("I feel nostalgic","Nostalgic")
        }) {
            var mapped=await Analyze(text);
            check(mapped["mood"]!["tenTamTrang"]!.GetValue<string>()==expected,"Explicit context was overwritten by model coordinates: "+text);
            check(mapped["mappingMethod"]!.GetValue<string>()=="explicit_self_description","Text rule provenance missing.");
        }
        foreach(var text in new[] {
            "Tôi không vui","I am not happy","Tôi vui nhưng buồn","I feel happy and sad",
            "Tôi muốn nghe nhạc vui","I want happy music","Tôi tức giận","I am angry",
            "Hôm qua tôi vui","I was happy yesterday","Bạn tôi vui","My friend is happy"
        }) {
            var uncertain=await Analyze(text);
            check(uncertain["status"]!.GetValue<string>()=="unknown"&&uncertain["analysisId"] is null
                &&uncertain["mood"] is null,"Ambiguous/unsupported self report forced a mood: "+text);
        }
        inference.BilingualScenario="low";
        var explicitLow=await Analyze("Tôi vui");
        check(explicitLow["reasonCode"]!.GetValue<string>()=="low_confidence"&&explicitLow["analysisId"] is null,
            "Context rules bypassed the model acceptance gate.");
        inference.BilingualScenario="matched";

        inference.BilingualScenario="negative";
        foreach(var text in new[]{"Hôm nay tôi không được vui","Hôm nay t k vui","I am not happy today","I do not feel particularly happy"}) {
            var negative=await Analyze(text);
            check(negative["status"]!.GetValue<string>()=="matched"&&negative["mood"]!["tenTamTrang"]!.GetValue<string>()=="Sad",
                "Compatible model evidence was blocked by negation: "+text);
            check(negative["mappingMethod"]!.GetValue<string>()=="model_negation","Negation/model provenance missing.");
        }
        foreach(var text in new[]{"Tôi không còn buồn nữa","I am not sad","Tôi không vui cũng không buồn","I am neither happy nor sad","Không phải là tôi không vui"}) {
            var rejected=await Analyze(text);
            check(rejected["status"]!.GetValue<string>()=="unknown"&&rejected["analysisId"] is null,"Model contradicted a negated emotion: "+text);
        }
        inference.BilingualScenario="low_negative";
        var belowThreshold=await Analyze("Hôm nay tôi không được vui");
        check(belowThreshold["status"]!.GetValue<string>()=="unknown"&&belowThreshold["reasonCode"]!.GetValue<string>()=="low_confidence",
            "Negation relaxed the artifact/policy acceptance gate.");
        inference.BilingualScenario="matched";
        foreach(var text in new[]{"Сегодня я счастлив","今日は嬉しい","أنا سعيد","今天很开心"}) {
            var foreign=await Analyze(text);
            check(foreign["status"]!.GetValue<string>()=="unsupported_language","Unsupported script silently entered English.");
        }
        foreach(var language in new[]{"vi","en"}) {
            await call("POST","/MoodAnalysis/valence-arousal",new{text=new string('a',4001),language},400,token);
        }
        foreach(var (text,language) in new[]{("Tôi đang buồn","en"),("I feel happy today","vi")}) {
            var mismatch=await Analyze(text,language);
            check(mismatch["reasonCode"]!.GetValue<string>()=="language_mismatch"&&mismatch["analysisId"] is null,
                "Explicit language override silently sent text to the wrong model.");
        }


        foreach(var text in new[]{"12345","!!! ???","https://example.test/sad-music"}) {
            var guardedCallCount=inference.BilingualCalls;
            var invalid=await Analyze(text);
            check(invalid["reasonCode"]!.GetValue<string>()=="insufficient_text"&&invalid["analysisId"] is null
                &&inference.BilingualCalls==guardedCallCount,"Non-mood input used inference or issued receipt: "+text);
        }
        foreach(var text in new[]{"Tôi đang vui 今天","I feel happy 今日は"}) {
            var guardedCallCount=inference.BilingualCalls;
            var foreignMixed=await Analyze(text);
            check(foreignMixed["status"]!.GetValue<string>()=="unsupported_language"&&inference.BilingualCalls==guardedCallCount,
                "Unsupported mixed scripts silently used a model.");
        }
        foreach(var text in new[]{"I feel happy at the café.","I'm sad after visiting Hà Nội."}) {
            inference.BilingualScenario=text.Contains("sad")?"negative":"matched";
            var routed=await Analyze(text);
            check(routed["language"]!.GetValue<string>()=="en"&&routed["status"]!.GetValue<string>()=="matched",
                "Accented English used the wrong model.");
        }
        inference.BilingualScenario="matched";

        var english=await Analyze();
        var englishId=english["analysisId"]!.GetValue<string>();
        var selected=english["mood"]!["tamTrangID"]!.GetValue<int>();
        await call("POST","/Recommendation",new {source="ai_va",tamTrangID=selected,valence=.1,arousal=.2,modelVersion="fake"},400,token);
        await call("POST","/Recommendation",new {source="ai_va",analysisId=englishId,tamTrangID=selected,valence=.1,arousal=.2},400,token);
        await call("POST","/Recommendation",new {source="ai_va",analysisId=englishId,tamTrangID=selected,modelVersion="fake"},400,token);
        await call("POST","/Recommendation",new {source="ai_va",analysisId="va_"+new string('f',32),tamTrangID=selected},409,token);
        var different=moods.First(m=>m.TamTrangID!=selected);
        await call("POST","/Recommendation",Confirm(english,moodId:different.TamTrangID),409,token);
        await call("POST","/Recommendation",Confirm(english,"manual",different.TamTrangID),200,token);
        var manual=await db.LichSuTamTrangs.FirstAsync(x=>x.AnalysisId==englishId);
        check(manual.Source=="manual" && manual.DecisionSource=="manual" && manual.AnalysisSource=="ai_va"
            && manual.AnalysisValence==.734m && manual.Valence==(different.MinValence+different.MaxValence)/2m,
            "Manual override masqueraded as AI VA.");

        var expired=await Analyze();
        var expiredId=expired["analysisId"]!.GetValue<string>();
        await receipts.UpdateOneAsync(x=>x.Id==expiredId,Builders<VaAnalysisReceipt>.Update.Set(x=>x.ExpiresAt,DateTime.UtcNow.AddSeconds(-1)));
        await call("POST","/Recommendation",Confirm(expired),409,token);
        var stale=await Analyze();
        inference.VaVersion="different_va_fixture";
        await call("POST","/Recommendation",Confirm(stale),409,token);
        inference.VaVersion="roberta_va_fixture";
        var changed=await Analyze();
        var unrelated=moods.First(m=>m.TamTrangID!=selected);
        var originalName=unrelated.TenTamTrang;
        unrelated.TenTamTrang += " changed";
        await db.Update(unrelated);
        await call("POST","/Recommendation",Confirm(changed),409,token);
        unrelated.TenTamTrang=originalName;await db.Update(unrelated);
        var policyStale=await Analyze();
        var policyId=policyStale["analysisId"]!.GetValue<string>();
        await receipts.UpdateOneAsync(x=>x.Id==policyId,Builders<VaAnalysisReceipt>.Update.Set(x=>x.MappingVersion,"old-policy"));
        await call("POST","/Recommendation",Confirm(policyStale),409,token);

        var before=await receipts.CountDocumentsAsync(FilterDefinition<VaAnalysisReceipt>.Empty);
        inference.BilingualScenario="low";
        var low=await Analyze("Tôi vừa buồn vừa vui");
        check(low["status"]!.GetValue<string>()=="unknown" && low["mood"] is null && low["analysisId"] is null
            && !low["accepted"]!.GetValue<bool>(),"Low-confidence output must abstain.");
        check(await receipts.CountDocumentsAsync(FilterDefinition<VaAnalysisReceipt>.Empty)==before,"Abstention issued a receipt.");
        inference.BilingualScenario="matched";
        var unsupported=await Analyze("今天很开心");
        check(unsupported["status"]!.GetValue<string>()=="unsupported_language" && unsupported["analysisId"] is null,
            "Unsupported script must not silently route to English.");

        foreach(var scenario in new[]{"missing_va","out_of_range","wrong_language","bad_version","bad_labels","bad_confidence","malformed"})
        {
            inference.BilingualScenario=scenario;
            var bad=await call("POST","/MoodAnalysis/valence-arousal",new{text="Tôi thấy vui",language="auto"},503,token);
            check(bad!["reasonCode"]!.GetValue<string>()=="invalid_response" && bad["analysisId"] is null,"Malformed prediction must fail closed.");
        }
        inference.BilingualScenario="matched";
        foreach(var code in new[]{429,503,500,422})
        {
            inference.BilingualStatusCode=code;
            var failure=await call("POST","/MoodAnalysis/valence-arousal",new{text="I feel sad",language="auto"},code==422?200:503,token);
            check(failure!["analysisId"] is null && !failure["accepted"]!.GetValue<bool>(),"Failed runtime issued an accepted result.");
        }
        inference.BilingualStatusCode=200;

        var disableConfig=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"AI:Enabled","false"}}).Build();
        var disabled=new BilingualMoodAnalysisService(factory,db,disableConfig,TimeProvider.System,new BilingualPolicyService(db));
        var disabledCallCount=inference.BilingualCalls;
        foreach(var text in new[]{"Tôi thấy vui","I feel happy"})
            check((await disabled.AnalyzeAsync(text,"auto",account.TaiKhoanID,default)).ReasonCode=="disabled","Disabled flag inconsistent across languages.");
        check(inference.BilingualCalls==disabledCallCount,"Disabled AI still called the model.");
        var timeoutService=new BilingualMoodAnalysisService(new TimeoutFactory(),db,enabled,TimeProvider.System,new BilingualPolicyService(db));
        check((await timeoutService.AnalyzeAsync("I feel happy","auto",account.TaiKhoanID,default)).ReasonCode=="timeout","Timeout contract lost.");
        using(var cancelled=new CancellationTokenSource())
        {
            cancelled.Cancel();
            try { await timeoutService.AnalyzeAsync("I feel happy","auto",account.TaiKhoanID,cancelled.Token); throw new Exception("Caller cancellation swallowed."); }
            catch(OperationCanceledException) { check(true,"Cancellation propagated."); }
        }

        var concurrent=await Analyze();
        var concurrentId=concurrent["analysisId"]!.GetValue<string>();
        var concurrentMood=concurrent["mood"]!["tamTrangID"]!.GetValue<int>();
        var leases=await Task.WhenAll(Enumerable.Range(0,6).Select(_=>service.ReserveAsync(concurrentId,account.TaiKhoanID,concurrentMood,"ai_va",default)));
        check(leases.Count(x=>x is not null)==1,"Concurrent confirmation must own one Mongo lease.");
        foreach(var lease in leases) if(lease is not null) await lease.DisposeAsync();
        await using(var retry=await service.ReserveAsync(concurrentId,account.TaiKhoanID,concurrentMood,"ai_va",default))
            check(retry is not null,"A released lease prevented retry.");
        check(await disabled.ReserveAsync(concurrentId,account.TaiKhoanID,concurrentMood,"ai_va",default) is null,"Disabled flag allowed confirmation.");

        var restartAnalysis=await Analyze();
        var original=await call("POST","/Recommendation",Confirm(restartAnalysis),200,token);
        var pending=await Analyze("Tôi thấy vui");
        await restart();
        var replay=await call("POST","/Recommendation",Confirm(restartAnalysis),200,token);
        check(original!["recommendationId"]!.GetValue<string>()==replay!["recommendationId"]!.GetValue<string>(),
            "Backend restart lost idempotent replay.");
        await call("POST","/Recommendation",Confirm(pending),200,token);
        check(await db.LichSuTamTrangs.CountAsync(x=>x.AnalysisId==restartAnalysis["analysisId"]!.GetValue<string>())==1,"Restart duplicated history.");

        var indexes=await (await receipts.Indexes.ListAsync()).ToListAsync();
        check(indexes.Any(x=>x["name"]=="expire_ai_va_analysis" && x["expireAfterSeconds"].ToInt32()==0),"Receipt TTL index missing.");
        var historyApi=await call("GET","/History",null,200,token);
        check(historyApi!["items"]!.AsArray().Any(x=>x!["analysisLanguage"]?.GetValue<string>()=="vi"
            && x["analysisModelType"]?.GetValue<string>()=="emotion_classification_va_adapter"),"History API omitted model type/language.");
    }

    sealed class InferenceFactory(Uri address):IHttpClientFactory
    {
        public HttpClient CreateClient(string name)=>new(){BaseAddress=address,Timeout=TimeSpan.FromSeconds(2)};
    }
    sealed class TimeoutFactory:IHttpClientFactory
    {
        public HttpClient CreateClient(string name)=>new(new TimeoutHandler()){BaseAddress=new Uri("http://test.invalid/")};
    }
    sealed class TimeoutHandler:HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct) =>
            Task.FromException<HttpResponseMessage>(new TaskCanceledException("Fixture timeout",null,ct));
    }
}
