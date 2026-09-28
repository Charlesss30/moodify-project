using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using MongoDB.Driver;
using mood_recommendation.Data;
using mood_recommendation.Models;

namespace mood_recommendation.Services;

public sealed class ExternalCatalog(MongoStore db, IHttpClientFactory clients, IConfiguration config, ISpotifyClient spotify, IMemoryCache cache)
{
    private readonly ConcurrentDictionary<string,SemaphoreSlim> locks = new();
    private readonly SemaphoreSlim slots = new(4);
    public IMongoCollection<ExternalReference> References => db.Database.GetCollection<ExternalReference>("externalReferences");
    public static string Prefix(string type) => type == "Movie" ? "OMDB_" : "SPOTIFY_";
    public Task<bool> Contains(string id) => References.Find(x=>x.Id==id).AnyAsync();
    public Task<long> Count(string type) => References.CountDocumentsAsync(x=>x.Id.StartsWith(Prefix(type)));
    public async Task<List<NoiDungGiaiTri>> List(string type, CancellationToken ct = default)
    {
        var prefix=Prefix(type);
        var rows=await References.Find(x=>x.Id.StartsWith(prefix)).SortBy(x=>x.Id).ToListAsync(ct);
        return (await Task.WhenAll(rows.Select(x=>Resolve(x.Id,ct)))).Where(x=>x!=null).Cast<NoiDungGiaiTri>().ToList();
    }
    public async Task<NoiDungGiaiTri?> Get(string id, CancellationToken ct = default) => await Contains(id) ? await Resolve(id,ct) : null;
    public async Task<bool> Delete(string id)
    {
        var result=await References.DeleteOneAsync(x=>x.Id==id);
        if(result.DeletedCount==0)return false;
        await db.Collection<DanhGia>().DeleteManyAsync(x=>x.NoiDungID==id);
        cache.Remove("external:"+id);
        return true;
    }
    private async Task<NoiDungGiaiTri> Resolve(string id, CancellationToken ct)
    {
        var key="external:"+id;
        if(cache.TryGetValue<NoiDungGiaiTri>(key,out var found))return found!;
        var gate=locks.GetOrAdd(id,_=>new SemaphoreSlim(1));
        await gate.WaitAsync(ct);
        try {
            if(cache.TryGetValue<NoiDungGiaiTri>(key,out found))return found!;
            await slots.WaitAsync(ct);
            var item=new NoiDungGiaiTri {NoiDungID=id,TieuDe=id,LoaiNoiDung=id.StartsWith("OMDB_")?"Movie":"Song",IsExternalReference=true,MoodAvailable=false,ReviewStatus="pending"};
            try {
                var source=item.LoaiNoiDung;
                if(cache.TryGetValue<string>("external-unavailable:"+source,out var unavailable))throw new InvalidOperationException(unavailable);
                if(source=="Movie") await LoadMovie(item,ct);
                else {
                    var track=await spotify.Info(id[8..],ct);
                    item.TieuDe=track.Title;item.HinhAnh=track.ImageUrl;
                    item.Nhac=new Nhac {NoiDungID=id,TenNgheSi=track.Artists,Album=track.Album,Duration=track.Duration,Source="Spotify",ExternalId=track.ExternalId,SourceUrl=track.SourceUrl,ReleaseDate=track.ReleaseDate,MoodSource="unassigned"};
                }
                item.MetadataStatus="available";
                cache.Set(key,item,TimeSpan.FromMinutes(30));
            } catch(SpotifyException e) {
                item.MetadataStatus="unavailable";
                if(e.StatusCode is 429 or 503)cache.Set("external-unavailable:Song",e.Message,TimeSpan.FromSeconds(e.RetryAfter??30));
                cache.Set(key,item,TimeSpan.FromSeconds(30));
            } catch(Exception e) when(e is HttpRequestException or JsonException or InvalidOperationException || e is OperationCanceledException && !ct.IsCancellationRequested) {
                item.MetadataStatus="unavailable";
                cache.Set(key,item,TimeSpan.FromSeconds(30));
            } finally {slots.Release();}
            return item;
        } finally {gate.Release();}
    }
    private async Task LoadMovie(NoiDungGiaiTri item,CancellationToken ct)
    {
        var apiKey=config["Omdb:ApiKey"];
        if(string.IsNullOrWhiteSpace(apiKey))throw new InvalidOperationException("OMDb is not configured.");
        var id=item.NoiDungID[5..];
        using var response=await clients.CreateClient("Omdb").GetAsync("https://www.omdbapi.com/?apikey="+Uri.EscapeDataString(apiKey)+"&i="+Uri.EscapeDataString(id)+"&plot=short&type=movie",ct);
        response.EnsureSuccessStatusCode();
        using var doc=JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var data=doc.RootElement;
        string? Text(string key)=>data.TryGetProperty(key,out var v)&&v.ValueKind==JsonValueKind.String&&v.GetString()!="N/A"?v.GetString():null;
        if(Text("Response")!="True"||Text("Type")!="movie"||Text("imdbID")!=id)throw new InvalidOperationException("Movie is unavailable.");
        item.TieuDe=Text("Title")??id;item.MoTa=Text("Plot");
        item.HinhAnh=Uri.TryCreate(Text("Poster"),UriKind.Absolute,out var poster)&&poster.Scheme=="https"?poster.AbsoluteUri:null;
        item.Phim=new Phim {NoiDungID=item.NoiDungID,IMDBID=id,TheLoai=Text("Genre"),DiemDanhGiaTB=decimal.TryParse(Text("imdbRating"),NumberStyles.Number,CultureInfo.InvariantCulture,out var rating)?rating:null,MoodSource="unassigned"};
        var suggestion=await new GenreMoodService(db).Suggest(item.Phim.TheLoai,"Movie");
        if(suggestion.Valence.HasValue){item.Valence=suggestion.Valence.Value;item.Arousal=suggestion.Arousal!.Value;item.MoodAvailable=true;item.Phim.MoodSource="genre-default";item.MoodRuleVersion=suggestion.RuleVersion;}
    }
}
