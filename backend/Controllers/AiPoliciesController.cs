using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Driver;
using mood_recommendation.Data;
using mood_recommendation.Services;
namespace mood_recommendation.Controllers;
[ApiController,Route("api/AiPolicies"),Authorize(Roles="AIEngineer")]
public sealed class AiPoliciesController(BilingualPolicyService policies,MongoStore db):ControllerBase
{
    int Actor=>int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    [HttpGet] public async Task<IActionResult> List(CancellationToken ct)=>Ok(new{
        active=new[]{await policies.ActiveAsync("vi",ct),await policies.ActiveAsync("en",ct)},
        candidates=await policies.Policies.Find(_=>true).SortByDescending(x=>x.CreatedAt).Limit(100).ToListAsync(ct)});
    [HttpPost] public async Task<IActionResult> Draft(BilingualPolicyInput input,CancellationToken ct)
    {var r=await policies.DraftAsync(input,Actor,ct);return StatusCode(r.StatusCode,r.Body);}
    [HttpPost("{version}/activate")] public async Task<IActionResult> Activate(string version,RevisionInput input,CancellationToken ct)
    {var r=await policies.ActivateAsync(version,input.ExpectedRevision,Actor,ct);return StatusCode(r.StatusCode,r.Body);}
    [HttpPost("{language:regex(^(vi|en)$)}/reset")] public async Task<IActionResult> Reset(string language,RevisionInput input,CancellationToken ct)
    {var r=await policies.ResetAsync(language,input.ExpectedRevision,Actor,ct);return StatusCode(r.StatusCode,r.Body);}
    [HttpGet("metrics")] public async Task<IActionResult> Metrics(CancellationToken ct)
    {
        var rows=await db.Database.GetCollection<BsonDocument>("aiInferenceMetrics").Find(_=>true)
            .Sort(new BsonDocument("day",-1)).Limit(500).ToListAsync(ct);
        return Ok(rows.Select(x=>new{day=x["day"].AsString,language=x["language"].AsString,modelVersion=x["modelVersion"].AsString,
            mappingVersion=x["mappingVersion"].AsString,status=x["status"].AsString,reason=x["reason"].AsString,
            count=x["count"].ToInt64(),accepted=x["accepted"].ToInt64(),
            meanMs=x["totalMs"].ToDouble()/x["count"].ToInt64(),maxMs=x["maxMs"].ToDouble()}));
    }
}
