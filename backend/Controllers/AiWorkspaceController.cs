using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using mood_recommendation.Data;

namespace mood_recommendation.Controllers;

[ApiController, Route("api/AiWorkspace"), Authorize(Roles = "AIEngineer")]
public class AiWorkspaceController(MongoStore db, IWebHostEnvironment env, mood_recommendation.Services.LocalTrainingWorker worker) : ControllerBase
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private IMongoCollection<AiDataset> Datasets => db.Database.GetCollection<AiDataset>("aiDatasets");
    private IMongoCollection<AiTrainingJob> Jobs => db.Database.GetCollection<AiTrainingJob>("aiTrainingJobs");
    private int Actor => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string[] Labels => JsonDocument.Parse(System.IO.File.ReadAllText(Path.Combine(env.ContentRootPath,
        "Data/emotion-mood-mapping.json"))).RootElement.GetProperty("labels").EnumerateArray()
        .Select(x => x.GetProperty("label").GetString()!).ToArray();
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static object Summary(AiDataset d) => new { d.Id, d.Name, d.Source, d.Hash, d.CreatedAt, d.ActorId,
        count = d.Rows.Count, splits = d.Rows.GroupBy(x => x.Split).ToDictionary(x => x.Key, x => x.Count()),
        labels = d.Rows.SelectMany(x => x.Labels).Distinct().Order().ToArray() };

    [HttpGet("jobs/{id}/artifact")]
    public async Task<IActionResult> LocalArtifact(string id, CancellationToken ct) {
        var result=await worker.Artifact(id,ct);return result is null?NotFound():Ok(result);
    }
    [HttpGet("worker")]
    public IActionResult Worker() => Ok(worker.Availability());
    [HttpPost("jobs/{id}/start")]
    public async Task<IActionResult> StartLocal(string id, LocalJobInput input, CancellationToken ct) {
        var result = await worker.Start(id, input.Revision, Actor, ct); return StatusCode(result.StatusCode, result.Body);
    }
    [HttpPost("jobs/{id}/cancel")]
    public async Task<IActionResult> CancelLocal(string id, LocalJobInput input, CancellationToken ct) {
        var result = await worker.Cancel(id, input.Revision, Actor, ct); return StatusCode(result.StatusCode, result.Body);
    }
    [HttpGet("jobs/{id}/progress")]
    public async Task<IActionResult> LocalProgress(string id, CancellationToken ct) {
        var result = await worker.Progress(id, ct); return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("datasets")]
    public async Task<IActionResult> ListDatasets(CancellationToken ct) => Ok(new {
        labels = Labels, items = (await Datasets.Find(_ => true).SortByDescending(x => x.CreatedAt).Limit(200).ToListAsync(ct)).Select(Summary),
        limit = 200 });

    [HttpPost("datasets"), RequestSizeLimit(32_000_000)]
    public async Task<IActionResult> CreateDataset(DatasetInput input, CancellationToken ct)
    {
        if (input.Rows is null || input.Rows.Count is < 3 or > 60000)
            return BadRequest(new { message = "Provide 3–60,000 reviewed English samples with train/validation/test splits." });
        var allowed = Labels.ToHashSet();
        var seen = new HashSet<string>();
        foreach (var row in input.Rows)
        {
            row.Text = (row.Text ?? "").Trim();
            if (row.Text.Length is < 1 or > 1000 || row.Split is not ("train" or "validation" or "test") ||
                row.Labels is null || row.Labels.Length is < 1 or > 28 || row.Labels.Distinct().Count() != row.Labels.Length ||
                row.Labels.Any(x => !allowed.Contains(x)) || !seen.Add(string.Join(' ', row.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant()))
                return BadRequest(new { message = "Invalid sample, unknown label, or duplicate text (including cross-split leakage)." });
        }
        if (input.Rows.Select(x => x.Split).Distinct().Count() != 3)
            return BadRequest(new { message = "All three splits are required. Review the labels and split quality before training." });
        var d = new AiDataset { Id = Guid.NewGuid().ToString("N"), Name = input.Name.Trim(), Source = input.Source.Trim(),
            Rows = input.Rows, ActorId = Actor, CreatedAt = DateTime.UtcNow };
        d.Hash = Hash(JsonSerializer.Serialize(d.Rows, Json));
        if (d.ToBson().Length > 15_000_000)
            return BadRequest(new { message = "Dataset exceeds the 15 MB storage limit. Reduce sample text/size before registering." });
        await Datasets.InsertOneAsync(d, cancellationToken: ct);
        return Ok(Summary(d));
    }

    [HttpGet("datasets/{id}")]
    public async Task<IActionResult> Dataset(string id, CancellationToken ct)
    {
        var d = await Datasets.Find(x => x.Id == id).FirstOrDefaultAsync(ct);
        return d is null ? NotFound() : Ok(d);
    }

    [HttpGet("jobs")]
    public async Task<IActionResult> ListJobs(CancellationToken ct) => Ok(await Jobs.Find(_ => true)
        .SortByDescending(x => x.CreatedAt).Limit(200).ToListAsync(ct));

    [HttpPost("jobs")]
    public async Task<IActionResult> CreateJob(JobInput input, CancellationToken ct)
    {
        var d = await Datasets.Find(x => x.Id == input.DatasetId).FirstOrDefaultAsync(ct);
        if (d is null) return BadRequest(new { message = "Dataset not found." });
        if (d.Rows.Count(x => x.Split == "validation") < 400)
            return BadRequest(new { message = "The original ASL notebook requires at least 400 distinct validation samples (200 selection + 200 calibration)." });
        var config = JsonSerializer.Serialize(new { input.Epochs, input.BatchSize, input.LearningRate, input.Seed,
            input.GradientAccumulation, maxLength = 128, loss = "asl", gammaNeg = 2.0, gammaPos = 0.0, clip = 0.05,
            modelId = "FacebookAI/roberta-large", templateHash = Hash(System.IO.File.ReadAllText(TemplatePath)) }, Json);
        var job = new AiTrainingJob { Id = Guid.NewGuid().ToString("N"), DatasetId = d.Id, DatasetHash = d.Hash,
            ModelVersion = input.ModelVersion, ConfigJson = config, ConfigHash = Hash(config),
            ActorId = Actor, CreatedAt = DateTime.UtcNow };
        job.Events.Add(new(Actor, "prepared", DateTime.UtcNow));
        await Jobs.InsertOneAsync(job, cancellationToken: ct);
        return Ok(job);
    }

    [HttpGet("jobs/{id}/bundle")]
    public async Task<IActionResult> Bundle(string id, CancellationToken ct)
    {
        var j = await Jobs.Find(x => x.Id == id).FirstOrDefaultAsync(ct);
        if (j is null) return NotFound();
        var d = await Datasets.Find(x => x.Id == j.DatasetId).FirstOrDefaultAsync(ct);
        if (d is null) return Conflict(new { message = "Dataset unavailable." });
        return Ok(new { jobId = j.Id, j.ModelVersion, j.DatasetHash, j.ConfigHash, j.ConfigJson,
            datasetJson = JsonSerializer.Serialize(d.Rows, Json), labels = Labels, runner = "RoBERTa_large.ipynb",
            execution = "external", note = "Run on your own training machine/Colab. This download does not start GPU training." });
    }

    private string TemplatePath => Path.Combine(env.ContentRootPath, "Training/RoBERTa_large.template.ipynb");

    [HttpGet("jobs/{id}/notebook")]
    public async Task<IActionResult> Notebook(string id, CancellationToken ct)
    {
        var job = await Jobs.Find(x => x.Id == id).FirstOrDefaultAsync(ct);
        if (job is null) return NotFound();
        var dataset = await Datasets.Find(x => x.Id == job.DatasetId).FirstOrDefaultAsync(ct);
        if (dataset is null) return Conflict(new { message = "Dataset unavailable." });
        var template = System.IO.File.ReadAllText(TemplatePath);
        var config = JsonNode.Parse(job.ConfigJson)!;
        if (config["templateHash"]?.GetValue<string>() != Hash(template))
            return Conflict(new { message = "Notebook template changed. Prepare a new job." });
        var notebook = JsonNode.Parse(template)!;
        // Strip notebook outputs/old execution state. Code source comes from the reviewed local template.
        foreach (var cell in notebook["cells"]!.AsArray())
            if (cell!["cell_type"]!.GetValue<string>() == "code") { cell["outputs"] = new JsonArray(); cell["execution_count"] = null; }
        var source = string.Concat(notebook["cells"]![1]!["source"]!.AsArray().Select(x => x!.GetValue<string>()));
        var bundle = new { jobId = job.Id, job.ModelVersion, job.DatasetHash, job.ConfigHash, job.ConfigJson,
            datasetJson = JsonSerializer.Serialize(dataset.Rows, Json), labels = Labels };
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(bundle, Json)));
        var bridge = """
import base64, csv
WORKSPACE = json.loads(base64.b64decode('__BUNDLE__').decode('utf-8'))
for content, digest in [('datasetJson', 'datasetHash'), ('configJson', 'configHash')]:
    assert hashlib.sha256(WORKSPACE[content].encode()).hexdigest() == WORKSPACE[digest], 'Workspace checksum mismatch'
workspace_config = json.loads(WORKSPACE['configJson'])
workspace_rows = json.loads(WORKSPACE['datasetJson'])
BASE = BASE / 'workspace-jobs' / WORKSPACE['jobId']
DATA_DIR = BASE / 'data'
DATA_DIR.mkdir(parents=True, exist_ok=True)
RUN_ID = WORKSPACE['modelVersion']
EPOCHS = workspace_config['epochs']
BATCH_SIZE = workspace_config['batchSize']
GRAD_ACCUM = workspace_config['gradientAccumulation']
LEARNING_RATE = workspace_config['learningRate']
SEED = workspace_config['seed']
ALLOW_DOWNLOAD_IF_EMPTY = False
for split, filename in [('train','train.csv'), ('validation','validation.csv'), ('test','test.csv')]:
    with (DATA_DIR / filename).open('w', encoding='utf-8', newline='') as stream:
        writer = csv.DictWriter(stream, fieldnames=['text','labels','id'])
        writer.writeheader()
        for index, row in enumerate(workspace_rows):
            if row['split'] == split:
                writer.writerow({'text':row['text'], 'labels':json.dumps(row['labels']), 'id':str(index)})
""".Replace("__BUNDLE__", encoded);
        if (!source.Contains("LOG_DIR = BASE / 'log' / RUN_ID")) return Conflict(new { message = "Unsupported notebook template layout." });
        source = source.Replace("LOG_DIR = BASE / 'log' / RUN_ID", bridge + "\nLOG_DIR = BASE / 'log' / RUN_ID");
        notebook["cells"]![1]!["source"] = new JsonArray(JsonValue.Create(source));
        var export = """
# Run after the original evaluation/export cells. This reports results; it never deploys the model.
from google.colab import files
assert run_saved['run_id'] == WORKSPACE['modelVersion']
for key, expected in [('epochs',workspace_config['epochs']), ('batch_size',workspace_config['batchSize']),
                      ('gradient_accumulation',workspace_config['gradientAccumulation']),
                      ('learning_rate',workspace_config['learningRate']), ('seed',workspace_config['seed'])]:
    assert run_saved[key] == expected, 'Training settings differ from the registered job'
assert run_saved['loss_config'] == {'kind':'asl', 'gamma_neg':2.0, 'gamma_pos':0.0, 'clip':0.05}
assert run_saved['labels'] == WORKSPACE['labels']
# A disabled policy can use 1.01 as a notebook sentinel; runtime accepts only [0,1].
runtime_config_path = BEST_DIR / 'inference_config.json'
runtime_config = json.loads(runtime_config_path.read_text())
if not runtime_config['selective_policy']['enabled']:
    runtime_config['selective_policy']['threshold'] = min(1.0, runtime_config['selective_policy']['threshold'])
    runtime_config_path.write_text(json.dumps(runtime_config, indent=2))
artifact_digest = hashlib.sha256()
with (BEST_DIR / 'model.safetensors').open('rb') as stream:
    for chunk in iter(lambda: stream.read(1024 * 1024), b''):
        artifact_digest.update(chunk)
workspace_report = {key: WORKSPACE[key] for key in ['jobId','modelVersion','datasetHash','configHash']}
workspace_report.update(artifactHash=artifact_digest.hexdigest(), microF1=report['full_test']['micro_f1'],
    macroF1=report['full_test']['macro_f1'], testSamples=audit['after']['test'])
report_path = OUT_DIR / 'workspace_report.json'
report_path.write_text(json.dumps(workspace_report, indent=2))
files.download(str(report_path))
print('Import workspace_report.json in AI workspace. Artifact still requires server validation before activation.')
""";
        notebook["cells"]!.AsArray().Add(new JsonObject { ["cell_type"] = "code", ["metadata"] = new JsonObject(),
            ["execution_count"] = null, ["outputs"] = new JsonArray(), ["source"] = new JsonArray(JsonValue.Create(export)) });
        return Ok(notebook);
    }

    [HttpPost("jobs/{id}/state")]
    public async Task<IActionResult> State(string id, JobStateInput input, CancellationToken ct)
    {
        var job = await Jobs.Find(x => x.Id == id).FirstOrDefaultAsync(ct);
        if (job is null) return NotFound();
        if (job.Execution == "local" || job.Revision != input.Revision || job.Status is "reported" or "cancelled" or "failed" ||
            input.State is not ("running" or "cancelled" or "failed") || input.State == job.Status)
            return Conflict(new { message = "Job changed or transition not allowed. Refresh the list." });
        job.Status = input.State;
        return await Save(job, input.Revision, ct);
    }

    [HttpPost("jobs/{id}/report"), RequestSizeLimit(64_000)]
    public async Task<IActionResult> Report(string id, TrainingReport input, CancellationToken ct)
    {
        var job = await Jobs.Find(x => x.Id == id).FirstOrDefaultAsync(ct);
        if (job is null) return NotFound();
        if (job.Execution == "local" || job.Status is not ("prepared" or "running") || job.Revision != input.Revision)
            return Conflict(new { message = "Job is no longer accepting reports. Refresh the list." });
        if (input.JobId != job.Id || input.ModelVersion != job.ModelVersion || input.DatasetHash != job.DatasetHash || input.ConfigHash != job.ConfigHash)
            return BadRequest(new { message = "Report provenance does not match this job/dataset/configuration." });
        var dataset = await Datasets.Find(x => x.Id == job.DatasetId).FirstOrDefaultAsync(ct);
        if (dataset is null || input.TestSamples != dataset.Rows.Count(x => x.Split == "test"))
            return BadRequest(new { message = "Test sample count does not match the registered dataset." });
        job.Report = input;
        job.Status = "reported"; // A submitted result is not a server-verified artifact or deployed model.
        return await Save(job, input.Revision, ct);
    }

    private async Task<IActionResult> Save(AiTrainingJob job, int revision, CancellationToken ct)
    {
        job.Revision++;
        job.Events.Add(new(Actor, job.Status, DateTime.UtcNow));
        var result = await Jobs.ReplaceOneAsync(x => x.Id == job.Id && x.Revision == revision, job, cancellationToken: ct);
        return result.ModifiedCount == 1 ? Ok(job) : Conflict(new { message = "Job changed. Refresh before retrying." });
    }

    [HttpGet("feedback")]
    public async Task<IActionResult> Feedback(CancellationToken ct)
    {
        var rows = await db.DanhGias.OrderByDescending(x => x.ThoiGian).Take(20000).ToListAsync(ct);
        var ids = rows.Select(x => x.NoiDungID).Distinct().ToArray();
        var names = await db.NoiDungGiaiTris.Where(x => ids.Contains(x.NoiDungID)).ToListAsync(ct);
        return Ok(new { count = rows.Count, limit = 20000, note = "Latest ratings; no account identities or comment text exposed.",
            distribution = Enumerable.Range(1, 5).Select(stars => new { stars, count = rows.Count(x => x.SoSao == stars) }),
            items = rows.GroupBy(x => x.NoiDungID).Select(g => new { contentId = g.Key,
                title = names.FirstOrDefault(x => x.NoiDungID == g.Key)?.TieuDe ?? g.Key,
                count = g.Count(), average = Math.Round(g.Average(x => x.SoSao), 2) }) });
    }

    [HttpPost("feedback/export")]
    public async Task<IActionResult> ExportFeedback(CancellationToken ct)
    {
        var rows = await db.DanhGias.OrderByDescending(x => x.ThoiGian).Take(20000).ToListAsync(ct);
        var aliases = rows.Select(x => x.TaiKhoanID).Distinct().ToDictionary(x => x, _ => Guid.NewGuid().ToString("N"));
        return Ok(new { exportedAt = DateTime.UtcNow, task = "recommendation_ratings", limit = 20000,
            note = "Pseudonymous IDs regenerated per export. Not emotion labels. No comment text included.",
            rows = rows.Select(x => new { user = aliases[x.TaiKhoanID], contentId = x.NoiDungID, stars = x.SoSao }) });
    }
}

