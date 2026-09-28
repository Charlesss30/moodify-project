using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using MongoDB.Driver.Linq;
using mood_recommendation.Services;
using mood_recommendation.Data;
using mood_recommendation.Models;

public static class SpotifyTests
{
    public const string TrackId = "11dFghVXANMlKmJXsNCbNl";
    public static async Task<int> Run(MongoStore db)
    {
        var checks = 0;
        void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
            ["Spotify:ClientId"] = "test-client", ["Spotify:ClientSecret"] = "test-secret", ["Spotify:Market"] = "VN"
        }).Build();
        using var handler = new FakeHandler();
        using var http = new HttpClient(handler);
        var client = new SpotifyClient(http, config);
        var tracks = await client.Search("Song & test", 10, 0, default);
        Check(tracks.Count == 1 && handler.LastQuery!.Contains("limit=10"), "Search limit or deduplication failed.");
        var info = await client.Info(TrackId, default);
        Check(info.Title == "Fixture song" && info.Artists == "Fixture artist" && info.Duration == 180, "Metadata mapping failed.");
        Check(info.ReleaseDate == "2020-01" && info.ImageUrl == "https://i.scdn.co/image/test", "Release precision or artwork lost.");
        Check(info.EmbedUrl == SpotifyClient.EmbedUrl(TrackId), "Embed URL invalid.");
        Check(handler.Tokens == 1, "Token must be reused until expiry.");
        handler.UnauthorizedOnce = true;
        await client.Info(TrackId, default);
        Check(handler.Tokens == 2, "401 must refresh token once.");
        async Task Fails(Func<Task> action, string code, int status) {
            try { await action(); throw new Exception("Expected " + code); }
            catch(SpotifyException e) { Check(e.Code == code && e.StatusCode == status, "Wrong provider error mapping: " + e.Code); }
        }
        await Fails(async()=>{await client.Info("../bad", default);}, "invalid_id", 400);
        await Fails(async()=>{await client.Search("q", 20, 0, default);}, "invalid_search", 400);
        handler.Mode = "premium";
        await Fails(async()=>{await client.Search("test", 1, 0, default);}, "premium_required", 503);
        handler.Mode = "rate";
        try { await client.Info(TrackId,default); throw new Exception("Rate limit accepted"); }
        catch(SpotifyException e) { Check(e.StatusCode==429 && e.RetryAfter==12, "Retry-After lost."); }
        handler.Mode = "malformed";
        await Fails(async()=>{await client.Info(TrackId,default);}, "invalid_response", 502);
        handler.Mode = "missing";
        await Fails(async()=>{await client.Info(TrackId,default);}, "track_not_found", 404);
        handler.Mode = "";
        var empty = new SpotifyClient(http, new ConfigurationBuilder().Build());
        await Fails(async()=>{await empty.Info(TrackId,default);}, "not_configured", 503);

        var mood = new TamTrang { TenTamTrang = "Spotify test", MinValence = .2m, MaxValence = .8m, MinArousal = -.8m, MaxArousal = -.2m };
        await db.Insert(mood);
        var importer = new SpotifyImportService(db, client);
        var result = await importer.Import(TrackId, mood, "admin-selected", default);
        var content = await db.GetContent(result.ContentId!);
        Check(content!.Valence==.5m && content.Arousal==-.5m && content.LoaiNoiDung=="Song", "Mood mapping or type wrong.");
        Check(content.Nhac!.Source=="Spotify" && content.Nhac.ExternalId==TrackId && content.Nhac.Album=="Fixture album", "Source metadata not saved.");
        Check(content.Nhac.Energy==null && content.Nhac.Valence==null && content.Nhac.Genre==null, "Import fabricated unavailable features.");
        Check(content.Nhac.MoodSource=="admin-selected" && content.Nhac.LastSyncedAt!=null, "Missing provenance.");
        content!.Valence=-.9m;await db.SaveContent(content);
        var repeat=await importer.Import(TrackId,mood,"admin-selected",default);
        Check(repeat.Status=="existing" && await db.Nhacs.CountAsync(x=>x.Source=="Spotify"&&x.ExternalId==TrackId)==1, "Import created duplicate.");
        Check(content.Valence==-.9m, "Import overwrote reviewed mood.");
        var relinked = await importer.Import("33dFghVXANMlKmJXsNCbNl",mood,"admin-selected",default);
        Check(relinked.Status=="existing" && relinked.ContentId==result.ContentId, "Relinked Spotify track duplicated existing content.");
        handler.Mode="premium";
        await Fails(async()=>{await importer.Import("22dFghVXANMlKmJXsNCbNl",mood,"admin-selected",default);}, "premium_required",503);
        Check(!await db.Nhacs.AnyAsync(x=>x.ExternalId=="22dFghVXANMlKmJXsNCbNl"),"Failed provider call inserted data.");
        await db.DeleteContent(content!.NoiDungID);await db.Delete(mood);
        handler.Mode="";
        var genre=new TheLoai{TenTheLoai="Spotify genre fixture",ContentType="Song",DefaultValence=.25m,DefaultArousal=-.4m};await db.Insert(genre);
        var genreImport=await importer.Import(TrackId,null,"genre-default",default,genre);
        var genreContent=await db.GetContent(genreImport.ContentId!);
        Check(genreContent!.Valence==.25m && genreContent.Arousal==-.4m,"Spotify genre defaults incorrect.");
        Check(genreContent.Nhac!.TheLoaiID==genre.TheLoaiID && genreContent.Nhac.MoodSource=="genre-default" && genreContent.ReviewStatus=="pending","Spotify genre provenance missing.");
        Check(genreContent.Nhac.Valence==null,"Genre inference fabricated Spotify audio features.");
        await db.DeleteContent(genreContent.NoiDungID);await db.Delete(genre);
        return checks;
    }
    private sealed class FakeHandler : HttpMessageHandler
    {
        public int Tokens;
        public string Mode="";
        public bool UnauthorizedOnce;
        public string? LastQuery;
        private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) {Content=new StringContent(JsonSerializer.Serialize(value))};
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req,CancellationToken ct)
        {
            if(req.RequestUri!.Host=="accounts.spotify.com")
            {
                if(req.Headers.Authorization?.Scheme!="Basic" || await req.Content!.ReadAsStringAsync(ct)!="grant_type=client_credentials")
                    throw new Exception("Invalid client credentials request.");
                Tokens++;return Json(new {access_token="fixture-token-"+Tokens,expires_in=3600});
            }
            if(req.RequestUri.Host!="api.spotify.com"||req.Headers.Authorization?.Scheme!="Bearer")throw new Exception("Unsafe provider request.");
            LastQuery=req.RequestUri.Query;
            if(UnauthorizedOnce){UnauthorizedOnce=false;return new(HttpStatusCode.Unauthorized){Content=new StringContent("")};}
            if(Mode=="premium")return new(HttpStatusCode.Forbidden){Content=new StringContent("Active premium subscription required for the owner of the app.")};
            if(Mode=="rate"){var r=new HttpResponseMessage(HttpStatusCode.TooManyRequests){Content=new StringContent("")};r.Headers.RetryAfter=new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(12));return r;}
            if(Mode=="malformed")return new(HttpStatusCode.OK){Content=new StringContent("not JSON")};
            if(Mode=="missing")return new(HttpStatusCode.NotFound){Content=new StringContent("")};
            var track=new {id=TrackId,name="Fixture song",duration_ms=180999,artists=new[]{new{name="Fixture artist"}},
                album=new{name="Fixture album",release_date="2020-01",images=new[]{new{url="http://unsafe/image"},new{url="https://i.scdn.co/image/test"}}}};
            return req.RequestUri.AbsolutePath.EndsWith("/search")?Json(new{tracks=new{items=new[]{track,track}}}):Json(track);
        }
    }
}
