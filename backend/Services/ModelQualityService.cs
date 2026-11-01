using System.ComponentModel.DataAnnotations;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using mood_recommendation.Controllers;
using mood_recommendation.Data;

namespace mood_recommendation.Services;

public class NlpQualityPolicyInput {
    [Required,RegularExpression("^(vi|va)$")]public string Kind{get;set;}="";
    [Range(20,60000)]public int MinimumValidationSamples{get;set;}=50;
    [Range(20,60000)]public int MinimumTestSamples{get;set;}=50;
    [Range(0,1)]public double MinimumMacroF1{get;set;}=.55;
    [Range(0,1)]public double MinimumCoverage{get;set;}=.2;
    [Range(0,1)]public double MinimumSelectiveAccuracy{get;set;}=.8;
    [Range(.0001,2)]public double MaximumMae{get;set;}=.4;
    [Range(.0001,2)]public double MaximumRmse{get;set;}=.6;
    [Required,StringLength(2000,MinimumLength=30)]public string Reason{get;set;}="";
}
public sealed class NlpQualityPolicy:NlpQualityPolicyInput {
    [BsonId]public string Id{get;set;}="";public int ActorId{get;set;}public DateTime CreatedAt{get;set;}
}
public sealed class NlpFingerprintFile { public string Path{get;set;}="";public string Sha256{get;set;}=""; }
public sealed class NlpFingerprint {
    public string Kind{get;set;}="";public string Version{get;set;}="";public string BundleSha256{get;set;}="";
    public string WeightSha256{get;set;}="";public List<NlpFingerprintFile> Files{get;set;}=[];
    public bool Active{get;set;}public string? ActiveBundleSha256{get;set;}
}
public sealed class NlpQualityApproval {
    [BsonId]public string Id{get;set;}="";public string Kind{get;set;}="";public string Version{get;set;}="";
    public string BundleSha256{get;set;}="";public string WeightSha256{get;set;}="";
    public string Mode{get;set;}="";public string? JobId{get;set;}public string? PolicyId{get;set;}
    public string ReviewEvidence{get;set;}="";public int ActorId{get;set;}public DateTime CreatedAt{get;set;}
}
public sealed class NlpQualityApprovalInput {
    [Required]public string JobId{get;set;}="";
    public bool IndependentReviewConfirmed{get;set;}
    [Required,StringLength(3000,MinimumLength=30)]public string ReviewEvidence{get;set;}="";
}

