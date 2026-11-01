using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using mood_recommendation.Services;
namespace mood_recommendation.Controllers;
[ApiController,Route("api/RecommendationExperiments"),Authorize(Roles="AIEngineer")]
public sealed class RecommendationExperimentsController(RecommendationStrategyService strategy):ControllerBase
{
    [HttpGet]public async Task<IActionResult> Report(CancellationToken ct)=>Ok(await strategy.ReportAsync(ct));
    [HttpPost]public async Task<IActionResult> Configure(ExperimentInput input,CancellationToken ct){
        var result=await strategy.ConfigureAsync(input,int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!),ct);
        return StatusCode(result.StatusCode,result.Body);
    }
}
