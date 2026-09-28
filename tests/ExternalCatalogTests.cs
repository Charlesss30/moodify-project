using System.Net;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using MongoDB.Bson;
using MongoDB.Driver;
using mood_recommendation.Data;
using mood_recommendation.Models;
using mood_recommendation.Services;

public static class ExternalCatalogTests
{
    public static async Task<int> Run(MongoStore db)
    {
        var count=0;
        void Check(bool value,string message){if(!value)throw new Exception(message);count++;}
        using var memory=new MemoryCache(new MemoryCacheOptions());
        var transport=new MovieHandler();var music=new MusicClient();
        using var http=new HttpClient(transport);
        var catalog=new ExternalCatalog(db,new Factory(http),new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"Omdb:ApiKey","fixture"}}).Build(),music,memory);
        var genre=new TheLoai{TenTheLoai="Reference test genre",ContentType="Movie",DefaultValence=.4m,DefaultArousal=.6m};await db.Insert(genre);
        const string movieId="OMDB_tt1234567",songId="SPOTIFY_11dFghVXANMlKmJXsNCbNl";
        await catalog.References.InsertManyAsync([new(){Id=movieId},new(){Id=songId}]);
        try {
            var concurrent=await Task.WhenAll(Enumerable.Range(0,5).Select(_=>catalog.Get(movieId)));
            Check(transport.Calls==1,"Concurrent reference reads were not deduplicated.");
            var movie=concurrent[0]!;
            Check(movie.IsExternalReference&&movie.TieuDe=="Reference movie"&&movie.HinhAnh=="https://example.org/poster.jpg","Movie reference did not hydrate metadata.");
            Check(movie.MoodAvailable&&movie.Valence==.4m&&movie.Arousal==.6m,"Reference movie did not use genre configuration.");
            var song=await catalog.Get(songId);
            Check(song!.Nhac!.Source=="Spotify"&&song.HinhAnh!=null&&!song.MoodAvailable,"Song metadata or unknown-mood handling incorrect.");
            await catalog.Get(songId);Check(music.Calls==1,"Song metadata was not cached.");
            var stored=await db.Database.GetCollection<BsonDocument>("externalReferences").Find(new BsonDocument("_id",movieId)).FirstAsync();
            Check(stored.ElementCount==1&&stored.Contains("_id"),"Provider metadata leaked into MongoDB.");
            Check(await db.GetContent(movieId)==null,"Reference created a local content copy.");
            Check(await catalog.Count("Movie")==1&&await catalog.Count("Song")==1,"Reference counts are incorrect.");
            Check(await catalog.Get("OMDB_tt9999999")==null,"Unknown reference should not fetch metadata.");
            await catalog.Delete(movieId);
            transport.Fail=true;
            await catalog.References.InsertOneAsync(new(){Id=movieId});
            var unavailable=await catalog.Get(movieId);
            Check(unavailable!.MetadataStatus=="unavailable"&&!unavailable.MoodAvailable,"Provider failure should leave an identifiable unavailable item.");
        } finally {await catalog.Delete(movieId);await catalog.Delete(songId);await db.Delete(genre);}
        return count;
    }
    private sealed class Factory(HttpClient client):IHttpClientFactory {public HttpClient CreateClient(string name)=>client;}
    private sealed class MovieHandler:HttpMessageHandler
    {
        public int Calls;public bool Fail;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            Calls++;await Task.Delay(20,ct);
            if(Fail)return new(HttpStatusCode.ServiceUnavailable);
            return new(HttpStatusCode.OK){Content=new StringContent("""{"Response":"True","Type":"movie","imdbID":"tt1234567","Title":"Reference movie","Genre":"Reference test genre","Poster":"https://example.org/poster.jpg","Plot":"Short synopsis","imdbRating":"8.1"}""",Encoding.UTF8,"application/json")};
        }
    }
    private sealed class MusicClient:ISpotifyClient
    {
        public int Calls;public bool IsConfigured=>true;
        public Task<IReadOnlyList<SpotifyTrack>> Search(string query,int limit,int offset,CancellationToken ct)=>throw new NotSupportedException();
        public Task<SpotifyTrack> Info(string id,CancellationToken ct){Calls++;return Task.FromResult(new SpotifyTrack(id,"Reference song","Artist","https://i.scdn.co/image/fixture",180,"Album","2020","https://open.spotify.com/track/"+id,SpotifyClient.EmbedUrl(id)));}
    }
}
