using Microsoft.Extensions.Caching.Memory;
using mood_recommendation.Models;

namespace mood_recommendation.Services;

/// Enrich old catalog details without rewriting reviewed mood coordinates or DB rows.
public sealed class MovieMetadataService(IHttpClientFactory clients, IConfiguration config, IMemoryCache cache)
{
    public async Task EnrichAsync(NoiDungGiaiTri content, CancellationToken ct)
    {
        var movie = content.Phim;
        if (movie is null || (movie.ReleaseYear.HasValue && movie.RuntimeMinutes.HasValue && movie.Cast is { Length: > 0 })) return;
        var service = new TmdbClient(clients.CreateClient("Tmdb"), config);
        var id = !string.IsNullOrWhiteSpace(movie.TMDBID) ? movie.TMDBID : movie.IMDBID;
        if (!service.IsConfigured || string.IsNullOrWhiteSpace(id)) return;
        if (!System.Text.RegularExpressions.Regex.IsMatch(id, @"^(tt[0-9]{7,10}|[1-9][0-9]{0,8})$")) return;
        var key = "movie-details:" + id;
        if (!cache.TryGetValue<TmdbMovie>(key, out var details))
        {
            // Cache failed attempts briefly to avoid repeated requests on every reopen.
            if (cache.TryGetValue<bool>(key + ":unavailable", out _)) return;
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(3));
                details = await service.Lookup(id, timeout.Token);
                if (details is not null) cache.Set(key, details, TimeSpan.FromHours(6));
                else cache.Set(key + ":unavailable", true, TimeSpan.FromMinutes(10));
            }
            catch (Exception error) when (error is HttpRequestException or System.Text.Json.JsonException
                or InvalidOperationException or KeyNotFoundException
                || error is OperationCanceledException && !ct.IsCancellationRequested)
            {
                cache.Set(key + ":unavailable", true, TimeSpan.FromMinutes(2));
                return;
            }
        }
        if (details is null) return;
        movie.ReleaseYear ??= int.TryParse(details.Year, out var year) && year is >= 1800 and <= 2100 ? year : null;
        movie.RuntimeMinutes ??= details.RuntimeMinutes;
        if (movie.Cast is not { Length: > 0 }) movie.Cast = details.Cast ?? [];
    }
}
