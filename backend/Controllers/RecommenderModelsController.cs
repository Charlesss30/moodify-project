using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using mood_recommendation.Services;
namespace mood_recommendation.Controllers;
[ApiController,Route("api/RecommenderModels"),Authorize(Roles="AIEngineer")]
public sealed class RecommenderModelsController(MatrixFactorizationService mf):ControllerBase
{
    int Actor=>int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    [HttpGet]public async Task<IActionResult> List(CancellationToken ct)=>Ok(new{
        state=await mf.StateAsync(ct),models=(await mf.Models.Find(_=>true).SortByDescending(x=>x.CreatedAt).Limit(100).ToListAsync(ct)).Select(MatrixFactorizationService.Summary)});
    [HttpPost,RequestSizeLimit(9_000_000)]public async Task<IActionResult> Register(MfUpload input,CancellationToken ct)
    {var r=await mf.RegisterAsync(input.Model,input.Source,input.Reviewed,Actor,ct);return StatusCode(r.StatusCode,r.Body);}
    [HttpPost("{version}/activate")]public async Task<IActionResult> Activate(string version,RevisionInput input,CancellationToken ct)
    {var r=await mf.SwitchAsync(version,input.ExpectedRevision,Actor,ct);return StatusCode(r.StatusCode,r.Body);}
    [HttpPost("deactivate")]public async Task<IActionResult> Deactivate(RevisionInput input,CancellationToken ct)
    {var r=await mf.SwitchAsync(null,input.ExpectedRevision,Actor,ct);return StatusCode(r.StatusCode,r.Body);}
    [HttpPost("rollback")]public async Task<IActionResult> Rollback(RevisionInput input,CancellationToken ct)
    {
        var state=await mf.StateAsync(ct);
        if(state.PreviousVersion is null)return Conflict(new{message="No previous MF artifact. Deactivate to restore neighbor CF."});
        var r=await mf.SwitchAsync(state.PreviousVersion,input.ExpectedRevision,Actor,ct);return StatusCode(r.StatusCode,r.Body);
    }
}
public sealed class MfUpload {
    [Required]public JsonElement Model{get;set;}
    [Required,StringLength(1000,MinimumLength=10)]public string Source{get;set;}="";
    public bool Reviewed{get;set;}
}
