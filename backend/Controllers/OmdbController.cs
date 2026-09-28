using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace mood_recommendation.Controllers;

[ApiController, Route("api/Omdb"), Authorize(Roles = "Admin")]
public sealed class OmdbController(IHttpClientFactory clients, IConfiguration config) : ControllerBase
{
    [HttpGet("lookup")]
    public async Task<IActionResult> Lookup([FromQuery, Required, StringLength(200)] string query,
        [FromQuery, Range(1800, 2100)] int? year, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query)) return BadRequest(new { message = "Enter a movie title or IMDb ID." });
        var key = config["Omdb:ApiKey"];
        if (string.IsNullOrWhiteSpace(key)) return StatusCode(503, new { message = "The OMDb API key is not configured on the backend." });
        var q = query.Trim();
        var parameter = Regex.IsMatch(q, "^tt[0-9]{7,10}$") ? "i" : "t";
        var url = "https://www.omdbapi.com/?apikey=" + Uri.EscapeDataString(key) +
            "&type=movie&plot=full&" + parameter + "=" + Uri.EscapeDataString(q) + (year.HasValue ? "&y=" + year.Value : "");
        try
        {
            using var response = await clients.CreateClient("Omdb").GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
                return StatusCode(response.StatusCode == System.Net.HttpStatusCode.TooManyRequests ? 429 : 502,
                    new { message = "OMDb is unavailable or its quota has been reached. Please try again later." });
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var data = doc.RootElement;
            string? Read(string name) => data.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String &&
                v.GetString() is string s && s != "N/A" ? s : null;
            if (Read("Response") != "True")
            {
                var error = Read("Error") ?? "";
                if (error.Contains("not found", StringComparison.OrdinalIgnoreCase))
                    return NotFound(new { message = "Movie not found. Try the original title, release year or IMDb ID." });
                return StatusCode(503, new { message = "OMDb rejected the request. Check API key activation and usage limits." });
            }
            if (Read("Type") != "movie") return UnprocessableEntity(new { message = "This result is not a movie." });
            var poster = Read("Poster");
            if (!Uri.TryCreate(poster, UriKind.Absolute, out var image) || image.Scheme != "https") poster = null;
            decimal? rating = decimal.TryParse(Read("imdbRating"), NumberStyles.Number, CultureInfo.InvariantCulture, out var score)
                && score >= 0 && score <= 10 ? score : null;
            return Ok(new { title = Read("Title"), genre = Read("Genre"), description = Read("Plot"),
                poster, rating, imdbId = Read("imdbID"), year = Read("Year"), source = "OMDb" });
        }
        catch (HttpRequestException) { return StatusCode(502, new { message = "Unable to connect to OMDb." }); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return StatusCode(504, new { message = "OMDb timed out. Please try again." }); }
        catch (JsonException) { return StatusCode(502, new { message = "OMDb returned invalid data." }); }
    }
}
