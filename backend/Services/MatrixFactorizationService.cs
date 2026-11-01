using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using mood_recommendation.Data;
namespace mood_recommendation.Services;

public sealed class MfUser { public int UserId {get;set;} public double Bias{get;set;} public double[] Factors{get;set;}=[]; }
public sealed class MfItem { public string ContentId{get;set;}=""; public double Bias{get;set;} public double[] Factors{get;set;}=[]; }
public sealed class MfRating { public int UserId{get;set;} public string ContentId{get;set;}="";public int Stars{get;set;}public DateTimeOffset Timestamp{get;set;} }
public sealed class MfModel {
    public int SchemaVersion{get;set;} public string Version{get;set;}="";public string DatasetHash{get;set;}="";
    public int Factors{get;set;}public double GlobalMean{get;set;}
    public DateTimeOffset TrainBefore{get;set;}public DateTimeOffset ValidationBefore{get;set;}
    public List<MfUser> Users{get;set;}=[];public List<MfItem> Items{get;set;}=[];
    public List<MfRating> ValidationRows{get;set;}=[];public List<MfRating> TestRows{get;set;}=[];
}
public sealed class MfStored {
    [BsonId]public string Id{get;set;}="";public string Json{get;set;}="";public string Hash{get;set;}="";
    public string Source{get;set;}="";public string DatasetHash{get;set;}="";public int ActorId{get;set;}
    public DateTime CreatedAt{get;set;}public int ValidationCount{get;set;}public int TestCount{get;set;}
    public double? ValidationRmse{get;set;}public double? TestRmse{get;set;}public bool Deployable{get;set;}
}
public sealed class MfState {
    [BsonId]public string Id{get;set;}="active";public string? Version{get;set;}public string? PreviousVersion{get;set;}
    public long Revision{get;set;}public DateTime UpdatedAt{get;set;}public int ActorId{get;set;}
}
public sealed record MfScores(string? Version,IReadOnlyDictionary<string,double> Scores);

