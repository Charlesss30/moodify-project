using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using mood_recommendation.Services;

namespace mood_recommendation.Controllers;

[ApiController, Route("api/[controller]"), Authorize]
public sealed class MoodAnalysisController(
    MoodAnalysisService analysis,
    ValenceArousalAnalysisService vaAnalysis,
    VietnameseEmotionService viAnalysis) : ControllerBase
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

    [HttpPost("valence-arousal")]
    [RequestSizeLimit(16 * 1024)]
    public async Task<IActionResult> AnalyzeValenceArousal(VaMoodAnalysisRequest request)
    {
        if (request.Language == "vi")
        {
            var vietnamese = await viAnalysis.AnalyzeAsync(request.Text, HttpContext.RequestAborted);
            if (vietnamese is null)
                return StatusCode(503, new { message = "Vietnamese emotion model is unavailable." });
            return Ok(new
            {
                status = "matched", language = "vi", modelVersion = vietnamese.ModelVersion,
                emotion = vietnamese.Emotion, confidence = vietnamese.Confidence, accepted = vietnamese.Accepted,
                emotionCandidates = vietnamese.EmotionCandidates,
                valence = vietnamese.Valence, arousal = vietnamese.Arousal,
                mood = new { tamTrangID = vietnamese.MoodId, tenTamTrang = vietnamese.MoodName,
                    valence = vietnamese.MoodValence, arousal = vietnamese.MoodArousal }
            });
        }
        var result = await vaAnalysis.AnalyzeAsync(request.Text, HttpContext.RequestAborted);
        if (result is null)
            return StatusCode(503, new { message = "Valence/arousal model is unavailable." });
        return Ok(new
        {
            status = "matched",
            language = "en",
            modelVersion = result.ModelVersion,
            valence = result.Valence,
            arousal = result.Arousal,
            moodCandidates = result.MoodCandidates,
            mood = new
            {
                tamTrangID = result.MoodId,
                tenTamTrang = result.MoodName,
                valence = result.MoodValence,
                arousal = result.MoodArousal
            }
        });
    }
}

public sealed class VaMoodAnalysisRequest
{
    [System.ComponentModel.DataAnnotations.Required]
    [System.ComponentModel.DataAnnotations.StringLength(4000, MinimumLength = 1)]
    public string Text { get; set; } = "";

    [System.ComponentModel.DataAnnotations.RegularExpression("^(en|vi)$")]
    public string Language { get; set; } = "en";
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
