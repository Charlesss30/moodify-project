using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using mood_recommendation.Services;
public static class TmdbTests {
    public static async Task<int> Run() {
        var n=0;void Check(bool ok,string msg){if(!ok)throw new Exception(msg);n++;}
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"Tmdb:AccessToken","fixture"}}).Build();
        var handler=new Handler();using var http=new HttpClient(handler);var service=new TmdbClient(http,config);
        var movie=await service.Lookup("tt3896198",default);
        Check(movie is {TmdbId:"283995",RatingSource:"TMDB",Director:"James Gunn",Genre:"Sci-Fi, Action"},"TMDB metadata or canonical genre mapping failed.");
        Check(movie!.Rating==7.6m&&movie.TrailerUrl=="https://www.youtube.com/watch?v=12345678901","TMDB rating/trailer mapping failed.");
        Check(movie.Poster=="https://image.tmdb.org/t/p/w500/poster.jpg","TMDB poster mapping failed.");
        var found=await service.Search("Guardians & galaxy",2017,default);
        Check(found.Length==2&&found[1].Id==99,"Search must preserve choices instead of picking first.");
        Check(handler.LastQuery.Contains("primary_release_year=2017")&&handler.LastQuery.Contains("%26"),"Search query/year not encoded.");
        handler.Unrated=true;Check((await service.Lookup("283995",default))!.Rating==null,"No votes must not produce fake zero rating.");
        handler.Mismatch=true;Check(await service.Lookup("tt3896198",default)==null,"IMDb lookup accepted wrong movie.");
        handler.Mismatch=false;handler.Status=HttpStatusCode.TooManyRequests;
        try{await service.Lookup("283995",default);throw new Exception("Expected rate limit.");}catch(HttpRequestException e){Check(e.StatusCode==HttpStatusCode.TooManyRequests,"Lost provider status.");}
        return n;
    }
    public static async Task Live(string root) {
        var config=new ConfigurationBuilder().AddJsonFile(Path.Combine(root,"backend/appsettings.Development.json")).AddEnvironmentVariables().Build();
        using var http=new HttpClient{Timeout=TimeSpan.FromSeconds(25)};
        var service=new TmdbClient(http,config);
        var results=await service.Search("Guardians of the Galaxy Vol. 2",2017,default);
        var imdb=await service.Lookup("tt3896198",default);var numeric=await service.Lookup("283995",default);
        if(!results.Any(x=>x.Id==283995)||imdb?.TmdbId!="283995"||numeric?.ImdbId!="tt3896198"||imdb.Director!="James Gunn"||imdb.TrailerUrl==null||imdb.Poster==null)throw new Exception("Live TMDB metadata incomplete.");
        using var poster=await http.GetAsync(imdb.Poster);poster.EnsureSuccessStatusCode();
        Console.WriteLine("PASS: live TMDB search, IMDb/numeric lookup, director, poster and official trailer. No MongoDB writes.");
    }
    sealed class Handler:HttpMessageHandler {
        public string LastQuery="";public bool Unrated,Mismatch;public HttpStatusCode Status=HttpStatusCode.OK;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken ct) {
            if(r.Headers.Authorization?.ToString()!="Bearer fixture")throw new Exception("Token missing or sent incorrectly.");
            LastQuery=r.RequestUri!.PathAndQuery;
            var body=LastQuery.Contains("/find/")?"""{"movie_results":[{"id":283995}]}""":LastQuery.Contains("/search/")?"""{"results":[{"id":283995,"title":"Guardians","release_date":"2017-01-01"},{"id":99,"title":"Another","release_date":"2017-01-01"}]}""":
            """{"id":283995,"imdb_id":"IMDB","title":"Guardians","release_date":"2017-01-01","overview":"Overview","poster_path":"/poster.jpg","vote_average":7.6,"vote_count":VOTES,"genres":[{"name":"Science Fiction"},{"name":"Action"}],"credits":{"crew":[{"job":"Producer","name":"Other"},{"job":"Director","name":"James Gunn"}]},"videos":{"results":[{"site":"YouTube","type":"Trailer","official":true,"key":"12345678901"}]}}""".Replace("IMDB",Mismatch?"tt0000000":"tt3896198").Replace("VOTES",Unrated?"0":"200");
            return Task.FromResult(new HttpResponseMessage(Status){Content=new StringContent(body,Encoding.UTF8,"application/json")});
        }
    }
}