public sealed class MatrixFactorizationService(MongoStore db,IMemoryCache cache)
{
    public IMongoCollection<MfStored> Models=>db.Database.GetCollection<MfStored>("aiRecommenderModels");
    public IMongoCollection<MfState> States=>db.Database.GetCollection<MfState>("aiRecommenderState");
    static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    public async Task<MfState> StateAsync(CancellationToken ct)=>await States.Find(x=>x.Id=="active").FirstOrDefaultAsync(ct)??new();
    public static double Estimate(MfModel m,MfUser u,MfItem i)=>Math.Clamp(m.GlobalMean+u.Bias+i.Bias+
        u.Factors.Zip(i.Factors).Sum(pair=>pair.First*pair.Second),-1,1);
    static (int Count,double? Rmse,double? Baseline) Evaluate(MfModel m,List<MfRating> rows) {
        var users=m.Users.ToDictionary(x=>x.UserId);var items=m.Items.ToDictionary(x=>x.ContentId);
        var known=rows.Where(x=>users.ContainsKey(x.UserId)&&items.ContainsKey(x.ContentId)).ToList();
        return known.Count==0?(0,null,null):(known.Count,Math.Sqrt(known.Average(x=>Math.Pow(Estimate(m,users[x.UserId],items[x.ContentId])-(x.Stars-3d)/2,2))),
            Math.Sqrt(known.Average(x=>Math.Pow(m.GlobalMean-(x.Stars-3d)/2,2))));
    }
    public async Task<RegistryOperation> RegisterAsync(JsonElement payload,string source,bool reviewed,int actor,CancellationToken ct)
    {
        if(!reviewed||source.Trim().Length<10)return new(400,new{message="Declare dataset source/label review and permission to use the supplied ratings."});
        if(payload.ValueKind!=JsonValueKind.Object)return new(400,new{message="MF artifact must be a JSON object."});
        var raw=payload.GetRawText();
        if(Encoding.UTF8.GetByteCount(raw)>8_000_000)return new(400,new{message="Artifact exceeds the 8 MB limit."});
        MfModel? m;
        try {m=JsonSerializer.Deserialize<MfModel>(raw,Json);}catch(JsonException){return new(400,new{message="Invalid MF artifact JSON."});}
        if(m is null||m.Users is null||m.Items is null||m.ValidationRows is null||m.TestRows is null
            ||m.Users.Any(x=>x is null)||m.Items.Any(x=>x is null)||m.ValidationRows.Any(x=>x is null)||m.TestRows.Any(x=>x is null)
            ||m.SchemaVersion!=1||!Regex.IsMatch(m.Version??"",@"^[A-Za-z0-9][A-Za-z0-9_-]{2,79}$")
            ||!Regex.IsMatch(m.DatasetHash??"",@"^[a-fA-F0-9]{64}$")||m.Factors is <1 or >64
            ||!double.IsFinite(m.GlobalMean)||Math.Abs(m.GlobalMean)>1||m.TrainBefore>=m.ValidationBefore
            ||m.ValidationBefore>DateTimeOffset.UtcNow||m.Users.Count==0||m.Items.Count==0
            ||m.Users.Select(x=>x.UserId).Distinct().Count()!=m.Users.Count||m.Items.Select(x=>x.ContentId).Distinct().Count()!=m.Items.Count
            ||m.Users.Any(u=>u.UserId<1||!Vector(u.Factors,m.Factors)||!double.IsFinite(u.Bias)||Math.Abs(u.Bias)>5)
            ||m.Items.Any(i=>string.IsNullOrWhiteSpace(i.ContentId)||i.ContentId.Length>100||!Vector(i.Factors,m.Factors)||!double.IsFinite(i.Bias)||Math.Abs(i.Bias)>5)
            ||m.ValidationRows.Any(r=>!Rating(r)||r.Timestamp<m.TrainBefore||r.Timestamp>=m.ValidationBefore)
            ||m.TestRows.Any(r=>!Rating(r)||r.Timestamp<m.ValidationBefore)
            ||m.ValidationRows.GroupBy(x=>(x.UserId,x.ContentId)).Any(x=>x.Count()>1)
            ||m.TestRows.GroupBy(x=>(x.UserId,x.ContentId)).Any(x=>x.Count()>1))
            return new(400,new{message="Invalid factors, IDs, scores or temporal evaluation splits."});
        var validation=Evaluate(m,m.ValidationRows);var test=Evaluate(m,m.TestRows);
        // Explicit, reproducible safety gate; this does not prove superior ranking relevance.
        var deployable=validation.Count>=20&&test.Count>=20&&validation.Rmse<=validation.Baseline&&test.Rmse<=test.Baseline;
        var stored=new MfStored{Id=m.Version!,Json=raw,Hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))),
            DatasetHash=m.DatasetHash!,Source=source.Trim(),ActorId=actor,CreatedAt=DateTime.UtcNow,
            ValidationCount=validation.Count,TestCount=test.Count,ValidationRmse=validation.Rmse,TestRmse=test.Rmse,Deployable=deployable};
        try{await Models.InsertOneAsync(stored,cancellationToken:ct);}
        catch(MongoWriteException e)when(e.WriteError.Category==ServerErrorCategory.DuplicateKey){return new(409,new{message="MF versions are immutable."});}
        return new(201,Summary(stored));
    }
    public static object Summary(MfStored m)=>new{version=m.Id,m.Hash,m.DatasetHash,m.Source,m.CreatedAt,m.ActorId,
        m.ValidationCount,m.TestCount,m.ValidationRmse,m.TestRmse,m.Deployable,
        gate="At least 20 warm validation and 20 warm test pairs; RMSE no worse than mean baseline. Ranking/Hybrid quality still requires review."};
    static bool Vector(double[]? v,int size)=>v is not null&&v.Length==size&&v.All(x=>double.IsFinite(x)&&Math.Abs(x)<=10);
    static bool Rating(MfRating r)=>r.UserId>0&&!string.IsNullOrWhiteSpace(r.ContentId)&&r.Stars is >=1 and <=5&&r.Timestamp<=DateTimeOffset.UtcNow;
    public async Task<RegistryOperation> SwitchAsync(string? version,long revision,int actor,CancellationToken ct)
    {
        var current=await StateAsync(ct);
        if(current.Revision!=revision)return new(409,new{message="Recommender state changed. Refresh."});
        if(version is not null){
            var stored=await Models.Find(x=>x.Id==version).FirstOrDefaultAsync(ct);
            if(stored is null)return new(404,new{message="MF candidate not found."});
            if(!stored.Deployable)return new(409,new{message="MF candidate has insufficient held-out evidence or failed its measured baseline gate."});
            var model=JsonSerializer.Deserialize<MfModel>(stored.Json,Json)!;
            var ids=(await db.NoiDungGiaiTris.Select(x=>x.NoiDungID).ToListAsync(ct)).ToHashSet();
            var userIds=(await db.TaiKhoans.Select(x=>x.TaiKhoanID).ToListAsync(ct)).ToHashSet();
            if(model.Users.Any(x=>!userIds.Contains(x.UserId)))return new(409,new{message="Map every latent user to a current account ID before activation."});
            if(model.Items.Any(x=>!ids.Contains(x.ContentId)))return new(409,new{message="Map every trained item to a current catalog ID before activation."});
        }
        var next=new MfState{Version=version,PreviousVersion=current.Version,Revision=revision+1,ActorId=actor,UpdatedAt=DateTime.UtcNow};
        try{
            var result=await States.ReplaceOneAsync(x=>x.Id=="active"&&x.Revision==revision,next,new ReplaceOptions{IsUpsert=revision==0},ct);
            if(result.MatchedCount==0&&result.UpsertedId is null)return new(409,new{message="Concurrent recommender change."});
        }catch(MongoWriteException e)when(e.WriteError.Category==ServerErrorCategory.DuplicateKey){return new(409,new{message="Concurrent recommender change."});}
        await db.Database.GetCollection<MongoDB.Bson.BsonDocument>("aiAudit").InsertOneAsync(new(){
            ["_id"]=Guid.NewGuid().ToString("N"),["actorId"]=actor,["actorRole"]="AIEngineer",["action"]=version is null?"mf_deactivate":"mf_activate",
            ["modelVersion"]=version??"neighbors",["outcome"]="succeeded",["createdAt"]=DateTime.UtcNow,["detail"]="revision "+next.Revision},cancellationToken:ct);
        return new(200,next);
    }
    public async Task<MfScores> ScoresAsync(int userId,IEnumerable<string> ids,CancellationToken ct)
    {
        var state=await StateAsync(ct);
        if(state.Version is null)return new(null,new Dictionary<string,double>());
        var key="mf:"+state.Version;
        if(!cache.TryGetValue<MfModel>(key,out var model)){
            var stored=await Models.Find(x=>x.Id==state.Version&&x.Deployable).FirstOrDefaultAsync(ct);
            if(stored is null)return new(null,new Dictionary<string,double>());
            model=JsonSerializer.Deserialize<MfModel>(stored.Json,Json);
            cache.Set(key,model,TimeSpan.FromMinutes(5));
        }
        var user=model!.Users.FirstOrDefault(x=>x.UserId==userId);
        if(user is null)return new(null,new Dictionary<string,double>()); // Honest cold-start fallback.
        var wanted=ids.ToHashSet();
        return new(state.Version,model.Items.Where(i=>wanted.Contains(i.ContentId)).ToDictionary(i=>i.ContentId,i=>(Estimate(model,user,i)+1)/2));
    }
}
