using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using mood_recommendation.Data;
using mood_recommendation.Models;
using mood_recommendation.Services;

public static class AiCompletionTests
{
    public static async Task Run(MongoStore db,Func<string,string,object?,int,string?,Task<JsonNode?>> call,
        Action<bool,string> check,string root,string engineer,string admin,string user)
    {
        foreach(var path in new[]{"/AiPolicies","/RecommenderModels","/AiExperiments","/RecommendationExperiments"}){
            await call("GET",path,null,401,null);await call("GET",path,null,403,user);await call("GET",path,null,403,admin);
            await call("GET",path,null,200,engineer);
        }
        var moods=await db.TamTrangs.ToListAsync();
        var moodId=moods[0].TamTrangID;
        var old=await call("POST","/MoodAnalysis/valence-arousal",new{text="Tôi thấy vui",language="auto"},200,user);
        var oldId=old!["analysisId"]!.GetValue<string>();
        var draft=await call("POST","/AiPolicies",new{language="vi",moodIds=moods.Select(m=>m.TamTrangID).ToArray(),minimumConfidence=0,reason="Reviewed test policy fixture; no changed anchors."},201,engineer);
        var policy=draft!["id"]!.GetValue<string>();
        await call("POST","/AiPolicies/"+policy+"/activate",new{expectedRevision=0},403,user);
        await call("POST","/AiPolicies/"+policy+"/activate",new{expectedRevision=0},200,engineer);
        await call("POST","/AiPolicies/"+policy+"/activate",new{expectedRevision=0},409,engineer);
        await call("POST","/Recommendation",new{source="ai_va",analysisId=oldId,tamTrangID=old["mood"]!["tamTrangID"]!.GetValue<int>()},409,user);
        var updated=await call("POST","/MoodAnalysis/valence-arousal",new{text="Tôi thấy vui",language="auto"},200,user);
        check(updated!["mappingVersion"]!.GetValue<string>()==BilingualMoodAnalysisService.MappingVersion(policy,updated["mappingMethod"]!.GetValue<string>()),"Approved policy did not reach mobile API.");
        var pipeline=await call("GET","/AiWorkspace/pipelines",null,200,engineer);
        check(pipeline!["policies"]![0]!["mappingVersion"]!.GetValue<string>()==policy,"Dashboard reported stale policy.");
        await call("POST","/AiPolicies/vi/reset",new{expectedRevision=1},200,engineer);
        await call("POST","/AiPolicies",new{language="en",moodIds=new[]{moodId},minimumConfidence=.5,reason="Reviewed English classifier confidence floor fixture"},201,engineer);
        await call("POST","/AiPolicies",new{language="vi",moodIds=new[]{int.MaxValue},minimumConfidence=0,reason="Missing target fixture"},400,engineer);
        check((await call("GET","/AiPolicies/metrics",null,200,engineer))!.AsArray().Any(x=>x!["language"]!.GetValue<string>()=="vi"),"Inference metrics omitted VI.");
        await call("GET","/AiPolicies/metrics",null,403,user);

        object[] NlpRows(bool reviewed=true)=>new object[]{
            new{id="n1",text="Reviewed first joy",emotion="Enjoyment",split="train",reviewed},
            new{id="n2",text="Reviewed second fear",emotion="Fear",split="validation",reviewed},
            new{id="n3",text="Reviewed third sadness",emotion="Sadness",split="test",reviewed}};
        await call("POST","/AiExperiments/datasets",new{name="Unreviewed",taskType="vsmec7",language="vi",source="Synthetic test data only",annotationMethod="Fixture labels for software contract",rows=NlpRows(false)},400,engineer);
        var dataset=await call("POST","/AiExperiments/datasets",new{name="Typed VSMEC fixture",taskType="vsmec7",language="vi",source="Synthetic test data only",annotationMethod="Fixture labels for software contract",rows=NlpRows()},201,engineer);
        var job=await call("POST","/AiExperiments/jobs",new{datasetId=dataset!["id"]!.GetValue<string>(),modelVersion="typed_vsmec_fixture",baseVersion="phobert_fixture",epochs=2},201,engineer);
        var jobId=job!["id"]!.GetValue<string>();
        await call("POST","/AiExperiments/jobs/"+jobId+"/report",new{jobId,modelVersion="typed_vsmec_fixture",datasetHash=job["datasetHash"]!.GetValue<string>(),
            configHash=job["configHash"]!.GetValue<string>(),artifactHash=new string('a',64),testSamples=1,metrics=new{valenceMae=.1}},400,engineer);
        await call("POST","/AiExperiments/jobs/"+jobId+"/report",new{jobId,modelVersion="typed_vsmec_fixture",datasetHash=job["datasetHash"]!.GetValue<string>(),
            configHash=job["configHash"]!.GetValue<string>(),artifactHash=new string('a',64),testSamples=1,metrics=new{macroF1=.5,coverage=.3}},200,engineer);
        await call("POST","/AiExperiments/jobs/"+jobId+"/report",new{jobId,modelVersion="typed_vsmec_fixture",datasetHash=job["datasetHash"]!.GetValue<string>(),
            configHash=job["configHash"]!.GetValue<string>(),artifactHash=new string('a',64),testSamples=1,metrics=new{macroF1=.5,coverage=.3}},409,engineer);
        var recRows=new List<object>();
        foreach(var (split,day) in new[]{("train",1),("validation",11),("test",21)})
        for(var n=0;n<4;n++)recRows.Add(new{id=split+n,split,reviewed=true,userId=n+1,contentId="fixture-item",stars=n%2==0?5:1,timestamp=$"2024-01-{day:00}T00:00:00Z"});
        var recData=await call("POST","/AiExperiments/datasets",new{name="Typed MF fixture",taskType="recommender",language="none",
            source="Synthetic contract fixture",annotationMethod="Generated for execution test, not quality evidence",rows=recRows},201,engineer);
        var recJob=await call("POST","/AiExperiments/jobs",new{datasetId=recData!["id"]!.GetValue<string>(),modelVersion="mf_bundle_fixture",epochs=5},201,engineer);
        var bundle=await call("GET","/AiExperiments/jobs/"+recJob!["id"]!.GetValue<string>()+"/bundle",null,200,engineer);
        var folder=Path.Combine(root,".local-build","typed-training-smoke",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        var bundlePath=Path.Combine(folder,"bundle.json");await File.WriteAllTextAsync(bundlePath,bundle!.ToJsonString());
        var start=new ProcessStartInfo(Path.Combine(root,"ai-service/.venv/Scripts/python.exe")){WorkingDirectory=Path.Combine(root,"ai-service"),
            UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var arg in new[]{"train_experiment.py","--bundle",bundlePath,"--out",Path.Combine(folder,"artifact")})start.ArgumentList.Add(arg);
        using(var process=Process.Start(start)!){
            var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();var details=await error;await output;check(process.ExitCode==0,"Typed MF runner failed: "+details);
        }
        var trained=JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(folder,"artifact/report.json")))!;
        await call("POST","/AiExperiments/jobs/"+recJob["id"]!.GetValue<string>()+"/report",trained,200,engineer);

