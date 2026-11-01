using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using mood_recommendation.Data;
namespace mood_recommendation.Services;

public sealed record HybridWeights(double Mood=.55,double Content=.30,double Collaborative=.15);
public sealed record StrategyChoice(string? ExperimentId,string Group,string Arm,double ActionPropensity,
    double AssignmentProbability,HybridWeights Weights);
public sealed class RecommendationExperiment {
    [BsonId]public string Id{get;set;}="active";public string? Version{get;set;}public long Revision{get;set;}
    public bool Enabled{get;set;}public int TrafficPercent{get;set;}=10;public double Epsilon{get;set;}=.1;
    public string ReviewEvidence{get;set;}="";public int ActorId{get;set;}public DateTime UpdatedAt{get;set;}
}
public sealed class ExperimentInput {
    [Range(0,long.MaxValue)]public long ExpectedRevision{get;set;}public bool Enabled{get;set;}
    [Range(1,50)]public int TrafficPercent{get;set;}=10;[Range(.05,.5)]public double Epsilon{get;set;}=.1;
    [Required,StringLength(2000,MinimumLength=20)]public string ReviewEvidence{get;set;}="";
}
public sealed class RecommendationExposure {
    [BsonId]public string Id{get;set;}="";public int UserId{get;set;}public int MoodId{get;set;}public string ExperimentId{get;set;}="";
    public string Group{get;set;}="";public string Arm{get;set;}="";public double ActionPropensity{get;set;}
    public double AssignmentProbability{get;set;}public DateTime CreatedAt{get;set;}
}
/// <summary>Optional contextual epsilon-greedy bandit; never described as full multi-step RL.</summary>
public sealed class RecommendationStrategyService(MongoStore db)
{
    public static readonly IReadOnlyDictionary<string,HybridWeights> Arms=new Dictionary<string,HybridWeights>{
        ["baseline"]=new(),["content"]=new(.35,.50,.15),["mood"]=new(.70,.20,.10)};
    public IMongoCollection<RecommendationExperiment> States=>db.Database.GetCollection<RecommendationExperiment>("aiRecommendationExperiment");
    public IMongoCollection<RecommendationExposure> Exposures=>db.Database.GetCollection<RecommendationExposure>("recommendationExposures");
    public async Task<RecommendationExperiment> StateAsync(CancellationToken ct)=>await States.Find(x=>x.Id=="active").FirstOrDefaultAsync(ct)??new();
    public async Task<RegistryOperation> ConfigureAsync(ExperimentInput input,int actor,CancellationToken ct)
    {
        var current=await StateAsync(ct);
        if(current.Revision!=input.ExpectedRevision)return new(409,new{message="Experiment state changed."});
        var next=new RecommendationExperiment{Version=Guid.NewGuid().ToString("N"),Revision=current.Revision+1,Enabled=input.Enabled,
            TrafficPercent=input.TrafficPercent,Epsilon=input.Epsilon,ReviewEvidence=input.ReviewEvidence,ActorId=actor,UpdatedAt=DateTime.UtcNow};
        try{
            var result=await States.ReplaceOneAsync(x=>x.Id=="active"&&x.Revision==input.ExpectedRevision,next,
                new ReplaceOptions{IsUpsert=input.ExpectedRevision==0},ct);
            if(result.MatchedCount==0&&result.UpsertedId is null)return new(409,new{message="Concurrent experiment change."});
        }catch(MongoWriteException e)when(e.WriteError.Category==ServerErrorCategory.DuplicateKey){return new(409,new{message="Concurrent experiment change."});}
        await db.Database.GetCollection<BsonDocument>("aiAudit").InsertOneAsync(new(){
            ["_id"]=Guid.NewGuid().ToString("N"),["actorId"]=actor,["actorRole"]="AIEngineer",["action"]="recommendation_experiment_configure",
            ["modelVersion"]=next.Version!,["outcome"]="succeeded",["createdAt"]=DateTime.UtcNow,
            ["detail"]="enabled="+next.Enabled+"; "+next.ReviewEvidence},cancellationToken:ct);
        return new(200,next);
    }
    public static int Bucket(string experiment,int userId)=>(int)(System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(
        SHA256.HashData(Encoding.UTF8.GetBytes(experiment+":"+userId)))%100);

