using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using mood_recommendation.Services;
namespace mood_recommendation.Controllers;
[ApiController, Route("api/[controller]"), Authorize(Roles = "Admin,AIEngineer")]
public class ModelsController(
    MoodAnalysisService analysis,
    AiModelRegistryService registry, IWebHostEnvironment environment, IConfiguration configuration) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Status(CancellationToken cancellationToken) =>
        Ok(await registry.StatusAsync(cancellationToken));

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
