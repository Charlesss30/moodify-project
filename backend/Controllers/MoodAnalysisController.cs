using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using mood_recommendation.Services;

namespace mood_recommendation.Controllers;

[ApiController, Route("api/[controller]"), Authorize]
public sealed class MoodAnalysisController(MoodAnalysisService analysis) : ControllerBase
{
    [HttpPost]
    [RequestSizeLimit(16 * 1024)]
    public async Task<IActionResult> Analyze(MoodAnalysisRequest request)
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = await analysis.AnalyzeAsync(
            request.Text, request.Language, userId, true, HttpContext.RequestAborted);
        return result.Status == "unavailable" ? StatusCode(503, result) : Ok(result);
    }
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