    public static double Reward(IEnumerable<int> stars,bool externalOpened) {
        var values=stars.ToArray();return values.Length>0?values.Average(s=>(s-3d)/2):externalOpened?.05:0;
    }
    public static (string Arm,double Propensity) Select(IReadOnlyDictionary<string,double> means,double epsilon,double draw,int randomIndex)
    {
        var names=Arms.Keys.Order(StringComparer.Ordinal).ToArray();
        var best=names.OrderByDescending(k=>means.GetValueOrDefault(k)).ThenBy(k=>k,StringComparer.Ordinal).First();
        var selected=draw<epsilon?names[Math.Clamp(randomIndex,0,names.Length-1)]:best;
        return(selected,epsilon/names.Length+(selected==best?1-epsilon:0));
    }
    public async Task<StrategyChoice> ChooseAsync(int userId,int moodId,CancellationToken ct)
    {
        var state=await StateAsync(ct);
        if(!state.Enabled||state.Version is null)return new(null,"baseline","baseline",1,1,new());
        var traffic=state.TrafficPercent/100d;
        if(Bucket(state.Version,userId)>=state.TrafficPercent)return new(state.Version,"control","baseline",1,1-traffic,new());
        var exposures=await Exposures.Find(x=>x.ExperimentId==state.Version&&x.MoodId==moodId&&x.Group=="bandit")
            .SortByDescending(x=>x.CreatedAt).Limit(2000).ToListAsync(ct);
        var rewards=await Rewards(exposures,ct);
        var means=Arms.Keys.ToDictionary(k=>k,k=>{
            var values=exposures.Where(x=>x.Arm==k&&rewards.ContainsKey(x.Id)).Select(x=>rewards[x.Id]).ToArray();
            return values.Length==0?0:values.Average();
        });
        var draw=RandomNumberGenerator.GetInt32(1_000_000)/1_000_000d;
        var selected=Select(means,state.Epsilon,draw,RandomNumberGenerator.GetInt32(Arms.Count));
        return new(state.Version,"bandit",selected.Arm,selected.Propensity,traffic,Arms[selected.Arm]);
    }
    public async Task LogAsync(string recommendationId,int userId,int moodId,StrategyChoice choice,CancellationToken ct)
    {
        if(choice.ExperimentId is null)return;
        var exposure=new RecommendationExposure{Id=recommendationId,UserId=userId,MoodId=moodId,ExperimentId=choice.ExperimentId,
            Group=choice.Group,Arm=choice.Arm,ActionPropensity=choice.ActionPropensity,AssignmentProbability=choice.AssignmentProbability,CreatedAt=DateTime.UtcNow};
        await Exposures.ReplaceOneAsync(x=>x.Id==exposure.Id,exposure,new ReplaceOptions{IsUpsert=true},ct);
    }
    async Task<Dictionary<string,double>> Rewards(List<RecommendationExposure> exposures,CancellationToken ct)
    {
        var ids=exposures.Select(x=>x.Id).ToArray();
        if(ids.Length==0)return [];
        var ratings=await db.DanhGias.Where(x=>ids.Contains(x.RecommendationId!)).OrderByDescending(x=>x.ThoiGian)
            .ThenByDescending(x=>x.DanhGiaID).Take(20000).ToListAsync(ct);
        var opens=await db.Database.GetCollection<BsonDocument>("interactions").Find(new BsonDocument{
            ["recommendationId"]=new BsonDocument("$in",new BsonArray(ids)),
            ["action"]=new BsonDocument("$in",new BsonArray{"external_open","trailer_open"})}).Limit(10000).ToListAsync(ct);
        var rewards=new Dictionary<string,double>();
        foreach(var exposure in exposures){
            var own=ratings.Where(x=>x.RecommendationId==exposure.Id&&x.TaiKhoanID==exposure.UserId)
                .GroupBy(x=>x.NoiDungID).Select(x=>x.First().SoSao).ToArray();
            var opened=opens.Any(x=>x.GetValue("recommendationId","").AsString==exposure.Id&&x["accountId"].ToInt32()==exposure.UserId);
            if(own.Length>0||opened)rewards[exposure.Id]=Reward(own,opened);
        }
        return rewards;
    }
    public async Task<object> ReportAsync(CancellationToken ct) {
        var state=await StateAsync(ct);
        var records=await Exposures.Find(_=>true).SortByDescending(x=>x.CreatedAt).Limit(10000).ToListAsync(ct);
        var rewards=await Rewards(records,ct);
        return new{state,scope="Context=mood ID; action=Hybrid weighting; reward=latest explicit feedback, otherwise bounded link-open signal. Contextual bandit, not full RL.",
            eventMeaning="Recommendation generated, not proof that an item was seen/listened to.",
            groups=records.GroupBy(x=>new{x.ExperimentId,x.Group,x.Arm}).Select(g=>new{g.Key.ExperimentId,g.Key.Group,g.Key.Arm,count=g.Count(),
                feedbackCount=g.Count(x=>rewards.ContainsKey(x.Id)),meanReward=g.Where(x=>rewards.ContainsKey(x.Id)).Select(x=>(double?)rewards[x.Id]).Average()})};
    }
}
