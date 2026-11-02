using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using mood_recommendation.Data;

namespace mood_recommendation.Services;

public sealed class BilingualPolicy
{
    [BsonId] public string Id { get; set; } = "";
    public string Language { get; set; } = "";
    public long Revision { get; set; }
    public string Version { get; set; } = "";
    public string? PreviousVersion { get; set; }
    public List<int>? MoodIds { get; set; }
    public double MinimumConfidence { get; set; }
    public string Method { get; set; } = SemanticMoodMapper.Version;
    public string Reason { get; set; } = "";
    public int ActorId { get; set; }
    public DateTime CreatedAt { get; set; }
}
public sealed class BilingualPolicyInput
{
    [Required,RegularExpression("^(vi|en)$")] public string Language { get; set; } = "";
    [Required,MinLength(1),MaxLength(100)] public List<int> MoodIds { get; set; } = [];
    [Range(0,1)] public double MinimumConfidence { get; set; }
    [Required,StringLength(1000,MinimumLength=10)] public string Reason { get; set; } = "";
}
public sealed class RevisionInput { [Range(0,long.MaxValue)] public long ExpectedRevision { get; set; } }

/// <summary>Policies select existing mood IDs and may tighten classification acceptance; anchors never change.</summary>
public sealed class BilingualPolicyService(MongoStore db)
{
    public IMongoCollection<BilingualPolicy> Policies => db.Database.GetCollection<BilingualPolicy>("aiBilingualPolicies");
    public IMongoCollection<BilingualPolicy> States => db.Database.GetCollection<BilingualPolicy>("aiBilingualPolicyState");
    public async Task<BilingualPolicy> ActiveAsync(string language,CancellationToken ct) =>
        await States.Find(x=>x.Id==language).FirstOrDefaultAsync(ct) ?? new BilingualPolicy{
            Id=language,Language=language,Version=BilingualMoodAnalysisService.PolicyVersion(language),Reason="Built-in baseline; all current moods."};
    public async Task<RegistryOperation> DraftAsync(BilingualPolicyInput input,int actor,CancellationToken ct)
    {
        if(input.MoodIds.Count!=input.MoodIds.Distinct().Count() || input.MoodIds.Any(x=>x<1))
            return new(400,new{message="Use unique stable mood IDs. Confidence limits apply to classifiers; regression has no classification confidence."});
        var ids=(await db.TamTrangs.ToListAsync(ct)).Select(x=>x.TamTrangID).ToHashSet();
        if(input.MoodIds.Any(x=>!ids.Contains(x)))return new(400,new{message="Every policy mood must exist."});
        var draft=new BilingualPolicy{Id="va-policy-"+Guid.NewGuid().ToString("N"),Language=input.Language,
            MoodIds=input.MoodIds.Order().ToList(),MinimumConfidence=input.MinimumConfidence,Reason=input.Reason.Trim(),ActorId=actor,CreatedAt=DateTime.UtcNow};
        draft.Version=draft.Id;
        await Policies.InsertOneAsync(draft,cancellationToken:ct);
        return new(201,draft);
    }
    public async Task<RegistryOperation> ActivateAsync(string version,long revision,int actor,CancellationToken ct)
    {
        var policy=await Policies.Find(x=>x.Id==version).FirstOrDefaultAsync(ct);
        if(policy is null)return new(404,new{message="Policy candidate not found."});
        var ids=(await db.TamTrangs.ToListAsync(ct)).Select(x=>x.TamTrangID).ToHashSet();
        if(policy.MoodIds is null || policy.MoodIds.Count==0 || policy.MoodIds.Any(x=>!ids.Contains(x)))
            return new(409,new{message="Policy references a missing mood; create a reviewed replacement."});
        var current=await ActiveAsync(policy.Language,ct);
        if(current.Revision!=revision)return new(409,new{message="Policy changed. Refresh before approval."});
        var next=new BilingualPolicy{Id=policy.Language,Language=policy.Language,Version=policy.Version,
            Revision=revision+1,PreviousVersion=current.Version,MoodIds=policy.MoodIds.ToList(),MinimumConfidence=policy.MinimumConfidence,
            Method=policy.Method,Reason=policy.Reason,ActorId=actor,CreatedAt=DateTime.UtcNow};
        try
        {
            var result=await States.ReplaceOneAsync(x=>x.Id==policy.Language&&x.Revision==revision,next,new ReplaceOptions{IsUpsert=revision==0},ct);
            if(result.MatchedCount==0&&result.UpsertedId is null)return new(409,new{message="Policy changed."});
        }
        catch(MongoWriteException e) when(e.WriteError.Category==ServerErrorCategory.DuplicateKey)
        {return new(409,new{message="Another approval won. Refresh."});}
        await Audit("policy_activate",current,next,actor,ct);
        return new(200,next);
    }
    public async Task<RegistryOperation> ResetAsync(string language,long revision,int actor,CancellationToken ct)
    {
        var current=await ActiveAsync(language,ct);
        if(current.Revision!=revision)return new(409,new{message="Policy changed."});
        if(revision==0)return new(200,current);
        // A reset also advances the version: outstanding receipts never become valid again by rollback.
        var next=new BilingualPolicy{Id=language,Language=language,Revision=revision+1,
            Version="va-baseline-"+Guid.NewGuid().ToString("N"),PreviousVersion=current.Version,
            Reason="Restore built-in all-mood context mapping baseline.",ActorId=actor,CreatedAt=DateTime.UtcNow};
        var result=await States.ReplaceOneAsync(x=>x.Id==language&&x.Revision==revision,next,cancellationToken:ct);
        if(result.ModifiedCount!=1)return new(409,new{message="Policy changed."});
        await Audit("policy_reset",current,next,actor,ct);return new(200,next);
    }
    async Task Audit(string action,BilingualPolicy before,BilingualPolicy after,int actor,CancellationToken ct) =>
        await db.Database.GetCollection<BsonDocument>("aiAudit").InsertOneAsync(new BsonDocument{
            ["_id"]=Guid.NewGuid().ToString("N"),["actorId"]=actor,["actorRole"]="AIEngineer",["action"]=action,
            ["modelVersion"]=after.Language,["outcome"]="succeeded",["createdAt"]=DateTime.UtcNow,
            ["detail"]=before.Version+" -> "+after.Version+"; reason: "+after.Reason},cancellationToken:ct);

    public async Task ObserveAsync(BilingualAnalysisResponse response,double milliseconds,CancellationToken ct)
    {
        // Aggregate by day/version/status. No descriptions, identities or per-user telemetry.
        var day=DateTime.UtcNow.ToString("yyyy-MM-dd");
        var material=JsonSerializer.Serialize(new{day,response.Language,response.ModelVersion,response.MappingVersion,response.Status,response.ReasonCode});
        var id=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
        await db.Database.GetCollection<BsonDocument>("aiInferenceMetrics").UpdateOneAsync(new BsonDocument("_id",id),
            new BsonDocument{
                ["$setOnInsert"]=new BsonDocument{["day"]=day,["language"]=response.Language??"unknown",
                    ["modelVersion"]=response.ModelVersion??"unknown",["mappingVersion"]=response.MappingVersion??"unknown",
                    ["status"]=response.Status,["reason"]=response.ReasonCode??"none"},
                ["$inc"]=new BsonDocument{["count"]=1,["accepted"]=response.Accepted?1:0,["totalMs"]=milliseconds},
                ["$max"]=new BsonDocument("maxMs",milliseconds)},new UpdateOptions{IsUpsert=true},ct);
    }
}
