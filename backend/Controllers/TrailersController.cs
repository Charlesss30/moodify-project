using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using mood_recommendation.Services;
namespace mood_recommendation.Controllers;
[ApiController, Route("api/Trailers"), Authorize(Roles = "Admin")]
public sealed class TrailersController(IHttpClientFactory clients, IConfiguration config) : ControllerBase
{
    [HttpGet("lookup")]
    public async Task<IActionResult> Lookup([FromQuery, Required, RegularExpression(@"^tt[0-9]{7,10}$")] string imdbId) {
        var token = config["Tmdb:AccessToken"];
        if (string.IsNullOrWhiteSpace(token)) return Ok(new { status = "not_configured", message = "Configure Tmdb:AccessToken on the backend, or enter a trailer URL manually." });
        try {
            var url = await TrailerLookup.Find(clients.CreateClient("Tmdb"), token, imdbId, HttpContext.RequestAborted);
            return Ok(new { status = url is null ? "not_found" : "found", trailerUrl = url, source = "TMDB", imdbId });
        } catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or InvalidOperationException) {
            return StatusCode(502, new { message = "Trailer lookup unavailable. Try again or enter a URL manually." });
        }
    }
}
