using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using mood_recommendation.Services;

namespace mood_recommendation.Controllers;

[ApiController, Route("api/[controller]"), Authorize]
public sealed class MoodAnalysisController(
    MoodAnalysisService analysis, BilingualMoodAnalysisService bilingual, BilingualPolicyService policies,
    ILogger<MoodAnalysisController> logger) : ControllerBase
{
    // Compatibility endpoint for the independent, 28-label GoEmotions pipeline.
    [HttpPost]
    [RequestSizeLimit(16 * 1024)]
    public async Task<IActionResult> Analyze(MoodAnalysisRequest request)
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = await analysis.AnalyzeAsync(request.Text, request.Language, userId, true, HttpContext.RequestAborted);
        return result.Status == "unavailable" ? StatusCode(503, result) : Ok(result);
    }

    [HttpPost("valence-arousal")]
    [RequestSizeLimit(16 * 1024)]
    public async Task<IActionResult> AnalyzeValenceArousal(VaMoodAnalysisRequest request)
    {
        Response.Headers.CacheControl = "no-store";
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var timer=System.Diagnostics.Stopwatch.StartNew();
        var result = await bilingual.AnalyzeAsync(request.Text, request.Language, userId, HttpContext.RequestAborted);
        try { await policies.ObserveAsync(result,timer.Elapsed.TotalMilliseconds,HttpContext.RequestAborted); }
        catch(Exception error) when(error is not OperationCanceledException) { logger.LogWarning("Inference metrics unavailable ({Type}).",error.GetType().Name); }
        return result.Status == "unavailable" ? StatusCode(503, result) : Ok(result);
    }
}

public sealed class VaMoodAnalysisRequest
{
    [System.ComponentModel.DataAnnotations.Required]
    [System.ComponentModel.DataAnnotations.StringLength(4000, MinimumLength = 1)]
    public string Text { get; set; } = "";

    [System.ComponentModel.DataAnnotations.Required]
    [System.ComponentModel.DataAnnotations.RegularExpression("^(auto|en|vi)$")]
    public string Language { get; set; } = "auto";
}

public sealed class MoodAnalysisRequest
{
    [System.ComponentModel.DataAnnotations.Required]
    [System.ComponentModel.DataAnnotations.StringLength(4000, MinimumLength = 1)]
    public string Text { get; set; } = "";

    [System.ComponentModel.DataAnnotations.Required]
    [System.ComponentModel.DataAnnotations.RegularExpression("^(en|vi)$")]
    public string Language { get; set; } = "vi";
}
