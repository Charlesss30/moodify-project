using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace mood_recommendation.Services;
public sealed record TmdbMovie(string Title,string? Director,string? Genre,string? Description,string? Poster,decimal? Rating,string? ImdbId,string Year,string TmdbId,string? TrailerUrl,string Source="TMDB",string RatingSource="TMDB",string[]? Cast=null,int? RuntimeMinutes=null);
public sealed record TmdbSearchItem(int Id,string Title,string Year,string? Poster);
public sealed class TmdbClient(HttpClient client,IConfiguration config)
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(config["Tmdb:AccessToken"]);
    private async Task<JsonDocument> Get(string path,CancellationToken ct) {
        if(!IsConfigured)throw new InvalidOperationException("TMDB is not configured.");
        using var request=new HttpRequestMessage(HttpMethod.Get,"https://api.themoviedb.org/3/"+path);
        request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",config["Tmdb:AccessToken"]);
        using var response=await client.SendAsync(request,ct);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
    }
    private static string? Text(JsonElement d,string name)=>d.TryGetProperty(name,out var v)&&v.ValueKind==JsonValueKind.String?v.GetString():null;
    private static string Year(JsonElement d)=>(Text(d,"release_date")??"").Split('-')[0];
    private static string? Poster(JsonElement d) {
        var path=Text(d,"poster_path");
        return path is not null&&Regex.IsMatch(path,@"^/[A-Za-z0-9_\-]+\.(jpg|png|webp)$")?"https://image.tmdb.org/t/p/w500"+path:null;
    }
    public async Task<TmdbSearchItem[]> Search(string query,int? year,CancellationToken ct) {
        using var doc=await Get("search/movie?include_adult=false&language=en-US&query="+Uri.EscapeDataString(query)+(year.HasValue?"&primary_release_year="+year:""),ct);
        return doc.RootElement.GetProperty("results").EnumerateArray().Take(20)
            .Select(d=>new TmdbSearchItem(d.GetProperty("id").GetInt32(),Text(d,"title")??"",Year(d),Poster(d))).ToArray();
    }
    public async Task<TmdbMovie?> Lookup(string query,CancellationToken ct) {
        int id;
        if(Regex.IsMatch(query,@"^tt[0-9]{7,10}$")) {
            using var found=await Get("find/"+query+"?external_source=imdb_id",ct);
            var rows=found.RootElement.GetProperty("movie_results");
            if(rows.GetArrayLength()!=1)return null;
            id=rows[0].GetProperty("id").GetInt32();
        } else if(!int.TryParse(query,out id)||id<=0) throw new ArgumentException("Enter an IMDb ID or numeric TMDB ID.");
        using var doc=await Get("movie/"+id+"?language=en-US&append_to_response=credits,videos",ct);
        var d=doc.RootElement;
        if(d.GetProperty("id").GetInt32()!=id)throw new JsonException("Mismatched movie ID.");
        var imdb=Text(d,"imdb_id");
        if(query.StartsWith("tt")&&imdb!=query)return null;
        var directors=d.TryGetProperty("credits",out var credits)&&credits.TryGetProperty("crew",out var crew)
            ?string.Join(", ",crew.EnumerateArray().Where(c=>Text(c,"job")=="Director").Select(c=>Text(c,"name")).Where(x=>!string.IsNullOrWhiteSpace(x)).Distinct()):null;
        // Match the existing local genre defaults; do not rename the user's genres.
        var genres=string.Join(", ",d.GetProperty("genres").EnumerateArray().Select(g=>Text(g,"name")=="Science Fiction"?"Sci-Fi":Text(g,"name")));
        decimal? rating=d.TryGetProperty("vote_count",out var votes)&&votes.GetInt32()>0&&d.TryGetProperty("vote_average",out var score)&&score.TryGetDecimal(out var number)&&number>=0&&number<=10?number:null;
        var cast = d.TryGetProperty("credits", out var creditData) && creditData.TryGetProperty("cast", out var castData)
            ? castData.EnumerateArray().Select(c => Text(c, "name")?.Trim())
                .Where(n => !string.IsNullOrWhiteSpace(n) && n.Length <= 120)
                .Select(n => n!).Distinct(StringComparer.OrdinalIgnoreCase).Take(20).ToArray() : [];
        int? runtime = d.TryGetProperty("runtime", out var runtimeData)
            && runtimeData.ValueKind == JsonValueKind.Number && runtimeData.TryGetInt32(out var minutes) && minutes is > 0 and <= 1000 ? minutes : null;
        return new(Text(d,"title")??"",directors,genres,Text(d,"overview"),Poster(d),rating,imdb,Year(d),id.ToString(),d.TryGetProperty("videos",out var videos)?TrailerLookup.Select(videos):null, Cast:cast, RuntimeMinutes:runtime);
    }
}