/// <summary>Promotion gate for accountable, reviewed offline evidence. Metrics are submitted by an engineer, not server-recomputed predictions.</summary>
public sealed class ModelQualityService(MongoStore db,IHttpClientFactory clients) {
    public IMongoCollection<NlpQualityPolicy> Policies=>db.Database.GetCollection<NlpQualityPolicy>("aiNlpQualityPolicies");
    public IMongoCollection<NlpQualityApproval> Approvals=>db.Database.GetCollection<NlpQualityApproval>("aiNlpQualityApprovals");
    static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    static bool Hash(string? value)=>Regex.IsMatch(value??"","^[a-fA-F0-9]{64}$");
    public async Task<NlpFingerprint?> FingerprintAsync(string kind,string version,CancellationToken ct) {
        using var response=await clients.CreateClient("VietnameseInference").GetAsync("models/"+kind+"/fingerprint/"+Uri.EscapeDataString(version),ct);
        if(!response.IsSuccessStatusCode)return null;
        var result=await response.Content.ReadFromJsonAsync<NlpFingerprint>(cancellationToken:ct);
        return result is not null&&result.Kind==kind&&result.Version==version&&Hash(result.BundleSha256)&&Hash(result.WeightSha256)
            &&result.Files is {Count:>0}&&result.Files.All(f=>f is not null&&Hash(f.Sha256))?result:null;
    }
    public static bool MetricsPass(NlpQualityPolicy p,IReadOnlyDictionary<string,double>? metrics) {
        if(metrics is null||metrics.Any(x=>!double.IsFinite(x.Value)))return false;
        bool AtLeast(string key,double threshold)=>metrics.TryGetValue(key,out var value)&&value>=threshold&&value<=1;
        bool AtMost(string key,double threshold)=>metrics.TryGetValue(key,out var value)&&value>=0&&value<=threshold;
        return p.Kind=="vi" ? AtLeast("macroF1",p.MinimumMacroF1)&&AtLeast("coverage",p.MinimumCoverage)
                &&AtLeast("selectiveAccuracy",p.MinimumSelectiveAccuracy)
            : AtMost("valenceMae",p.MaximumMae)&&AtMost("arousalMae",p.MaximumMae)
                &&AtMost("valenceRmse",p.MaximumRmse)&&AtMost("arousalRmse",p.MaximumRmse);
    }
    static bool ManifestMatches(ExperimentReport report,NlpFingerprint fingerprint,string kind) {
        if(report.ArtifactFiles is null||report.ArtifactFiles.Count==0||report.ArtifactFiles.Count>500)return false;
        var files=new Dictionary<string,string>(StringComparer.Ordinal);
        foreach(var entry in report.ArtifactFiles) {
            if(entry is null||string.IsNullOrWhiteSpace(entry.Path)||entry.Path.Length>500||!Hash(entry.Sha256))return false;
            var key=entry.Path.Replace('\\','/');
            if(key.StartsWith('/')||key.Contains(':')||key.Split('/').Any(p=>p is ".." or "."))return false;
            if(kind=="vi"&&key.StartsWith("model/",StringComparison.Ordinal))key=key[6..];
            if(!files.TryAdd(key,entry.Sha256))return false;
        }
        return fingerprint.Files.All(f=>files.TryGetValue(f.Path,out var hash)&&hash.Equals(f.Sha256,StringComparison.OrdinalIgnoreCase));
    }
    public async Task<RegistryOperation> CreatePolicyAsync(NlpQualityPolicyInput input,int actor,CancellationToken ct) {
        var policy=JsonSerializer.Deserialize<NlpQualityPolicy>(JsonSerializer.Serialize(input,Json),Json)!;
        policy.Id=Guid.NewGuid().ToString("N");policy.ActorId=actor;policy.CreatedAt=DateTime.UtcNow;
        await Policies.InsertOneAsync(policy,cancellationToken:ct);
        await Audit("nlp_quality_policy_create",input.Kind,policy.Id,actor,ct);
        return new(201,policy);
    }
    public async Task<RegistryOperation> ApproveAsync(NlpQualityApprovalInput input,int actor,CancellationToken ct) {
        if(!input.IndependentReviewConfirmed)return new(400,new{message="Confirm independent label/split review and evaluation evidence."});
        var job=await db.Database.GetCollection<ExperimentJob>("aiTypedJobs").Find(j=>j.Id==input.JobId).FirstOrDefaultAsync(ct);
        if(job is null||job.TaskType=="recommender"||job.Report is null||string.IsNullOrEmpty(job.QualityPolicyId))
            return new(409,new{message="Choose a bilingual evaluation job with a submitted report and a policy fixed before execution."});
        var data=await db.Database.GetCollection<ExperimentDataset>("aiTypedDatasets").Find(d=>d.Id==job.DatasetId).FirstAsync(ct);
        var policy=await Policies.Find(p=>p.Id==job.QualityPolicyId).FirstOrDefaultAsync(ct);
        var kind=job.TaskType=="vsmec7"?"vi":"va";
        if(data.Purpose!="evaluation"||!data.IndependentEvaluation||policy is null||policy.Kind!=kind||policy.CreatedAt>job.CreatedAt
            ||job.Report.SubmittedAt<=job.CreatedAt||data.Splits.GetValueOrDefault("validation")<policy.MinimumValidationSamples
            ||data.Splits.GetValueOrDefault("test")<policy.MinimumTestSamples
            ||!MetricsPass(policy,job.Report.ValidationMetrics)||!MetricsPass(policy,job.Report.Metrics))
            return new(409,new{message="Independent evaluation dataset, sample counts, temporal policy lock or validation/test metrics failed the promotion gate."});
        NlpFingerprint? fingerprint;
        try{fingerprint=await FingerprintAsync(kind,job.ModelVersion,ct);}
        catch(Exception e)when(e is HttpRequestException or JsonException or TaskCanceledException){return new(503,new{message="Candidate fingerprint is unavailable; activation remains blocked."});}
        if(fingerprint is null||!job.Report.ArtifactHash.Equals(fingerprint.WeightSha256,StringComparison.OrdinalIgnoreCase)
            ||!ManifestMatches(job.Report,fingerprint,kind)||!job.Report.BundleSha256.Equals(fingerprint.BundleSha256,StringComparison.OrdinalIgnoreCase))
            return new(409,new{message="Stored weights/tokenizer/preprocessing do not match the evaluated artifact manifest."});
        var approval=new NlpQualityApproval{Id=Guid.NewGuid().ToString("N"),Kind=kind,Version=job.ModelVersion,
            BundleSha256=fingerprint.BundleSha256,WeightSha256=fingerprint.WeightSha256,Mode="reviewed_offline_report",
            JobId=job.Id,PolicyId=policy.Id,ReviewEvidence=input.ReviewEvidence,ActorId=actor,CreatedAt=DateTime.UtcNow};
        await Approvals.InsertOneAsync(approval,cancellationToken:ct);
        await Audit("nlp_quality_approve",kind,approval.Id,actor,ct);
        return new(201,approval);
    }
    public async Task RememberBaselineAsync(string kind,int actor,CancellationToken ct) {
        // Preserve only the artifact already loaded and unchanged on disk. This is not a quality claim.
        try {
            using var response=await clients.CreateClient("VietnameseInference").GetAsync("models/"+kind+"/lifecycle",ct);
            if(!response.IsSuccessStatusCode)return;
            var state=await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken:ct);
            if(!state.TryGetProperty("active_version",out var active)||active.ValueKind!=JsonValueKind.String)return;
            var version=active.GetString();if(string.IsNullOrEmpty(version))return;
            var fp=await FingerprintAsync(kind,version,ct);
            if(fp is null||!fp.Active||!fp.BundleSha256.Equals(fp.ActiveBundleSha256,StringComparison.OrdinalIgnoreCase))return;
            var id="baseline-"+kind+"-"+fp.BundleSha256;
            var record=new NlpQualityApproval{Id=id,Kind=kind,Version=version,BundleSha256=fp.BundleSha256,WeightSha256=fp.WeightSha256,
                Mode="existing_serving_baseline_not_quality_validated",ReviewEvidence="Previously loaded artifact retained for operational rollback.",ActorId=actor,CreatedAt=DateTime.UtcNow};
            await Approvals.UpdateOneAsync(x=>x.Id==id,Builders<NlpQualityApproval>.Update
                .SetOnInsert(x=>x.Kind,record.Kind).SetOnInsert(x=>x.Version,record.Version).SetOnInsert(x=>x.BundleSha256,record.BundleSha256)
                .SetOnInsert(x=>x.WeightSha256,record.WeightSha256).SetOnInsert(x=>x.Mode,record.Mode)
                .SetOnInsert(x=>x.ReviewEvidence,record.ReviewEvidence).SetOnInsert(x=>x.ActorId,actor).SetOnInsert(x=>x.CreatedAt,record.CreatedAt),
                new UpdateOptions{IsUpsert=true},ct);
        }catch(Exception e)when(e is HttpRequestException or JsonException or TaskCanceledException) { }
    }
    public async Task<RegistryOperation> RequireAsync(string kind,string version,CancellationToken ct) {
        try {
            var fp=await FingerprintAsync(kind,version,ct);
            if(fp is null)return new(503,new{message="Candidate identity cannot be verified. No activation was attempted."});
            var approval=await Approvals.Find(x=>x.Kind==kind&&x.Version==version&&x.BundleSha256==fp.BundleSha256).FirstOrDefaultAsync(ct);
            if(approval?.Mode=="reviewed_offline_report") {
                var job=await db.Database.GetCollection<ExperimentJob>("aiTypedJobs").Find(j=>j.Id==approval.JobId).FirstOrDefaultAsync(ct);
                if(job is null)return new(409,new{message="Evaluation provenance is missing."});
                using var config=JsonDocument.Parse(job.ConfigJson);
                var language=kind=="vi"?"vi":"en";
                var policy=await db.Database.GetCollection<BilingualPolicy>("aiBilingualPolicyState").Find(p=>p.Id==language).FirstOrDefaultAsync(ct);
                var policyVersion=policy?.Version??BilingualMoodAnalysisService.PolicyVersion(language);
                var moods=await db.TamTrangs.ToListAsync(ct);
                if(!config.RootElement.TryGetProperty("moodSetHash",out var hash)||hash.GetString()!=BilingualMoodAnalysisService.MoodSetHash(moods)
                    ||!config.RootElement.TryGetProperty("mappingVersion",out var mapping)||mapping.GetString()!=policyVersion)
                    return new(409,new{message="Mood geometry or policy changed since evaluation; review a fresh evaluation bundle."});
            }
            return approval is null?new(409,new{code="quality_approval_required",message="Candidate needs reviewed independent validation/test evidence matching its complete artifact before activation."}):new(200,fp);
        }catch(Exception e)when(e is HttpRequestException or JsonException or TaskCanceledException) {
            return new(503,new{message="Quality gate cannot verify model identity. No activation was attempted."});
        }
    }
    async Task Audit(string action,string kind,string detail,int actor,CancellationToken ct)=>
        await db.Database.GetCollection<BsonDocument>("aiAudit").InsertOneAsync(new(){["_id"]=Guid.NewGuid().ToString("N"),
            ["actorId"]=actor,["actorRole"]="AIEngineer",["action"]=action,["modelVersion"]=kind,
            ["outcome"]="succeeded",["createdAt"]=DateTime.UtcNow,["detail"]=detail},cancellationToken:ct);
}