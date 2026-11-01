using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using System.Security.Claims;
using mood_recommendation.Services;

namespace mood_recommendation.Controllers;

[ApiController,Route("api/ModelQuality"),Authorize(Roles="AIEngineer")]
public sealed class ModelQualityController(ModelQualityService quality):ControllerBase {
    int Actor=>int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    [HttpGet]public async Task<IActionResult> List(CancellationToken ct)=>Ok(new{
        policies=await quality.Policies.Find(_=>true).SortByDescending(p=>p.CreatedAt).Limit(100).ToListAsync(ct),
        approvals=await quality.Approvals.Find(_=>true).SortByDescending(a=>a.CreatedAt).Limit(100).ToListAsync(ct),
        evidenceScope="Server checks policy/sample/metric thresholds and actual artifact identity. Offline predictions/labels remain subject to accountable engineer review.",
        baselineScope="An existing serving baseline may be restored without claiming independently validated quality."});
    [HttpPost("policies")]public async Task<IActionResult> Policy(NlpQualityPolicyInput input,CancellationToken ct) {
        var result=await quality.CreatePolicyAsync(input,Actor,ct);return StatusCode(result.StatusCode,result.Body);
    }
    [HttpPost("approvals")]public async Task<IActionResult> Approve(NlpQualityApprovalInput input,CancellationToken ct) {
        var result=await quality.ApproveAsync(input,Actor,ct);return StatusCode(result.StatusCode,result.Body);
    }
}