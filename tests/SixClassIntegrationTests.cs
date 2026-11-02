using System.Text.Json.Nodes;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using mood_recommendation.Data;

public static class SixClassIntegrationTests
{
    public static async Task Run(MongoStore db,Func<string,string,object?,int,string?,Task<JsonNode?>> call,
        Action<bool,string> check,string engineer,string user,FakeInferenceServer inference)
    {
        var oldVa=inference.VaVersion;var oldVi=inference.ViVersion;
        inference.SixClassModels=true;inference.FingerprintTampered=false;
        inference.VaVersion="roberta_six_fixture";inference.ViVersion="phobert_six_fixture";
        try {
            foreach(var text in new[]{"I feel happy today.","Hôm nay tôi cảm thấy vui."}) {
                inference.BilingualScenario="matched";
                var r=(await call("POST","/MoodAnalysis/valence-arousal",new{text,language="auto"},200,user))!;
                check(r["status"]!.GetValue<string>()=="matched"&&r["accepted"]!.GetValue<bool>(),"Six-class result cannot be confirmed.");
                check(r["modelAccepted"] is null&&r["calibrationStatus"]!.GetValue<string>()=="uncalibrated",
                    "Unconfigured confidence was changed into calibrated model acceptance.");
                check(r["modelType"]!.GetValue<string>()=="emotion_classification_va_adapter"&&r["confidence"]!.GetValue<double>()==.94,
                    "English classifier was mislabeled as regression or lost confidence.");
                check(r["requiresConfirmation"]!.GetValue<bool>()&&r["analysisId"] is not null,"Six-class inference bypassed user confirmation or server receipt.");
                var id=r["analysisId"]!.GetValue<string>();var mood=r["mood"]!["tamTrangID"]!.GetValue<int>();
                await call("POST","/Recommendation",new{source="ai_va",analysisId=id,tamTrangID=mood,mode="hybrid",limit=2},200,user);
                var history=await db.LichSuTamTrangs.FirstAsync(h=>h.AnalysisId==id);
                check(history.AnalysisModelType=="emotion_classification_va_adapter"&&history.AnalysisValence==.74m,
                    "Six-class provenance/raw coordinates did not reach history.");
                await call("POST","/Recommendation",new{source="ai_va",analysisId=id,tamTrangID=mood,valence=.8,arousal=.3,modelVersion="client_tamper"},400,user);
                inference.BilingualScenario="low";
                var low=await call("POST","/MoodAnalysis/valence-arousal",new{text,language="auto"},200,user);
                check(low!["analysisId"] is null&&!low["accepted"]!.GetValue<bool>(),"Explicit classifier rejection issued a receipt.");
                inference.BilingualScenario="bad_probabilities";
                await call("POST","/MoodAnalysis/valence-arousal",new{text,language="auto"},503,user);
            }
            inference.BilingualScenario="matched";
            var pipes=await call("GET","/AiWorkspace/pipelines",null,200,engineer);
            check(pipes!["english"]!["modelType"]!.GetValue<string>()=="emotion_classification_va_adapter", "Engineer dashboard hides the deployed English classifier type.");
            var policy=await call("POST","/ModelQuality/policies",new{kind="va",taskType="emotion6_en",minimumValidationSamples=20,minimumTestSamples=20,
                minimumMacroF1=.5,minimumCoverage=.2,minimumSelectiveAccuracy=.8,reason="SOFTWARE TEST FIXTURE for task-specific promotion; not scientific model evaluation."},201,engineer);
            var dataset=await call("POST","/AiExperiments/datasets",new{name="Six-class software fixture",taskType="emotion6_en",language="en",purpose="evaluation",
                independentEvaluation=true,source="Synthetic software test fixture only, never model quality evidence.",annotationMethod="Fixture labels validate API schema and gates; not independently reviewed evaluation data.",
                rows=Enumerable.Range(0,40).Select(i=>new{id="six_"+i,text="Unique software fixture row "+i,emotion=new[]{"Sadness","Enjoyment","Love","Anger","Fear","Surprise"}[i%6],split=i<20?"validation":"test",reviewed=true}).ToArray()},201,engineer);
            var job=await call("POST","/AiExperiments/jobs",new{datasetId=dataset!["id"]!.GetValue<string>(),qualityPolicyId=policy!["id"]!.GetValue<string>(),modelVersion="quality_fixture_candidate",baseVersion="quality_fixture_candidate"},201,engineer);
            var jobId=job!["id"]!.GetValue<string>();
            await call("POST","/AiExperiments/jobs/"+jobId+"/report",new{jobId,datasetHash=job["datasetHash"]!.GetValue<string>(),configHash=job["configHash"]!.GetValue<string>(),modelVersion="quality_fixture_candidate",
                artifactHash=new string('a',64),bundleSha256=new string('b',64),testSamples=20,
                metrics=new{macroF1=.9,coverage=.9,selectiveAccuracy=.95},validationMetrics=new{macroF1=.9,coverage=.9,selectiveAccuracy=.95},
                artifactFiles=new[]{new{path="config.json",sha256=new string('c',64)},new{path="model.safetensors",sha256=new string('a',64)}}},200,engineer);
            inference.SixClassModels=false;
            await call("POST","/ModelQuality/approvals",new{jobId,independentReviewConfirmed=true,reviewEvidence="SOFTWARE FIXTURE verifies that a different taxonomy cannot reuse an approval."},409,engineer);
            inference.SixClassModels=true;
            await call("POST","/ModelQuality/approvals",new{jobId,independentReviewConfirmed=true,reviewEvidence="SOFTWARE FIXTURE promotion gate only; no real model quality claim or activation."},201,engineer);
        }
        finally {inference.SixClassModels=false;inference.VaVersion=oldVa;inference.ViVersion=oldVi;inference.BilingualScenario="matched";}
    }
}