        await call("POST","/RecommenderModels",new{model=(object?)null,source="Malformed fixture artifact",reviewed=true},400,engineer);
        var userInfo=await call("GET","/Auth/me",null,200,user);
        var currentId=userInfo!["taiKhoanID"]!.GetValue<int>();
        var accountIds=new List<int>{currentId};
        for(var n=0;n<20;n++){
            var account=new TaiKhoan{TenDangNhap="mf_contract_"+n,Email="mf-contract-"+n+"@test.invalid",MatKhau="not-used-for-login"};
            await db.Insert(account);accountIds.Add(account.TaiKhoanID);
        }
        var items=await db.NoiDungGiaiTris.Take(2).ToListAsync();
        check(items.Count==2,"MF contract needs two catalog items.");
        object Model(string version,double factor)=>new{schemaVersion=1,version,factors=1,globalMean=0,datasetHash=new string('b',64),
            trainBefore="2024-01-10T00:00:00Z",validationBefore="2024-01-20T00:00:00Z",
            users=accountIds.Select(id=>new{userId=id,bias=0,factors=new[]{.5}}).ToArray(),
            items=new[]{new{contentId=items[0].NoiDungID,bias=0,factors=new[]{factor}},new{contentId=items[1].NoiDungID,bias=0,factors=new[]{-factor}}},
            validationRows=accountIds.SelectMany(id=>new[]{new{userId=id,contentId=items[0].NoiDungID,stars=4,timestamp="2024-01-11T00:00:00Z"},new{userId=id,contentId=items[1].NoiDungID,stars=2,timestamp="2024-01-11T00:00:00Z"}}).ToArray(),
            testRows=accountIds.SelectMany(id=>new[]{new{userId=id,contentId=items[0].NoiDungID,stars=4,timestamp="2024-01-21T00:00:00Z"},new{userId=id,contentId=items[1].NoiDungID,stars=2,timestamp="2024-01-21T00:00:00Z"}}).ToArray()};
        var modelA=await call("POST","/RecommenderModels",new{model=Model("mf_contract_a",.8),source="Synthetic factors for integration, not quality evidence",reviewed=true},201,engineer);
        check(modelA!["deployable"]!.GetValue<bool>(),"Server did not recompute the explicit MF holdout gate.");
        await call("POST","/RecommenderModels/mf_contract_a/activate",new{expectedRevision=0},200,engineer);
        await call("POST","/RecommenderModels/mf_contract_a/activate",new{expectedRevision=0},409,engineer);
        var mfResponse=await call("POST","/Recommendation",new{tamTrangID=moodId,mode="hybrid",limit=50},200,user);
        check(mfResponse!["collaborativeModelVersion"]!.GetValue<string>()=="mf_contract_a","MF is not wired into production Hybrid path.");
        var mfItem=mfResponse["items"]!.AsArray().First(x=>x!["content"]!["noiDungID"]!.GetValue<string>()==items[0].NoiDungID);
        check(Math.Abs(mfItem!["collaborativeScore"]!.GetValue<double>()-.7)<1e-9,"Serving MF score differs from dot-product prediction.");
        await call("POST","/RecommenderModels",new{model=Model("mf_contract_b",.2),source="A different synthetic artifact for rollback",reviewed=true},201,engineer);
        await call("POST","/RecommenderModels/mf_contract_b/activate",new{expectedRevision=1},200,engineer);
        await call("POST","/RecommenderModels/rollback",new{expectedRevision=2},200,engineer);
        check((await call("GET","/RecommenderModels",null,200,engineer))!["state"]!["version"]!.GetValue<string>()=="mf_contract_a","MF rollback failed.");
        await call("POST","/RecommenderModels/deactivate",new{expectedRevision=3},200,engineer);
        var baseline=await call("POST","/Recommendation",new{tamTrangID=moodId,mode="hybrid",limit=2},200,user);
        check(baseline!["collaborativeModelVersion"] is null,"Baseline fallback retained MF after deactivation.");

