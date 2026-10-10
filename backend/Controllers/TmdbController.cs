using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using mood_recommendation.Services;
namespace mood_recommendation.Controllers;
[ApiController,Route("api/Tmdb"),Authorize(Roles="Admin")]
public sealed class TmdbController(IHttpClientFactory clients,IConfiguration config):ControllerBase
{
    private async Task<IActionResult> Execute(Func<TmdbClient,Task<IActionResult>> run) {
        var service=new TmdbClient(clients.CreateClient("Tmdb"),config);
        if(!service.IsConfigured)return StatusCode(503,new {message="TMDB access token is not configured on the backend."});
        try{return await run(service);}
        catch(HttpRequestException e){return StatusCode(e.StatusCode==HttpStatusCode.TooManyRequests?429:e.StatusCode==HttpStatusCode.NotFound?404:502,new{message="TMDB request failed. Check the server token or try again later."});}
        catch(OperationCanceledException) when(!HttpContext.RequestAborted.IsCancellationRequested){return StatusCode(504,new{message="TMDB timed out. Try again."});}
        catch(Exception e) when(e is JsonException or InvalidOperationException or KeyNotFoundException){return StatusCode(502,new{message="TMDB returned unavailable or invalid movie data."});}
    }
    [HttpGet("search")]
    public Task<IActionResult> Search([FromQuery,Required,StringLength(200)] string query,[FromQuery,Range(1800,2100)] int? year,CancellationToken ct)
        =>Execute(async s=>string.IsNullOrWhiteSpace(query)?BadRequest():Ok(new{items=await s.Search(query.Trim(),year,ct)}));
    [HttpGet("lookup")]
    public Task<IActionResult> Lookup([FromQuery,Required,RegularExpression(@"^(tt[0-9]{7,10}|[1-9][0-9]{0,8})$")] string query,CancellationToken ct)
        =>Execute(async s=>await s.Lookup(query,ct) is { } movie?Ok(movie):NotFound(new{message="No matching movie found on TMDB."}));
}
