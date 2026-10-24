using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.IO.Compression;
using mood_recommendation.Services;
using SharpCompress.Archives;
namespace mood_recommendation.Controllers;
[ApiController, Route("api/[controller]"), Authorize(Roles = "Admin,AIEngineer")]
public class ModelsController(
    MoodAnalysisService analysis,
    AiModelRegistryService registry, ValenceArousalAnalysisService vaAnalysis, VietnameseEmotionService viAnalysis,
    IWebHostEnvironment environment, IConfiguration configuration) : ControllerBase
{
    private const long MaxVaPackageBytes = 2L * 1024 * 1024 * 1024;
    private const long MaxVaRequestBytes = MaxVaPackageBytes + 16L * 1024 * 1024;
    [HttpGet]
    public async Task<IActionResult> Status(CancellationToken cancellationToken) =>
        Ok(await registry.StatusAsync(cancellationToken));

    [HttpGet("va"), Authorize(Roles = "AIEngineer")]
    public async Task<IActionResult> ValenceArousalModels(CancellationToken ct) =>
        Ok(new { runtime = await vaAnalysis.RuntimeStatusAsync(ct), catalog = await vaAnalysis.CandidatesAsync(ct) });

    [HttpGet("vi"), Authorize(Roles = "AIEngineer")]
    public async Task<IActionResult> VietnameseModels(CancellationToken ct) =>
        Ok(new { runtime = await viAnalysis.RuntimeStatusAsync(ct), catalog = await viAnalysis.CandidatesAsync(ct) });

    [HttpPost("vi/activate"), Authorize(Roles = "AIEngineer")]
    public async Task<IActionResult> ActivateVietnameseModel(VaModelActivationRequest request, CancellationToken ct)
    {
        var result = await viAnalysis.ActivateAsync(request.Version, ct);
        if (result.StatusCode is >= 200 and < 300)
            return StatusCode(result.StatusCode, new { status = "ready", activeVersion = result.ActiveVersion, device = result.Device });
        return StatusCode(result.StatusCode, new { status = "error", message = result.Message ?? "Vietnamese model activation failed." });
    }

    [HttpPost("vi/upload-and-activate"), Authorize(Roles = "AIEngineer")]
    [RequestSizeLimit(MaxVaRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxVaRequestBytes)]
    public async Task<IActionResult> UploadVietnameseModel([FromForm] VaModelUploadRequest request, CancellationToken ct)
    {
        const long maxExpandedBytes = 3L * 1024 * 1024 * 1024;
        var version = request.Version.Trim();
        if (request.Package is null || request.Package.Length == 0 || request.Package.Length > MaxVaPackageBytes)
            return BadRequest(new { message = "Select a non-empty Vietnamese model package under 2 GB." });
        var extension = Path.GetExtension(request.Package.FileName);
        if (!new[] { ".zip", ".rar" }.Contains(extension, StringComparer.OrdinalIgnoreCase))
            return BadRequest(new { message = "Upload a ZIP or RAR containing the PhoBERT best_model directory." });

        var outputRoot = Path.GetFullPath(Path.Combine(environment.ContentRootPath, "../code-train/train-KLCN/output"));
        var modelRoot = Path.Combine(outputRoot, "vietnamese-emotion");
        Directory.CreateDirectory(modelRoot);
        var destination = Path.Combine(modelRoot, version);
        if (Directory.Exists(destination) || System.IO.File.Exists(destination))
            return Conflict(new { message = "That Vietnamese model version already exists; choose a new version." });

        var staging = Path.Combine(modelRoot, ".upload-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        var moved = false;
        try
        {
            var packagePath = Path.Combine(staging, "package" + extension.ToLowerInvariant());
            await using (var target = new FileStream(packagePath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: 1024 * 1024, useAsync: true))
                await request.Package.CopyToAsync(target, ct);

            var entries = new List<VaPackageEntry>();
            if (extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                using var input = System.IO.File.OpenRead(packagePath);
                using var archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: false);
                entries.AddRange(archive.Entries.Select(entry => new VaPackageEntry(entry.FullName, entry.Length,
                    entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'),
                    ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000, () => entry.Open())));
                await ExtractVietnameseArtifactsAsync(entries, staging, maxExpandedBytes, ct);
            }
            else
            {
                using var archive = ArchiveFactory.OpenArchive(packagePath);
                entries.AddRange(archive.Entries.Select(entry => new VaPackageEntry(entry.Key ?? "", entry.Size,
                    entry.IsDirectory, entry.LinkTarget is not null, () => entry.OpenEntryStream())));
                await ExtractVietnameseArtifactsAsync(entries, staging, maxExpandedBytes, ct);
            }

            System.IO.File.Delete(packagePath);
            Directory.Move(staging, destination);
            moved = true;
            var result = await viAnalysis.ActivateAsync(version, ct);
            if (result.StatusCode is < 200 or >= 300)
                return StatusCode(result.StatusCode, new { message = result.Message ?? "Vietnamese model uploaded, but failed loading/readiness. Existing English and Vietnamese active models remain unchanged.", uploadedVersion = version });
            return Ok(new { status = "ready", language = "vi", activeVersion = result.ActiveVersion, device = result.Device,
                message = "Vietnamese model uploaded and activated. English model was not changed." });
        }
        catch (Exception ex) when (ex is InvalidDataException or SharpCompress.Common.ArchiveException)
        {
            return BadRequest(new { message = "The archive is invalid or does not contain a supported PhoBERT sequence-classification bundle." });
        }
        catch (IOException)
        {
            return StatusCode(StatusCodes.Status507InsufficientStorage, new { message = "Could not save the Vietnamese model package; check disk space and permissions." });
        }
        catch (UnauthorizedAccessException)
        {
            return StatusCode(StatusCodes.Status507InsufficientStorage, new { message = "The server cannot write to its Vietnamese model artifact folder." });
        }
        finally
        {
            if (!moved && Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }

    private static async Task ExtractVietnameseArtifactsAsync(List<VaPackageEntry> entries, string staging,
        long maxExpandedBytes, CancellationToken ct)
    {
        if (entries.Count is 0 or > 500) throw new InvalidDataException("Archive must contain between 1 and 500 entries.");
        long expanded = 0;
        var files = new List<(VaPackageEntry Entry, string[] Segments, string Name)>();
        foreach (var entry in entries)
        {
            if (entry.IsDirectory) continue;
            expanded = checked(expanded + entry.Length);
            if (expanded > maxExpandedBytes || entry.IsSymbolicLink) throw new InvalidDataException("Archive exceeds safety limits.");
            var name = entry.FullName.Replace('\\', '/');
            var segments = name.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0 || name.StartsWith('/') || segments.Any(part => part is "." or ".." || part.Contains(':')))
                throw new InvalidDataException("Archive contains an unsafe file path.");
            files.Add((entry, segments, name));
        }
        var configEntry = files.FirstOrDefault(file => file.Segments[^1].Equals("config.json", StringComparison.OrdinalIgnoreCase)
            && files.Any(other => other.Name.Equals(string.Join('/', file.Segments[..^1].Append("model.safetensors")), StringComparison.OrdinalIgnoreCase)
                || other.Name.Equals(string.Join('/', file.Segments[..^1].Append("pytorch_model.bin")), StringComparison.OrdinalIgnoreCase)));
        var stateDict = files.FirstOrDefault(file => file.Segments[^1].EndsWith(".pt", StringComparison.OrdinalIgnoreCase));
        if (configEntry.Entry is null || !files.Any(file => file.Name.Equals(string.Join('/', configEntry.Segments[..^1].Append("tokenizer_config.json")), StringComparison.OrdinalIgnoreCase))
            || !files.Any(file => file.Name.Equals(string.Join('/', configEntry.Segments[..^1].Append("vocab.txt")), StringComparison.OrdinalIgnoreCase))
            || !files.Any(file => file.Name.Equals(string.Join('/', configEntry.Segments[..^1].Append("bpe.codes")), StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Required bundle: best_model/config.json, model.safetensors (or pytorch_model.bin), tokenizer_config.json, vocab.txt and bpe.codes. A .pt state dict can be included with these files.");

        var prefix = configEntry.Segments[..^1];
        var output = Path.Combine(staging, "model");
        Directory.CreateDirectory(output);
        var copiedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            var inModel = file.Segments.Length > prefix.Length && prefix.SequenceEqual(file.Segments.Take(prefix.Length), StringComparer.OrdinalIgnoreCase);
            var isStateDict = !inModel && stateDict.Entry is not null && ReferenceEquals(file.Entry, stateDict.Entry)
                && !files.Any(candidate => candidate.Segments[^1].Equals("model.safetensors", StringComparison.OrdinalIgnoreCase));
            var isMetadata = file.Segments[^1] is "preprocessing.json" or "confidence_threshold.json";
            if (!inModel && !isStateDict && !isMetadata) continue;
            var relative = isStateDict ? "model.pt" : inModel ? Path.Combine(file.Segments[prefix.Length..]) : file.Segments[^1];
            if (relative.EndsWith(".pt", StringComparison.OrdinalIgnoreCase) && !isStateDict) continue;
            if (!(relative.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || relative.EndsWith(".safetensors", StringComparison.OrdinalIgnoreCase)
                || relative.EndsWith(".bin", StringComparison.OrdinalIgnoreCase) || relative.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
                || relative.EndsWith(".codes", StringComparison.OrdinalIgnoreCase) || relative.EndsWith(".model", StringComparison.OrdinalIgnoreCase))) continue;
            var dest = Path.GetFullPath(Path.Combine(output, relative));
            var outputRoot = Path.GetFullPath(output) + Path.DirectorySeparatorChar;
            if (!dest.StartsWith(outputRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Unsafe model artifact path.");
            if (!copiedNames.Add(relative)) throw new InvalidDataException("Archive contains duplicate model asset paths.");
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            await CopyPackageEntryAsync(file.Entry, dest, ct);
        }
    }

    [HttpPost("va/activate"), Authorize(Roles = "AIEngineer")]
    public async Task<IActionResult> ActivateValenceArousalModel(VaModelActivationRequest request, CancellationToken ct)
    {
        var result = await vaAnalysis.ActivateModelAsync(request.Version, ct);
        if (result.StatusCode is >= 200 and < 300)
            return StatusCode(result.StatusCode, new { status = "ready", activeVersion = result.ActiveVersion, device = result.Device });
        return StatusCode(result.StatusCode,
            new { status = "error", message = result.Message ?? "Model activation failed." });
    }

    [HttpPost("va/upload-and-activate"), Authorize(Roles = "AIEngineer")]
    [RequestSizeLimit(MaxVaRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxVaRequestBytes)]
    public async Task<IActionResult> UploadAndActivateValenceArousalModel(
        [FromForm] VaModelUploadRequest request, CancellationToken ct)
    {
        const long maxExpandedBytes = 3L * 1024 * 1024 * 1024;
        var version = request.Version.Trim();
        if (request.Package is null || request.Package.Length == 0 || request.Package.Length > MaxVaPackageBytes)
            return BadRequest(new { message = "Select a non-empty ZIP or RAR package under 2 GB." });
        var packageExtension = Path.GetExtension(request.Package.FileName);
        if (!string.Equals(packageExtension, ".zip", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(packageExtension, ".rar", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "Upload a ZIP or RAR containing the checkpoint and tokenizer folder." });

        var modelRoot = Path.GetFullPath(Path.Combine(environment.ContentRootPath,
            "../code-train/train-KLCN/output"));
        Directory.CreateDirectory(modelRoot);
        var destination = Path.Combine(modelRoot, version);
        if (Directory.Exists(destination) || System.IO.File.Exists(destination))
            return Conflict(new { message = "That model version already exists. Choose a new version name; existing artifacts are never overwritten." });

        var staging = Path.Combine(modelRoot, ".upload-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        var moved = false;
        try
        {
            var packagePath = Path.Combine(staging, "package" + packageExtension.ToLowerInvariant());
            await using (var packageStream = new FileStream(packagePath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: 1024 * 1024, useAsync: true))
                await request.Package.CopyToAsync(packageStream, ct);

            var packageEntries = new List<VaPackageEntry>();
            if (string.Equals(packageExtension, ".zip", StringComparison.OrdinalIgnoreCase))
            {
                using var input = System.IO.File.OpenRead(packagePath);
                using var archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: false);
                packageEntries.AddRange(archive.Entries.Select(entry => new VaPackageEntry(
                    entry.FullName, entry.Length, entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'),
                    ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000, () => entry.Open())));
                await ProcessVaPackageEntriesAsync(packageEntries, staging, version, maxExpandedBytes, ct);
            }
            else
            {
                using var archive = ArchiveFactory.OpenArchive(packagePath);
                packageEntries.AddRange(archive.Entries.Select(entry => new VaPackageEntry(
                    entry.Key ?? "", entry.Size, entry.IsDirectory, entry.LinkTarget is not null, () => entry.OpenEntryStream())));
                await ProcessVaPackageEntriesAsync(packageEntries, staging, version, maxExpandedBytes, ct);
            }

            System.IO.File.Delete(packagePath);
            Directory.Move(staging, destination);
            moved = true;
            var activated = await vaAnalysis.ActivateModelAsync(version, ct);
            if (activated.StatusCode is < 200 or >= 300)
                return StatusCode(activated.StatusCode,
                    new { message = activated.Message ?? "Upload succeeded, but the model did not pass loading and readiness checks. The current model remains active.", uploadedVersion = version });
            return Ok(new { status = "ready", activeVersion = activated.ActiveVersion, device = activated.Device,
                message = "Model uploaded, loaded, and activated." });
        }
        catch (Exception ex) when (ex is InvalidDataException or SharpCompress.Common.ArchiveException)
        {
            return BadRequest(new { message = "The uploaded file is not a valid ZIP or RAR package." });
        }
        catch (IOException)
        {
            return StatusCode(StatusCodes.Status507InsufficientStorage,
                new { message = "The server could not save the model package. Check available disk space and permissions." });
        }
        catch (UnauthorizedAccessException)
        {
            return StatusCode(StatusCodes.Status507InsufficientStorage,
                new { message = "The server cannot write model files to its configured artifact folder." });
        }
        finally
        {
            if (!moved && Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }

    private static async Task ProcessVaPackageEntriesAsync(List<VaPackageEntry> entries, string staging,
        string version, long maxExpandedBytes, CancellationToken ct)
    {
            if (entries.Count is 0 or > 500)
                throw new InvalidDataException("Archive must contain between 1 and 500 files.");
            long expandedBytes = 0;
            var checkpointEntries = new List<VaPackageEntry>();
            var tokenizerEntries = new List<(VaPackageEntry Entry, string RelativePath)>();
            foreach (var entry in entries)
            {
                if (entry.IsDirectory) continue;
                expandedBytes = checked(expandedBytes + entry.Length);
                if (expandedBytes > maxExpandedBytes)
                    throw new InvalidDataException("Expanded package exceeds 3 GB safety limit.");
                if (entry.IsSymbolicLink)
                    throw new InvalidDataException("Package cannot contain symbolic links.");

                var name = entry.FullName.Replace('\\', '/');
                var segments = name.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (segments.Length == 0)
                    throw new InvalidDataException("Package contains an empty file path.");
                if (name.StartsWith('/') || segments.Any(part => part is "." or ".." || part.Contains(':')))
                    throw new InvalidDataException("Package contains an unsafe file path.");
                var fileName = segments[^1];
                var tokenizerIndex = Array.FindIndex(segments, part => string.Equals(part, "tokenizer", StringComparison.OrdinalIgnoreCase));
                if (fileName.EndsWith(".pt", StringComparison.OrdinalIgnoreCase) && tokenizerIndex < 0)
                    checkpointEntries.Add(entry);
                if (tokenizerIndex >= 0 && tokenizerIndex < segments.Length - 1)
                    tokenizerEntries.Add((entry, Path.Combine(segments[(tokenizerIndex + 1)..])));
            }

            if (checkpointEntries.Count != 1 || !tokenizerEntries.Any(item =>
                    string.Equals(item.RelativePath.Replace('\\', '/'), "tokenizer.json", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Required: one .pt checkpoint plus tokenizer/tokenizer.json and tokenizer assets.");

            var checkpointPath = Path.Combine(staging, version + ".pt");
            await CopyPackageEntryAsync(checkpointEntries[0], checkpointPath, ct);
            foreach (var (entry, relativePath) in tokenizerEntries)
            {
                var outputPath = Path.GetFullPath(Path.Combine(staging, "tokenizer", relativePath));
                var tokenizerRoot = Path.GetFullPath(Path.Combine(staging, "tokenizer")) + Path.DirectorySeparatorChar;
                if (!outputPath.StartsWith(tokenizerRoot, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Package contains an unsafe tokenizer path.");
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                await CopyPackageEntryAsync(entry, outputPath, ct);
            }

        }

    private static async Task CopyPackageEntryAsync(VaPackageEntry entry, string destination, CancellationToken ct)
    {
        await using var source = entry.Open();
        await using var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            bufferSize: 1024 * 1024, useAsync: true);
        await source.CopyToAsync(target, ct);
    }

    [HttpPost("test"), Authorize(Roles = "AIEngineer")]
    [RequestSizeLimit(16 * 1024)]
    public async Task<IActionResult> Test(MoodAnalysisRequest request)
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = await analysis.AnalyzeAsync(
            request.Text, request.Language, userId, false, HttpContext.RequestAborted);
        return result.Status == "unavailable" ? StatusCode(503, result) : Ok(result);
    }

    [HttpGet("evaluation")]
    public IActionResult Evaluation()
    {
        var path = configuration["AI:EvaluationReportPath"] ?? Path.GetFullPath(Path.Combine(environment.ContentRootPath,
            "../code-train/train-KLCN/output/roberta_large_asl_en_01/best_model/evaluation_summary.json"));
        if (!System.IO.File.Exists(path)) return Ok(new { available = false });
        using var document = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(path));
        if (document.RootElement.GetProperty("run_id").GetString() != configuration["AI:ModelVersion"])
            return Ok(new { available = false, reason = "Report version differs from configured model." });
        return Ok(new { available = true, report = document.RootElement.Clone() });
    }

    [HttpGet("coverage"), Authorize(Roles = "AIEngineer")]
    public async Task<IActionResult> Coverage(CancellationToken ct) => Ok(await analysis.CoverageAsync(ct));

    [HttpGet("mapping"), Authorize(Roles = "AIEngineer")]
    public async Task<IActionResult> Mapping(CancellationToken ct) => Ok(await analysis.MappingViewAsync(ct));

    [HttpPut("mapping"), Authorize(Roles = "AIEngineer")]
    public async Task<IActionResult> SaveMapping(SaveMappingRequest request, CancellationToken ct) =>
        Apply(await analysis.SaveMappingAsync(request, ActorId, ActorRole, ct));

    [HttpGet("audit")]
    public async Task<IActionResult> Audit(CancellationToken cancellationToken) =>
        Ok(await registry.AuditAsync(cancellationToken));

    [HttpPost("{version}/validate")]
    public async Task<IActionResult> Validate(string version, CancellationToken cancellationToken) =>
        Apply(await registry.ValidateAsync(version, ActorId, ActorRole, cancellationToken));

    [HttpPost("{version}/activate")]
    public async Task<IActionResult> Activate(
        string version, RegistryRevisionRequest request, CancellationToken cancellationToken) =>
        Apply(await registry.ActivateAsync(version, request.ExpectedRevision, ActorId, ActorRole, cancellationToken));

    [HttpPost("deactivate")]
    public async Task<IActionResult> Deactivate(
        RegistryRevisionRequest request, CancellationToken cancellationToken) =>
        Apply(await registry.DeactivateAsync(request.ExpectedRevision, ActorId, ActorRole, cancellationToken));

    [HttpPost("rollback")]
    public async Task<IActionResult> Rollback(
        RegistryRevisionRequest request, CancellationToken cancellationToken) =>
        Apply(await registry.RollbackAsync(request.ExpectedRevision, ActorId, ActorRole, cancellationToken));

    private int ActorId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string ActorRole => User.FindFirstValue(ClaimTypes.Role) ?? "User";
    private IActionResult Apply(RegistryOperation result) =>
        StatusCode(result.StatusCode, result.Body);
}

public sealed record RegistryRevisionRequest(long ExpectedRevision);

public sealed record VaModelActivationRequest(
    [System.ComponentModel.DataAnnotations.Required]
    [System.ComponentModel.DataAnnotations.RegularExpression("^[A-Za-z0-9][A-Za-z0-9_-]{2,79}$")]
    string Version);

public sealed class VaModelUploadRequest
{
    [System.ComponentModel.DataAnnotations.Required]
    [System.ComponentModel.DataAnnotations.RegularExpression("^[A-Za-z0-9][A-Za-z0-9_-]{2,79}$")]
    public string Version { get; set; } = "";

    [System.ComponentModel.DataAnnotations.Required]
    public IFormFile? Package { get; set; }
}

internal sealed record VaPackageEntry(
    string FullName, long Length, bool IsDirectory, bool IsSymbolicLink, Func<Stream> Open);
