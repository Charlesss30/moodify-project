using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace mood_recommendation.Services;
public static class TrailerLookup
{
    public static bool ValidUrl(string? value) {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var u) || u.Scheme != "https" || !u.IsDefaultPort || u.UserInfo.Length > 0) return false;
        if (u.Host == "youtu.be") return Regex.IsMatch(u.AbsolutePath, @"^/[A-Za-z0-9_-]{11}$");
        return (u.Host is "youtube.com" or "www.youtube.com") && u.AbsolutePath == "/watch"
            && Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(u.Query).TryGetValue("v", out var id)
            && id.Count == 1 && Regex.IsMatch(id.ToString(), @"^[A-Za-z0-9_-]{11}$");
    }
    public static string? Select(JsonElement response) {
        if (!response.TryGetProperty("results", out var videos) || videos.ValueKind != JsonValueKind.Array) return null;
        foreach (var v in videos.EnumerateArray()) {
            if (!v.TryGetProperty("site", out var site) || site.GetString() != "YouTube"
                || !v.TryGetProperty("type", out var type) || type.GetString() != "Trailer"
                || !v.TryGetProperty("official", out var official) || official.ValueKind != JsonValueKind.True
                || !v.TryGetProperty("key", out var key) || key.ValueKind != JsonValueKind.String) continue;
            var url = "https://www.youtube.com/watch?v=" + key.GetString();
            if (ValidUrl(url)) return url;
        }
        return null;
    }
    public static async Task<string?> Find(HttpClient client, string token, string imdbId, CancellationToken ct) {
        async Task<JsonDocument> Get(string path) {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("https://api.themoviedb.org/3/" + path));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await client.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        }
        using var matches = await Get("find/" + Uri.EscapeDataString(imdbId) + "?external_source=imdb_id");
        if (!matches.RootElement.TryGetProperty("movie_results", out var movies) || movies.GetArrayLength() != 1) return null;
        var id = movies[0].GetProperty("id").GetInt32();
        using var videos = await Get("movie/" + id + "/videos");
        return Select(videos.RootElement);
    }
}
