using System.Text.Json.Nodes;
using MongoDB.Driver;
using mood_recommendation.Data;
using mood_recommendation.Services;

public static class ModelQualityTests {
    public static async Task Run(MongoStore db,Func<string,string,object?,int,string?,Task<JsonNode?>> call,
        Action<bool,string> check,string engineer,string admin,string user,FakeInferenceServer inference) {
        await call("GET","/ModelQuality",null,401,null);
        await call("GET","/ModelQuality",null,403,user);
        await call("GET","/ModelQuality",null,403,admin);
        await call("GET","/ModelQuality",null,200,engineer);
        var calls=inference.ModelActivations;
        foreach(var route in new[]{"/Models/vi/activate","/ModelLifecycle/vi/activate","/Models/va/activate","/ModelLifecycle/va/activate"})
            await call("POST",route,new{version="quality_fixture_candidate",expectedRevision=0},409,engineer);
        check(inference.ModelActivations==calls,"An unapproved candidate reached the inference activation endpoint.");
        var policy=await call("POST","/ModelQuality/policies",new{kind="vi",minimumValidationSamples=20,minimumTestSamples=20,
            minimumMacroF1=.5,minimumCoverage=.2,minimumSelectiveAccuracy=.8,
            reason="Synthetic promotion gate contract only; these metrics are not NLP quality evidence."},201,engineer);
        var policyId=policy!["id"]!.GetValue<string>();
        var rows=new List<object>();
        foreach(var split in new[]{"validation","test"})
        for(var n=0;n<20;n++)rows.Add(new{id=split+n,split,reviewed=true,text="Synthetic reviewed contract example "+split+" "+n,emotion="Enjoyment"});
        var data=await call("POST","/AiExperiments/datasets",new{name="Quality contract fixture",taskType="vsmec7",language="vi",
            purpose="evaluation",independentEvaluation=true,source="Synthetic contract fixture only; not actual reviewed NLP data.",
            annotationMethod="Generated labels exercise promotion checks; never used on the main database.",rows},201,engineer);
        async Task<JsonNode> Job()=> (await call("POST","/AiExperiments/jobs",new{datasetId=data!["id"]!.GetValue<string>(),
            modelVersion="quality_fixture_candidate",baseVersion="phobert_fixture",qualityPolicyId=policyId},201,engineer))!;
        object Report(JsonNode job,double f1,string bundle="")=>new{
            jobId=job["id"]!.GetValue<string>(),modelVersion="quality_fixture_candidate",
            datasetHash=job["datasetHash"]!.GetValue<string>(),configHash=job["configHash"]!.GetValue<string>(),
            artifactHash=new string('a',64),bundleSha256=string.IsNullOrEmpty(bundle)?new string('b',64):bundle,testSamples=20,
            artifactFiles=new[]{new{path="config.json",sha256=new string('c',64)},new{path="model.safetensors",sha256=new string('a',64)}},
            metrics=new{macroF1=f1,coverage=.5,selectiveAccuracy=.85},
            validationMetrics=new{macroF1=f1,coverage=.5,selectiveAccuracy=.85}};
        object Review(JsonNode job,bool confirmed=true)=>new{jobId=job["id"]!.GetValue<string>(),independentReviewConfirmed=confirmed,
            reviewEvidence="Synthetic software contract approval in isolated DB; not independent model quality."};
        var failed=await Job();
        await call("POST","/AiExperiments/jobs/"+failed["id"]!.GetValue<string>()+"/report",Report(failed,.1),200,engineer);
        await call("POST","/ModelQuality/approvals",Review(failed),409,engineer);
        var mismatched=await Job();
        await call("POST","/AiExperiments/jobs/"+mismatched["id"]!.GetValue<string>()+"/report",Report(mismatched,.7,new string('e',64)),200,engineer);
        await call("POST","/ModelQuality/approvals",Review(mismatched),409,engineer);
        var passed=await Job();
        await call("POST","/AiExperiments/jobs/"+passed["id"]!.GetValue<string>()+"/report",Report(passed,.7),200,engineer);
        await call("POST","/ModelQuality/approvals",Review(passed),403,admin);
        await call("POST","/ModelQuality/approvals",Review(passed,false),400,engineer);
        await call("POST","/ModelQuality/approvals",Review(passed),201,engineer);
        await call("POST","/ModelLifecycle/vi/activate",new{version="quality_fixture_candidate",expectedRevision=0},200,engineer);
        check(inference.ViVersion=="quality_fixture_candidate","Approved candidate did not activate.");
        inference.FingerprintTampered=true;
        calls=inference.ModelActivations;
        await call("POST","/Models/vi/activate",new{version="quality_fixture_candidate"},409,engineer);
        check(inference.ModelActivations==calls,"A changed-on-disk artifact reused a stale quality/baseline approval.");
        inference.FingerprintTampered=false;
        await call("POST","/ModelLifecycle/vi/rollback",new{expectedRevision=0},200,engineer);
        check(inference.ViVersion=="phobert_fixture","The existing baseline could not be restored after candidate promotion.");
        var p=new NlpQualityPolicy{Kind="va",MaximumMae=.4,MaximumRmse=.6};
        check(!ModelQualityService.MetricsPass(p,new Dictionary<string,double>{{"valenceMae",.1}}),"Missing VA metrics passed.");
        check(ModelQualityService.MetricsPass(p,new Dictionary<string,double>{{"valenceMae",.1},{"arousalMae",.2},{"valenceRmse",.2},{"arousalRmse",.3}}),"Valid VA policy thresholds failed.");
        check(!ModelQualityService.MetricsPass(p,new Dictionary<string,double>{{"valenceMae",.1},{"arousalMae",.7},{"valenceRmse",.2},{"arousalRmse",.3}}),"A failed VA axis passed the gate.");
    }
}