        // Rating feedback is exercised through real HTTP calls and subsequent ranking, not only a static function.
        var feedbackAccount=new TaiKhoan{TenDangNhap="feedback_phase2",Email="feedback-phase2@test.invalid",
            MatKhau=BCrypt.Net.BCrypt.HashPassword("Feedback-fixture-1")};
        await db.Insert(feedbackAccount);
        var feedbackToken=(await call("POST","/Auth/login",new{identifier=feedbackAccount.Email,matKhau="Feedback-fixture-1"},200,null))!["token"]!.GetValue<string>();
        foreach(var id in new[]{"feedback_phase2_a","feedback_phase2_b"})
            await db.SaveContent(new NoiDungGiaiTri{NoiDungID=id,TieuDe=id,LoaiNoiDung="Movie",Valence=0,Arousal=0,
                Phim=new Phim{Director=id,TheLoai=id}},create:true);
        async Task<JsonNode> FeedbackRecommendations()=> (await call("POST","/Recommendation",
            new{tamTrangID=moodId,mode="content",limit=50,valence=0,arousal=0},200,feedbackToken))!;
        var first=await FeedbackRecommendations();
        await call("POST","/History/ratings",new{noiDungID="feedback_phase2_a",soSao=5,recommendationId=first["recommendationId"]!.GetValue<string>()},200,feedbackToken);
        var liked=await FeedbackRecommendations();
        var likedItem=liked["items"]!.AsArray().First(x=>x!["content"]!["noiDungID"]!.GetValue<string>()=="feedback_phase2_a");
        check(likedItem!["contentScore"]!.GetValue<double>()>.99,"Five-star feedback did not reach the next content profile.");
        await call("POST","/Interactions",new{contentId="feedback_phase2_a",action="external_open",
            eventId=Guid.NewGuid().ToString("N"),recommendationId=liked["recommendationId"]!.GetValue<string>()},200,feedbackToken);
        await call("POST","/History/ratings",new{noiDungID="feedback_phase2_a",soSao=1,recommendationId=liked["recommendationId"]!.GetValue<string>()},200,feedbackToken);
        var disliked=await FeedbackRecommendations();
        var dislikedItem=disliked["items"]!.AsArray().First(x=>x!["content"]!["noiDungID"]!.GetValue<string>()=="feedback_phase2_a");
        check(dislikedItem!["contentScore"]!.GetValue<double>()<.01,"Latest dislike failed to override weak link-open feedback.");

        check(RecommendationStrategyService.Bucket("stable",currentId)==RecommendationStrategyService.Bucket("stable",currentId),"Experiment assignment unstable.");
        check(RecommendationStrategyService.Reward(new[]{1},true)<RecommendationStrategyService.Reward([],true),"Weak open overrode explicit dislike.");
        var selected=RecommendationStrategyService.Select(new Dictionary<string,double>{{"baseline",0},{"content",.9},{"mood",-.2}},.1,.8,0);
        check(selected.Arm=="content"&&Math.Abs(selected.Propensity-(.9+.1/3))<1e-9,"Bandit failed measured exploitation/propensity.");
        await call("POST","/RecommendationExperiments",new{expectedRevision=0,enabled=true,trafficPercent=50,epsilon=.1,
            reviewEvidence="Synthetic integration pilot only; rollback before test cleanup."},200,engineer);
        var one=await call("POST","/Recommendation",new{tamTrangID=moodId,mode="hybrid",limit=2},200,user);
        var two=await call("POST","/Recommendation",new{tamTrangID=moodId,mode="hybrid",limit=2},200,user);
        check(one!["experimentGroup"]!.GetValue<string>()==two!["experimentGroup"]!.GetValue<string>(),"Account crossed A/B groups.");
        var experimentReport=await call("GET","/RecommendationExperiments",null,200,engineer);
        check(experimentReport!["groups"]!.AsArray().Sum(x=>x!["count"]!.GetValue<int>())==2,"Experiment generated-event log missing/duplicated.");
        await call("POST","/RecommendationExperiments",new{expectedRevision=1,enabled=false,trafficPercent=10,epsilon=.1,
            reviewEvidence="End synthetic pilot; restore deterministic baseline."},200,engineer);
    }
}
