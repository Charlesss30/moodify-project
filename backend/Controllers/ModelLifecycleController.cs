using System.ComponentModel.DataAnnotations;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Driver;
using mood_recommendation.Data;
namespace mood_recommendation.Controllers;
[ApiController,Route("api/ModelLifecycle/{kind:regex(^(va|vi)$)}"),Authorize(Roles="AIEngineer")]
public sealed class ModelLifecycleController(IHttpClientFactory clients,MongoStore db,mood_recommendation.Services.ModelQualityService quality):ControllerBase
{
    [HttpGet]public async Task<IActionResult> State(string kind,CancellationToken ct)=>await Send(kind,null,null,ct);
    [HttpPost("{operation:regex(^(validate|activate|deactivate|rollback)$)}")]
    public async Task<IActionResult> Control(string kind,string operation,ModelLifecycleInput input,CancellationToken ct)
    {
        if(operation is "validate" or "activate" && string.IsNullOrEmpty(input.Version))
            return BadRequest(new{message="Choose an immutable stored model version."});
        return await Send(kind,operation,input,ct);
    }
    async Task<IActionResult> Send(string kind,string? action,ModelLifecycleInput? input,CancellationToken ct)
    {
        try{
            using var client=clients.CreateClient("VietnameseInference");
            string? expectedFingerprint=null;
            if(action is "activate" or "deactivate" or "rollback"){
                await quality.RememberBaselineAsync(kind,int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!),ct);
                if(action is "activate" or "rollback"){
                    var version=input!.Version;
                    if(action=="rollback"){
                        using var stateResponse=await client.GetAsync("models/"+kind+"/lifecycle",ct);
                        if(!stateResponse.IsSuccessStatusCode)return StatusCode(503,new{message="Lifecycle state unavailable."});
                        var state=await stateResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(cancellationToken:ct);
                        version=state.TryGetProperty("previous_version",out var previous)&&previous.ValueKind==System.Text.Json.JsonValueKind.String?previous.GetString():null;
                    }
                    if(string.IsNullOrEmpty(version))return Conflict(new{message="No candidate or previous model to activate."});
                    var gate=await quality.RequireAsync(kind,version,ct);
                    if(gate.StatusCode!=200)return StatusCode(gate.StatusCode,gate.Body);
                    expectedFingerprint=((mood_recommendation.Services.NlpFingerprint)gate.Body).BundleSha256;
                }
            }
            using var response=action is null?await client.GetAsync("models/"+kind+"/lifecycle",ct):
                await client.PostAsJsonAsync("models/"+kind+"/"+action,new{version=input!.Version,expected_revision=input.ExpectedRevision,expected_fingerprint=expectedFingerprint},ct);
            var content=await response.Content.ReadAsStringAsync(ct);
            if(action is not null)
                await db.Database.GetCollection<BsonDocument>("aiAudit").InsertOneAsync(new(){
                    ["_id"]=Guid.NewGuid().ToString("N"),["actorId"]=int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!),
                    ["actorRole"]="AIEngineer",["action"]=kind+"_"+action,["modelVersion"]=input!.Version??kind,
                    ["outcome"]=response.IsSuccessStatusCode?"succeeded":"failed",["createdAt"]=DateTime.UtcNow,
                    ["detail"]="Lifecycle HTTP "+(int)response.StatusCode},cancellationToken:ct);
            return ContentResult((int)response.StatusCode,content);
        }catch(HttpRequestException){return StatusCode(503,new{message="Model lifecycle service is unavailable."});}
        catch(OperationCanceledException)when(!ct.IsCancellationRequested){return StatusCode(503,new{message="Model lifecycle request timed out."});}
    }
    static ContentResult ContentResult(int code,string content)=>new(){StatusCode=code,ContentType="application/json",Content=content};
}
public sealed class ModelLifecycleInput
{
    [RegularExpression("^[A-Za-z0-9][A-Za-z0-9_-]{2,79}$")]public string? Version{get;set;}
    [Range(0,int.MaxValue)]public int ExpectedRevision{get;set;}
}
