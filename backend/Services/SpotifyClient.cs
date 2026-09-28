using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace mood_recommendation.Services;

public sealed record SpotifyTrack(string ExternalId, string Title, string Artists, string? ImageUrl,
    int Duration, string? Album, string? ReleaseDate, string SourceUrl, string EmbedUrl);
public sealed class SpotifyException(int statusCode, string code, string message, int? retryAfter = null) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
    public int? RetryAfter { get; } = retryAfter;
}
public interface ISpotifyClient
{
    bool IsConfigured { get; }
    Task<IReadOnlyList<SpotifyTrack>> Search(string query, int limit, int offset, CancellationToken ct);
    Task<SpotifyTrack> Info(string id, CancellationToken ct);
}
public sealed class SpotifyClient(HttpClient http, IConfiguration config) : ISpotifyClient
{
    private readonly SemaphoreSlim tokenLock = new(1, 1);
    private string? token;
    private DateTimeOffset expires;
    public bool IsConfigured => !string.IsNullOrWhiteSpace(config["Spotify:ClientId"]) &&
        !string.IsNullOrWhiteSpace(config["Spotify:ClientSecret"]);
    public static bool ValidId(string? id) => id != null && Regex.IsMatch(id, "^[a-zA-Z0-9]{22}$");
    public static string EmbedUrl(string id) => "https://open.spotify.com/embed/track/" + id;
    private string Market => Regex.IsMatch(config["Spotify:Market"] ?? "", "^[A-Z]{2}$") ? config["Spotify:Market"]! : "VN";

    private async Task<string> Token(CancellationToken ct)
    {
        if (!IsConfigured) throw new SpotifyException(503, "not_configured", "Spotify Client ID and secret are not configured on the backend.");
        await tokenLock.WaitAsync(ct);
        try
        {
            if (token != null && expires > DateTimeOffset.UtcNow) return token;
            using var req = new HttpRequestMessage(HttpMethod.Post, "https://accounts.spotify.com/api/token");
            req.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes(config["Spotify:ClientId"] + ":" + config["Spotify:ClientSecret"])));
            req.Content = new FormUrlEncodedContent(new Dictionary<string,string> { ["grant_type"] = "client_credentials" });
            using var res = await http.SendAsync(req, ct);
            if (!res.IsSuccessStatusCode) await Fail(res, ct, true);
            using var json = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
            token = json.RootElement.GetProperty("access_token").GetString();
            if (string.IsNullOrWhiteSpace(token)) throw new JsonException();
            expires = DateTimeOffset.UtcNow.AddSeconds(Math.Max(0, json.RootElement.GetProperty("expires_in").GetInt32() - 60));
            return token;
        }
        finally { tokenLock.Release(); }
    }
    private static async Task Fail(HttpResponseMessage res, CancellationToken ct, bool auth = false)
    {
        var body = await res.Content.ReadAsStringAsync(ct);
        if (res.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var seconds = res.Headers.RetryAfter?.Delta?.TotalSeconds ??
                (res.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow)?.TotalSeconds ?? 30;
            throw new SpotifyException(429, "rate_limited", "Spotify rate limit reached. Please try again later.", (int)Math.Clamp(seconds, 1, 86400));
        }
        if (body.Contains("premium", StringComparison.OrdinalIgnoreCase))
            throw new SpotifyException(503, "premium_required", "Spotify requires an active Premium subscription for the app owner. Activation may take a few hours.");
        if (auth) throw new SpotifyException(503, "credentials_rejected", "Spotify rejected the app credentials. Check the backend Client ID and secret.");
        if (res.StatusCode == HttpStatusCode.Forbidden)
            throw new SpotifyException(503, "access_denied", "Spotify has not granted catalog access to this app. Check its permissions and status in the Spotify Developer Dashboard.");
        if (res.StatusCode == HttpStatusCode.NotFound) throw new SpotifyException(404, "track_not_found", "Song not found on Spotify.");
        throw new SpotifyException(502, "provider_error", "Spotify did not return valid data. Please try again.");
    }
    private async Task<JsonElement> Get(string path, CancellationToken ct)
    {
        try
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var bearer = await Token(ct);
                using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.spotify.com/v1/" + path);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
                using var res = await http.SendAsync(req, ct);
                if (res.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
                {
                    await tokenLock.WaitAsync(ct);
                    try { if (token == bearer) { token = null; expires = default; } }
                    finally { tokenLock.Release(); }
                    continue;
                }
                if (!res.IsSuccessStatusCode) await Fail(res, ct);
                using var json = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
                return json.RootElement.Clone();
            }
            throw new SpotifyException(502, "provider_auth", "Spotify rejected the app token.");
        }
        catch (HttpRequestException) { throw new SpotifyException(502, "network_error", "Unable to connect to Spotify."); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new SpotifyException(504, "timeout", "Spotify timed out. Please try again."); }
        catch (JsonException) { throw new SpotifyException(502, "invalid_response", "Spotify returned invalid data."); }
    }
    private static string? Text(JsonElement e, string key) =>
        e.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
    private static SpotifyTrack Parse(JsonElement t)
    {
        var id = Text(t, "id");
        var title = Text(t, "name");
        if (!ValidId(id) || string.IsNullOrWhiteSpace(title)) throw new SpotifyException(502, "invalid_track", "Spotify returned a track without an ID or title.");
        var artists = t.TryGetProperty("artists", out var a) && a.ValueKind == JsonValueKind.Array
            ? string.Join(", ", a.EnumerateArray().Select(x => Text(x, "name")).Where(x => x != null)) : "";
        string? album = null, release = null, image = null;
        if (t.TryGetProperty("album", out var al) && al.ValueKind == JsonValueKind.Object)
        {
            album = Text(al, "name"); release = Text(al, "release_date");
            if (al.TryGetProperty("images", out var imgs) && imgs.ValueKind == JsonValueKind.Array)
                image = imgs.EnumerateArray().Select(x => Text(x, "url")).FirstOrDefault(x =>
                    Uri.TryCreate(x, UriKind.Absolute, out var u) && u.Scheme == "https" && u.Host == "i.scdn.co");
        }
        var duration = t.TryGetProperty("duration_ms", out var d) && d.TryGetInt32(out var ms) ? Math.Max(0, ms / 1000) : 0;
        return new(id!, title!, artists, image, duration, album, release, "https://open.spotify.com/track/" + id, EmbedUrl(id!));
    }
    public async Task<IReadOnlyList<SpotifyTrack>> Search(string query, int limit, int offset, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length > 200 || limit < 1 || limit > 10 || offset < 0 || offset > 1000)
            throw new SpotifyException(400, "invalid_search", "Enter up to 200 characters. Each page supports up to 10 results.");
        var data = await Get($"search?type=track&q={Uri.EscapeDataString(query.Trim())}&market={Market}&limit={limit}&offset={offset}", ct);
        if (!data.TryGetProperty("tracks", out var tracks) || !tracks.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            throw new SpotifyException(502, "invalid_response", "Spotify returned an invalid list.");
        return items.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object).Select(Parse).DistinctBy(x => x.ExternalId).ToArray();
    }
    public async Task<SpotifyTrack> Info(string id, CancellationToken ct)
    {
        if (!ValidId(id)) throw new SpotifyException(400, "invalid_id", "Spotify track IDs must contain 22 letters or digits.");
        return Parse(await Get("tracks/" + id + "?market=" + Market, ct));
    }
}
