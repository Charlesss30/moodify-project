using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using mood_recommendation.Data;
using mood_recommendation.Services;
namespace mood_recommendation.Controllers;

public sealed class ExperimentRow {
    public string Id{get;set;}="";public string Split{get;set;}="";public bool Reviewed{get;set;}
    public string? Text{get;set;}public string? Emotion{get;set;}public double? Valence{get;set;}public double? Arousal{get;set;}
    public int? MoodId{get;set;}
    public int? UserId{get;set;}public string? ContentId{get;set;}public int? Stars{get;set;}public DateTimeOffset? Timestamp{get;set;}
}
public sealed class ExperimentDataset {
    [BsonId]public string Id{get;set;}="";public string Name{get;set;}="";public string TaskType{get;set;}="";
    public string Purpose{get;set;}="training";public bool IndependentEvaluation{get;set;}
    public string Language{get;set;}="";public string Source{get;set;}="";public string AnnotationMethod{get;set;}="";
    public string Hash{get;set;}="";public string RowsJson{get;set;}="";public int ActorId{get;set;}public DateTime CreatedAt{get;set;}
    public int Count{get;set;}public Dictionary<string,int> Splits{get;set;}=[];
}
public sealed class ExperimentDatasetInput {
    [Required,StringLength(100,MinimumLength=2)]public string Name{get;set;}="";
    [Required,RegularExpression("^(vsmec7|va_regression|recommender)$")]public string TaskType{get;set;}="";
    [Required,RegularExpression("^(vi|en|none)$")]public string Language{get;set;}="";
    [RegularExpression("^(training|evaluation)$")]public string Purpose{get;set;}="training";
    public bool IndependentEvaluation{get;set;}
    [Required,StringLength(1000,MinimumLength=10)]public string Source{get;set;}="";
    [Required,StringLength(2000,MinimumLength=10)]public string AnnotationMethod{get;set;}="";
    [Required,MinLength(3),MaxLength(60000)]public List<ExperimentRow> Rows{get;set;}=[];
}
public sealed class ExperimentJob {
    [BsonId]public string Id{get;set;}="";public string DatasetId{get;set;}="";public string DatasetHash{get;set;}="";
    public string ModelVersion{get;set;}="";public string TaskType{get;set;}="";public string ConfigJson{get;set;}="";
    public string ConfigHash{get;set;}="";public int ActorId{get;set;}public DateTime CreatedAt{get;set;}public string Status{get;set;}="prepared";
    public string? QualityPolicyId{get;set;}
    public ExperimentReport? Report{get;set;}
}
public sealed class ExperimentJobInput {
    [Required]public string DatasetId{get;set;}="";
    public string? QualityPolicyId{get;set;}
    [Required,RegularExpression("^[A-Za-z0-9][A-Za-z0-9_-]{2,79}$")]public string ModelVersion{get;set;}="";
    [RegularExpression("^[A-Za-z0-9][A-Za-z0-9_-]{2,79}$")]public string? BaseVersion{get;set;}
    [Range(1,1000)]public int Epochs{get;set;}=3;[Range(1,8)]public int BatchSize{get;set;}=2;
    [Range(1,64)]public int GradientAccumulation{get;set;}=16;[Range(1,64)]public int Factors{get;set;}=8;
    [Range(0,int.MaxValue)]public int Seed{get;set;}=42;
}
public sealed class ExperimentReport {
    [Required]public string JobId{get;set;}="";[Required]public string DatasetHash{get;set;}="";[Required]public string ConfigHash{get;set;}="";
    [Required]public string ModelVersion{get;set;}="";[Required,RegularExpression("^[a-fA-F0-9]{64}$")]public string ArtifactHash{get;set;}="";
    [Range(1,60000)]public int TestSamples{get;set;}public Dictionary<string,double> Metrics{get;set;}=[];
    public Dictionary<string,double> ValidationMetrics{get;set;}=[];
    public List<NlpFingerprintFile> ArtifactFiles{get;set;}=[];
    public string BundleSha256{get;set;}="";
    public DateTime SubmittedAt{get;set;}
}
[ApiController,Route("api/AiExperiments"),Authorize(Roles="AIEngineer")]
public sealed class AiExperimentsController(MongoStore db,BilingualPolicyService policies):ControllerBase
{
    static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    static readonly HashSet<string> Labels=new(["Anger","Disgust","Enjoyment","Fear","Other","Sadness","Surprise"]);
    int Actor=>int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    IMongoCollection<ExperimentDataset> Datasets=>db.Database.GetCollection<ExperimentDataset>("aiTypedDatasets");
    IMongoCollection<ExperimentJob> Jobs=>db.Database.GetCollection<ExperimentJob>("aiTypedJobs");
    static string Hash(string text)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    [HttpGet]public async Task<IActionResult> Overview(CancellationToken ct)=>Ok(new{
        datasets=await Datasets.Find(_=>true).SortByDescending(x=>x.CreatedAt).Limit(100).Project(x=>new{x.Id,x.Name,x.TaskType,x.Language,x.Purpose,x.IndependentEvaluation,x.Source,x.AnnotationMethod,x.Hash,x.Count,x.Splits,x.CreatedAt}).ToListAsync(ct),
        jobs=await Jobs.Find(_=>true).SortByDescending(x=>x.CreatedAt).Limit(100).ToListAsync(ct),
        note="Reviewed immutable datasets; training runs explicitly using the downloaded bundle. Submitted reports are not deployment approval."});
    [HttpPost("datasets"),RequestSizeLimit(16_000_000)]
    public async Task<IActionResult> Dataset(ExperimentDatasetInput input,CancellationToken ct)
    {
        if((input.TaskType=="vsmec7"&&input.Language!="vi")||(input.TaskType=="va_regression"&&input.Language!="en")||(input.TaskType=="recommender"&&input.Language!="none"))
            return BadRequest(new{message="Task and language mismatch."});
        if(input.Purpose=="evaluation"&&input.TaskType=="recommender")return BadRequest(new{message="Use the temporal MF evaluation workflow."});
        var moodIds=(await db.TamTrangs.Select(x=>x.TamTrangID).ToListAsync(ct)).ToHashSet();
        var ids=new HashSet<string>();var texts=new HashSet<string>();
        foreach(var row in input.Rows){
            if(row is null||string.IsNullOrWhiteSpace(row.Id)||!ids.Add(row.Id)||!row.Reviewed||row.Split is not("train" or "validation" or "test"))
                return BadRequest(new{message="Every reviewed row needs a unique ID and an explicit split."});
            if(input.TaskType!="recommender"){
                var text=(row.Text??"").Normalize(NormalizationForm.FormKC).Trim();
                var key=string.Join(' ',text.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
                if(text.Length is <1 or >4000||!texts.Add(key)||row.UserId is not null||row.Stars is not null||row.ContentId is not null||row.Timestamp is not null)
                    return BadRequest(new{message="Invalid text, private recommender fields, or duplicate/cross-split leakage."});
                if(input.TaskType=="vsmec7"&&(!Labels.Contains(row.Emotion??"")||(input.Purpose=="training"&&(row.Valence is not null||row.Arousal is not null||row.MoodId is not null))))
                    return BadRequest(new{message="VSMEC requires one of seven emotion labels, not fabricated VA labels."});
                if(input.Purpose=="evaluation"&&((row.MoodId is not null&&!moodIds.Contains(row.MoodId.Value))
                    ||((row.Valence is not null||row.Arousal is not null)&&(!Coordinate(row.Valence)||!Coordinate(row.Arousal)))))
                    return BadRequest(new{message="Evaluation VA and optional mood IDs must be valid independently reviewed labels."});
                if(input.TaskType=="va_regression"&&(!Coordinate(row.Valence)||!Coordinate(row.Arousal)||row.Emotion is not null))
                    return BadRequest(new{message="VA regression requires two finite coordinates in [-1,1], not emotion classes."});
            }else if(row.UserId is not >0||string.IsNullOrWhiteSpace(row.ContentId)||row.ContentId.Length>100||row.Stars is not(>=1 and <=5)
                ||row.Timestamp is null||row.Timestamp>DateTimeOffset.UtcNow||row.Text is not null||row.Emotion is not null||row.Valence is not null||row.Arousal is not null)
                return BadRequest(new{message="MF requires stable IDs, explicit stars and timestamps; it does not train on comments/emotion labels."});
        }
        if(input.Purpose=="training" ? input.Rows.Select(x=>x.Split).Distinct().Count()!=3
            : input.Rows.Any(x=>x.Split=="train")||!input.Rows.Any(x=>x.Split=="validation")||!input.Rows.Any(x=>x.Split=="test"))
            return BadRequest(new{message="Training needs three splits; independent evaluation needs validation/test only."});
        if(input.TaskType=="recommender"){
            if(input.Rows.GroupBy(x=>(x.Split,x.UserId,x.ContentId)).Any(x=>x.Count()>1))
                return BadRequest(new{message="Use the latest user/item rating within each temporal split."});
            var train=input.Rows.Where(x=>x.Split=="train").ToList();var validation=input.Rows.Where(x=>x.Split=="validation").ToList();var test=input.Rows.Where(x=>x.Split=="test").ToList();
            if(train.Max(x=>x.Timestamp)>=validation.Min(x=>x.Timestamp)||validation.Max(x=>x.Timestamp)>=test.Min(x=>x.Timestamp))
                return BadRequest(new{message="MF splits must follow global temporal order; future feedback cannot train past predictions."});
        }
        var rows=JsonSerializer.Serialize(input.Rows,Json);
        if(Encoding.UTF8.GetByteCount(rows)>12_000_000)return BadRequest(new{message="Dataset exceeds storage bound."});
        var d=new ExperimentDataset{Id=Guid.NewGuid().ToString("N"),Name=input.Name,TaskType=input.TaskType,Language=input.Language,
            Source=input.Source,AnnotationMethod=input.AnnotationMethod,Purpose=input.Purpose,IndependentEvaluation=input.IndependentEvaluation,Hash=Hash(rows),RowsJson=rows,ActorId=Actor,CreatedAt=DateTime.UtcNow,
            Count=input.Rows.Count,Splits=input.Rows.GroupBy(x=>x.Split).ToDictionary(x=>x.Key,x=>x.Count())};
        await Datasets.InsertOneAsync(d,cancellationToken:ct);
        return StatusCode(201,new{d.Id,d.Hash,d.Count,d.TaskType,d.Splits});
    }
    static bool Coordinate(double? value)=>value.HasValue&&double.IsFinite(value.Value)&&value is >=-1 and <=1;
    [HttpPost("jobs")]public async Task<IActionResult> Job(ExperimentJobInput input,CancellationToken ct)
    {
        var d=await Datasets.Find(x=>x.Id==input.DatasetId).FirstOrDefaultAsync(ct);
        if(d is null)return NotFound();
        if(d.TaskType!="recommender"&&(string.IsNullOrEmpty(input.BaseVersion)||input.Epochs>10))
            return BadRequest(new{message="NLP jobs require a local base version and at most 10 epochs."});
        NlpQualityPolicy? qualityPolicy=null;
        if(!string.IsNullOrEmpty(input.QualityPolicyId)){
            qualityPolicy=await db.Database.GetCollection<NlpQualityPolicy>("aiNlpQualityPolicies").Find(p=>p.Id==input.QualityPolicyId).FirstOrDefaultAsync(ct);
            if(qualityPolicy is null||d.Purpose!="evaluation"||qualityPolicy.Kind!=(d.TaskType=="vsmec7"?"vi":"va"))
                return BadRequest(new{message="Choose a compatible quality policy for an evaluation-only dataset."});
        }
        var moods=await db.TamTrangs.ToListAsync(ct);
        var activePolicy=d.TaskType=="recommender"?null:await policies.ActiveAsync(d.Language,ct);
        var centers=moods.Where(m=>activePolicy?.MoodIds is null||activePolicy.MoodIds.Contains(m.TamTrangID))
            .Select(m=>new{id=m.TamTrangID,valence=(m.MinValence+m.MaxValence)/2m,arousal=(m.MinArousal+m.MaxArousal)/2m});
        var cfg=JsonSerializer.Serialize(new{d.TaskType,d.Language,d.Purpose,input.QualityPolicyId,input.BaseVersion,input.Epochs,input.BatchSize,
            input.GradientAccumulation,input.Factors,input.Seed,maxLength=128,moodCenters=centers,
            moodSetHash=BilingualMoodAnalysisService.MoodSetHash(moods),mappingVersion=activePolicy?.Version,
            minimumConfidence=activePolicy?.MinimumConfidence??0},Json);
        var job=new ExperimentJob{Id=Guid.NewGuid().ToString("N"),DatasetId=d.Id,DatasetHash=d.Hash,ModelVersion=input.ModelVersion,TaskType=d.TaskType,QualityPolicyId=input.QualityPolicyId,
            ConfigJson=cfg,ConfigHash=Hash(cfg),ActorId=Actor,CreatedAt=DateTime.UtcNow};
        await Jobs.InsertOneAsync(job,cancellationToken:ct);return StatusCode(201,job);
    }
    [HttpGet("jobs/{id}/bundle")]public async Task<IActionResult> Bundle(string id,CancellationToken ct)
    {
        var j=await Jobs.Find(x=>x.Id==id).FirstOrDefaultAsync(ct);if(j is null)return NotFound();
        var d=await Datasets.Find(x=>x.Id==j.DatasetId).FirstAsync(ct);
        return Ok(new{jobId=j.Id,j.ModelVersion,j.DatasetHash,j.ConfigHash,j.ConfigJson,datasetJson=d.RowsJson,
            annotationMethod=d.AnnotationMethod,source=d.Source,runner="python train_experiment.py --bundle bundle.json --base-dir <local-base-artifact> --out <new-output-directory>",
            note="Explicit local/Colab execution; downloading a bundle does not start training or replace serving."});
    }
    [HttpPost("jobs/{id}/report"),RequestSizeLimit(2_000_000)]public async Task<IActionResult> Report(string id,ExperimentReport input,CancellationToken ct)
    {
        var j=await Jobs.Find(x=>x.Id==id).FirstOrDefaultAsync(ct);if(j is null)return NotFound();
        var d=await Datasets.Find(x=>x.Id==j.DatasetId).FirstAsync(ct);
        if(j.Report is not null)return Conflict(new{message="Reports are immutable; create another job/version."});
        if(input.JobId!=j.Id||input.DatasetHash!=j.DatasetHash||input.ConfigHash!=j.ConfigHash||input.ModelVersion!=j.ModelVersion||input.TestSamples!=d.Splits["test"])
            return BadRequest(new{message="Report provenance or test sample count does not match the immutable job."});
        if(input.ArtifactFiles is null||input.ArtifactFiles.Count>500||input.ValidationMetrics is null||input.ValidationMetrics.Count>30
            ||input.Metrics is null||input.Metrics.Count>30||input.ValidationMetrics.Any(m=>!double.IsFinite(m.Value)))
            return BadRequest(new{message="Evaluation metadata exceeds bounds or contains invalid numbers."});
        var required=j.TaskType=="vsmec7"?new[]{"macroF1","coverage"}:j.TaskType=="va_regression"?new[]{"valenceMae","valenceRmse","arousalMae","arousalRmse"}:new[]{"rmse","ndcg"};
        if(input.Metrics is null||required.Any(k=>!input.Metrics.ContainsKey(k))||input.Metrics.Any(x=>!double.IsFinite(x.Value)||x.Value<0
            ||x.Value>(x.Key.Contains("Mae")||x.Key.Contains("Rmse")||x.Key=="rmse"?2:1)))
            return BadRequest(new{message="Metrics must match the model task and supported numeric ranges."});
        input.SubmittedAt=DateTime.UtcNow;
        var result=await Jobs.UpdateOneAsync(x=>x.Id==id&&x.Report==null,Builders<ExperimentJob>.Update.Set(x=>x.Report,input).Set(x=>x.Status,"report_submitted_not_deployed"),cancellationToken:ct);
        return result.ModifiedCount==1?Ok(new{status="report_submitted_not_deployed"}):Conflict();
    }
}