public sealed class EmotionSample
{
    public string Text { get; set; } = "";
    public string[] Labels { get; set; } = [];
    public string Split { get; set; } = "";
}
public sealed class DatasetInput
{
    [Required, StringLength(100, MinimumLength = 2)] public string Name { get; set; } = "";
    [Required, StringLength(300, MinimumLength = 3)] public string Source { get; set; } = "";
    public List<EmotionSample> Rows { get; set; } = [];
}
public sealed class AiDataset
{
    [BsonId] public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Source { get; set; } = "";
    public string Hash { get; set; } = "";
    public int ActorId { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<EmotionSample> Rows { get; set; } = [];
}
public sealed class JobInput
{
    [Required] public string DatasetId { get; set; } = "";
    [Required, RegularExpression("^[a-zA-Z0-9][a-zA-Z0-9_-]{2,79}$")] public string ModelVersion { get; set; } = "";
    [Range(1, 10)] public int Epochs { get; set; } = 6;
    [Range(1, 8)] public int BatchSize { get; set; } = 2;
    [Range(1, 64)] public int GradientAccumulation { get; set; } = 16;
    [Range(0.000001, 0.0001)] public double LearningRate { get; set; } = 0.00001;
    [Range(0, 2147483647)] public int Seed { get; set; } = 42;
}
public sealed class AiTrainingJob
{
    public int? WorkerPid { get; set; }
    public string? WorkerHash { get; set; }
    public string? BaseArtifactHash { get; set; }
    public string Execution { get; set; } = "external";
    public bool ArtifactVerified { get; set; }
    public string? Failure { get; set; }
    [BsonId] public string Id { get; set; } = "";
    public string DatasetId { get; set; } = "";
    public string DatasetHash { get; set; } = "";
    public string ModelVersion { get; set; } = "";
    public string ConfigJson { get; set; } = "";
    public string ConfigHash { get; set; } = "";
    public string Status { get; set; } = "prepared";
    public int Revision { get; set; }
    public int ActorId { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<JobEvent> Events { get; set; } = [];
    public TrainingReport? Report { get; set; }
}
public sealed record JobEvent(int ActorId, string Action, DateTime CreatedAt);
public sealed class JobStateInput
{
    public int Revision { get; set; }
    [Required] public string State { get; set; } = "";
}
public sealed class TrainingReport
{
    public int Revision { get; set; }
    [Required] public string JobId { get; set; } = "";
    [Required] public string ModelVersion { get; set; } = "";
    [Required] public string DatasetHash { get; set; } = "";
    [Required] public string ConfigHash { get; set; } = "";
    [Required, RegularExpression("^[a-fA-F0-9]{64}$")] public string ArtifactHash { get; set; } = "";
    [Required, Range(0, 1)] public double? MicroF1 { get; set; }
    [Required, Range(0, 1)] public double? MacroF1 { get; set; }
    [Required, Range(1, 60000)] public int? TestSamples { get; set; }
}

public sealed class LocalJobInput { public int Revision { get; set; } }
