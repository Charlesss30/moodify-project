using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using mood_recommendation.Controllers;
using mood_recommendation.Data;
namespace mood_recommendation.Services;

// Single local worker per database; file lock prevents duplicate backend workers.
public sealed class LocalTrainingWorker(MongoStore db, IConfiguration config, IWebHostEnvironment env, ILogger<LocalTrainingWorker> logger) : BackgroundService
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    IMongoCollection<AiTrainingJob> Jobs => db.Database.GetCollection<AiTrainingJob>("aiTrainingJobs");
    readonly SemaphoreSlim gate = new(1,1);
    Process? process;
    volatile bool ready;
    string Root => Path.GetFullPath(Path.Combine(env.ContentRootPath,"../.local-training",db.Database.DatabaseNamespace.DatabaseName));
    string Python => Path.GetFullPath(config["Training:Python"] ?? Path.Combine(env.ContentRootPath,"../ai-service/.venv/Scripts/python.exe"));
    string Model => Path.GetFullPath(config["Training:BaseModelDirectory"] ?? Path.Combine(env.ContentRootPath,"../code-train/train-KLCN/output/roberta_large_asl_en_01/best_model"));
    string Script => Path.GetFullPath(Path.Combine(env.ContentRootPath,"../ai-service/train_worker.py"));
    bool Enabled => config.GetValue("Training:Enabled",true);
    string Device => config["Training:Device"] ?? "cpu";
    public object Availability() => new { enabled=Enabled, ready, device=Device, configured=File.Exists(Python)&&File.Exists(Script)&&File.Exists(Path.Combine(Model,"model.safetensors")),
        strategy="continued_finetuning_asl", note="Local fine-tuning from the installed model. CPU by default; artifacts stay separate until reviewed and deployed." };
    public async Task<RegistryOperation> Start(string id,int revision,int actor,CancellationToken ct)
    {
        if (!Enabled||!ready) return new(503,new {message="Local training worker is unavailable."});
        if (!File.Exists(Python)||!File.Exists(Script)||!File.Exists(Path.Combine(Model,"model.safetensors"))) return new(503,new {message="Configure the Python environment and base model before starting."});
        if (Device is not ("cpu" or "cuda")) return new(503,new {message="Training device must be cpu or cuda."});
        // Avoid competing for the serving GPU unless operator explicitly allows sharing.
        if(Device=="cuda" && config.GetValue("AI:Enabled",false) && !config.GetValue("Training:AllowSharedGpu",false)) return new(409,new {message="Inference uses this host. Use CPU or a dedicated training host/GPU; shared GPU is disabled."});
        await gate.WaitAsync(ct);
        try {
            if(await Jobs.Find(x=>x.Execution=="local"&&(x.Status=="queued"||x.Status=="running"||x.Status=="cancelling")).AnyAsync(ct)) return new(409,new {message="A local job is already queued or running."});
            var j=await Jobs.Find(x=>x.Id==id).FirstOrDefaultAsync(ct);
            if(j is null)return new(404,new {message="Job not found."});
            if(j.Revision!=revision||j.Status!="prepared")return new(409,new {message="Only an unchanged prepared job can start."});
            j.Execution="local";j.Status="queued";j.Revision++;j.Events.Add(new(actor,"queued",DateTime.UtcNow));
            var result=await Jobs.ReplaceOneAsync(x=>x.Id==id&&x.Revision==revision,j,cancellationToken:ct);
            return result.ModifiedCount==1?new(200,j):new(409,new {message="Job changed."});
        } finally {gate.Release();}
    }
    public async Task<RegistryOperation> Cancel(string id,int revision,int actor,CancellationToken ct)
    {
        var j=await Jobs.Find(x=>x.Id==id).FirstOrDefaultAsync(ct);
        if(j is null)return new(404,new {message="Job not found."});
        if(j.Execution!="local"||j.Revision!=revision||j.Status is not ("queued" or "running"))return new(409,new {message="Local job changed or is not cancellable."});
        var status=j.Status=="queued"?"cancelled":"cancelling";
        var result=await Jobs.UpdateOneAsync(x=>x.Id==id&&x.Revision==revision,
            Builders<AiTrainingJob>.Update.Set(x=>x.Status,status).Inc(x=>x.Revision,1).Push(x=>x.Events,new(actor,status,DateTime.UtcNow)),cancellationToken:ct);
        return result.ModifiedCount==1?new(200,new {status}):new(409,new {message="Job changed."});
    }
    public async Task<object?> Progress(string id,CancellationToken ct)
    {
        var job=await Jobs.Find(x=>x.Id==id).FirstOrDefaultAsync(ct);
        if(job is null)return null;
        var dir=Path.Combine(Root,job.Id);
        JsonElement? progress=null;
        try {if(File.Exists(Path.Combine(dir,"progress.json")))progress=JsonSerializer.Deserialize<JsonElement>(await File.ReadAllTextAsync(Path.Combine(dir,"progress.json"),ct));}catch(IOException){}catch(JsonException){}
        var log=Array.Empty<string>();
        try {if(File.Exists(Path.Combine(dir,"worker.log")))log=File.ReadLines(Path.Combine(dir,"worker.log")).TakeLast(100).ToArray();}catch(IOException){}
        return new {job,progress,log,artifactAvailable=job.Status=="completed"&&job.ArtifactVerified};
    }
    public async Task<object?> Artifact(string id, CancellationToken ct) {
        var job=await Jobs.Find(x=>x.Id==id&&x.Status=="completed"&&x.ArtifactVerified).FirstOrDefaultAsync(ct);
        if(job is null)return null;
        var dir=Path.Combine(Root,job.Id,"artifact");
        return new { job.Id,job.ModelVersion,job.DatasetHash,job.ConfigHash,job.Report,
            state="verified_local_candidate_not_deployed", directory=dir,
            files=Directory.GetFiles(dir).Select(p=>new {name=Path.GetFileName(p),bytes=new FileInfo(p).Length}),
            note="Candidate registered in aiTrainingArtifacts. Review calibration/quality, configure serving allowlist/hash and validate before activation. Serving model is unchanged." };
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if(!Enabled)return;
        Directory.CreateDirectory(Root);
        FileStream ownership;
        try {ownership=new FileStream(Path.Combine(Root,"worker.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}
        catch(IOException){logger.LogWarning("Another local training worker owns this database.");return;}
        using(ownership) {
            // A previous host may have ended without a final update. No automatic rerun.
            await Jobs.UpdateManyAsync(x=>x.Execution=="local"&&(x.Status=="running"||x.Status=="cancelling"),
                Builders<AiTrainingJob>.Update.Set(x=>x.Status,"failed").Set(x=>x.Failure,"Worker restarted; review output before preparing a new job.").Inc(x=>x.Revision,1),cancellationToken:stoppingToken);
            ready=true;
            try {
                while(!stoppingToken.IsCancellationRequested){
                    try {
                    var j=await Jobs.Find(x=>x.Execution=="local"&&x.Status=="queued").SortBy(x=>x.CreatedAt).FirstOrDefaultAsync(stoppingToken);
                    if(j is null){await Task.Delay(1000,stoppingToken);continue;}
                    var claimed=await Jobs.UpdateOneAsync(x=>x.Id==j.Id&&x.Status=="queued"&&x.Revision==j.Revision,
                        Builders<AiTrainingJob>.Update.Set(x=>x.Status,"running").Inc(x=>x.Revision,1).Push(x=>x.Events,new(j.ActorId,"running",DateTime.UtcNow)),cancellationToken:stoppingToken);
                    if(claimed.ModifiedCount==1)await Run(j,stoppingToken);
                    } catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested) { throw; }
                    catch(Exception e) { logger.LogWarning(e,"Local training worker will retry after a storage/process error."); await Task.Delay(3000,stoppingToken); }
                }
            }catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested){}
            finally{ready=false;Kill();}
        }
    }
    void Kill(){try{if(process is {HasExited:false})process.Kill(entireProcessTree:true);}catch(InvalidOperationException){} }
    async Task Run(AiTrainingJob job,CancellationToken ct)
    {
        var dir=Path.Combine(Root,job.Id);
        try{
            if(Directory.Exists(dir))throw new InvalidOperationException("Output already exists; prepare a new job instead of overwriting artifacts.");
            Directory.CreateDirectory(dir);
            var dataset=await db.Database.GetCollection<AiDataset>("aiDatasets").Find(x=>x.Id==job.DatasetId).FirstOrDefaultAsync(ct)??throw new InvalidOperationException("Dataset unavailable.");
            var labels=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(env.ContentRootPath,"Data/emotion-mood-mapping.json"),ct)).RootElement.GetProperty("labels").EnumerateArray().Select(x=>x.GetProperty("label").GetString()).ToArray();
            await File.WriteAllTextAsync(Path.Combine(dir,"bundle.json"),JsonSerializer.Serialize(new {jobId=job.Id,job.ModelVersion,job.DatasetHash,job.ConfigHash,job.ConfigJson,datasetJson=JsonSerializer.Serialize(dataset.Rows,Json),labels},Json),ct);
            using(var baseWeights=File.OpenRead(Path.Combine(Model,"model.safetensors"))) {
                job.BaseArtifactHash=Convert.ToHexString(await SHA256.HashDataAsync(baseWeights,ct)).ToLowerInvariant();
            }
            job.WorkerHash=Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(Script,ct))).ToLowerInvariant();
            await Jobs.UpdateOneAsync(x=>x.Id==job.Id,Builders<AiTrainingJob>.Update.Set(x=>x.BaseArtifactHash,job.BaseArtifactHash).Set(x=>x.WorkerHash,job.WorkerHash),cancellationToken:ct);
            var start=new ProcessStartInfo(Python){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=dir};
            foreach(var arg in new[]{Script,"--job-dir",dir,"--model-dir",Model,"--device",Device,"--parent-pid",Environment.ProcessId.ToString()})start.ArgumentList.Add(arg);
            start.Environment["PYTHONUNBUFFERED"]="1";start.Environment["HF_HUB_OFFLINE"]="1";start.Environment["TOKENIZERS_PARALLELISM"]="false";
            process=new Process{StartInfo=start};
            if(!process.Start())throw new InvalidOperationException("Could not start Python worker.");
            await Jobs.UpdateOneAsync(x=>x.Id==job.Id,Builders<AiTrainingJob>.Update.Set(x=>x.WorkerPid,process.Id),cancellationToken:ct);
            var logLock=new SemaphoreSlim(1,1);
            long bytes=0;
            async Task Pump(StreamReader stream){while(await stream.ReadLineAsync() is { } line){await logLock.WaitAsync();try{if(bytes<2_000_000){line=line.Length>2000?line[..2000]:line;await File.AppendAllTextAsync(Path.Combine(dir,"worker.log"),line+"\n");bytes+=line.Length;}}finally{logLock.Release();}}}
            var stdout=Pump(process.StandardOutput);var stderr=Pump(process.StandardError);
            var timeout=DateTime.UtcNow.AddHours(config.GetValue("Training:MaxHours",24));
            while(!process.HasExited){
                if(ct.IsCancellationRequested){Kill();ct.ThrowIfCancellationRequested();}
                var state=await Jobs.Find(x=>x.Id==job.Id).FirstAsync(ct);
                if(state.Status=="cancelling"){Kill();await process.WaitForExitAsync(CancellationToken.None);await Task.WhenAll(stdout,stderr);await Finish(job,"cancelled",null);return;}
                if(DateTime.UtcNow>timeout){Kill();throw new InvalidOperationException("Training time limit exceeded.");}
                await Task.Delay(500,ct);
            }
            await Task.WhenAll(stdout,stderr);
            if(process.ExitCode!=0)throw new InvalidOperationException("Training process failed. Inspect the job log.");
            var report=JsonSerializer.Deserialize<TrainingReport>(await File.ReadAllTextAsync(Path.Combine(dir,"report.json"),ct),Json)??throw new InvalidOperationException("Missing report.");
            if(report.JobId!=job.Id||report.ModelVersion!=job.ModelVersion||report.DatasetHash!=job.DatasetHash||report.ConfigHash!=job.ConfigHash||report.TestSamples!=dataset.Rows.Count(x=>x.Split=="test")||report.MicroF1 is not >=0 or not <=1||report.MacroF1 is not >=0 or not <=1)throw new InvalidOperationException("Invalid report provenance/metrics.");
            using var weights=File.OpenRead(Path.Combine(dir,"artifact/model.safetensors"));
            var hash=Convert.ToHexString(await SHA256.HashDataAsync(weights,ct));
            if(!hash.Equals(report.ArtifactHash,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Artifact hash mismatch.");
            var policy=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(dir,"artifact/inference_config.json"),ct));
            var pc=policy.RootElement;
            if(!pc.GetProperty("labels").EnumerateArray().Select(x=>x.GetString()).SequenceEqual(labels)
                || pc.GetProperty("thresholds").GetArrayLength()!=28
                || pc.GetProperty("thresholds").EnumerateArray().Any(x=>!double.IsFinite(x.GetDouble())||x.GetDouble()<0||x.GetDouble()>1)
                || pc.GetProperty("language").GetString()!="en"||pc.GetProperty("max_length").GetInt32()!=128)
                throw new InvalidOperationException("Invalid output label/threshold configuration.");
            await Jobs.UpdateOneAsync(x=>x.Id==job.Id&&x.Status=="running",Builders<AiTrainingJob>.Update.Set(x=>x.Report,report).Set(x=>x.ArtifactVerified,true).Set(x=>x.Status,"completed").Inc(x=>x.Revision,1).Push(x=>x.Events,new(job.ActorId,"completed",DateTime.UtcNow)));
            var completed=await Jobs.Find(x=>x.Id==job.Id&&x.Status=="completed").AnyAsync(ct);
            if(completed)await db.Database.GetCollection<MongoDB.Bson.BsonDocument>("aiTrainingArtifacts").ReplaceOneAsync(
                new MongoDB.Bson.BsonDocument("_id",job.Id), new MongoDB.Bson.BsonDocument {
                    ["_id"]=job.Id,["modelVersion"]=job.ModelVersion,["datasetHash"]=job.DatasetHash,["configHash"]=job.ConfigHash,
                    ["artifactHash"]=hash.ToLowerInvariant(),["baseArtifactHash"]=job.BaseArtifactHash,["workerHash"]=job.WorkerHash,["directory"]=Path.Combine(dir,"artifact"),
                    ["state"]="verified_local_candidate_not_deployed",["createdAt"]=DateTime.UtcNow
                },new ReplaceOptions{IsUpsert=true},ct);
            // A cancellation racing with completion wins; no activation/deployment side effect.
            await Jobs.UpdateOneAsync(x=>x.Id==job.Id&&x.Status=="cancelling",Builders<AiTrainingJob>.Update.Set(x=>x.Status,"cancelled").Inc(x=>x.Revision,1));
        }catch(Exception e){Kill();logger.LogWarning("Training job {Job} ended: {Type}",job.Id,e.GetType().Name);await Finish(job,ct.IsCancellationRequested?"cancelled":"failed",e.Message);}
        finally{if(process is not null){Kill();try{await process.WaitForExitAsync(CancellationToken.None);}catch(InvalidOperationException){}process.Dispose();process=null;}}
    }
    async Task Finish(AiTrainingJob job,string status,string? failure)=>await Jobs.UpdateOneAsync(x=>x.Id==job.Id&&(x.Status=="running"||x.Status=="cancelling"),
        Builders<AiTrainingJob>.Update.Set(x=>x.Status,status).Set(x=>x.Failure,failure).Inc(x=>x.Revision,1).Push(x=>x.Events,new(job.ActorId,status,DateTime.UtcNow)));
}
