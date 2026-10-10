using mood_recommendation.Models;
using mood_recommendation.Services;
using System.Text.Json;
public static class FeatureTests {
    public static bool SameRanking(System.Text.Json.Nodes.JsonNode? left, System.Text.Json.Nodes.JsonNode? right) {
        System.Text.Json.Nodes.JsonNode? WithoutSession(System.Text.Json.Nodes.JsonNode? node) {
            var copy=node?.DeepClone();
            if(copy is System.Text.Json.Nodes.JsonArray rows)
                foreach(var row in rows) row?["content"]?.AsObject().Remove("recommendationId");
            return copy;
        }
        return System.Text.Json.Nodes.JsonNode.DeepEquals(WithoutSession(left),WithoutSession(right));
    }
    public static async Task<int> Run() {
        int count=0;
        void Check(bool pass,string msg){if(!pass)throw new Exception(msg);count++;}
        NoiDungGiaiTri Item(string id,string genre) => new(){NoiDungID=id,LoaiNoiDung="Movie",Valence=0,Arousal=0,Phim=new(){TheLoai=genre}};
        var library=new[]{Item("a","Comedy"),Item("b","Horror"),Item("c","Comedy"),Item("d","Drama")};
        DanhGia Rate(int user,string id,int stars,int seq) => new(){TaiKhoanID=user,NoiDungID=id,SoSao=stars,DanhGiaID=seq,ThoiGian=new DateTime(2026,1,1).AddMinutes(seq)};
        var ratings=new[]{Rate(1,"a",5,1),Rate(1,"b",1,2),Rate(2,"a",5,3),Rate(2,"b",1,4),Rate(2,"c",5,5)};
        var ranked=HybridRecommender.Rank(library,library,ratings,new HashSet<string>(),1,0,0,"hybrid");
        Check(ranked.Single(x=>x.Content.NoiDungID=="c").Score > ranked.Single(x=>x.Content.NoiDungID=="d").Score,"Hybrid should favor compatible neighbor and genre.");
        Check(ranked.Single(x=>x.Content.NoiDungID=="c").CollaborativeScore==1,"Collaborative prediction missing.");
        Check(ranked.All(x=>x.Score>=0&&x.Score<=1),"Unbounded scores.");
        Check(ranked.Last().Content.NoiDungID=="b","Disliked content not demoted.");
        var cold=HybridRecommender.Rank(library,library,ratings,new HashSet<string>(),99,0,0,"hybrid");
        Check(cold.All(x=>x.ContentScore==null&&x.CollaborativeScore==null),"Cold start fabricated evidence.");
        Check(cold.Select(x=>x.Content.NoiDungID).SequenceEqual(new[]{"a","b","c","d"}),"Cold start must retain deterministic mood order.");
        var updated=HybridRecommender.Rank(library,library,new[]{Rate(1,"a",5,1),Rate(1,"a",1,10)},new HashSet<string>(),1,0,0,"content");
        Check(updated.Single(x=>x.Content.NoiDungID=="c").ContentScore==0,"Latest rating did not replace previous feedback.");
        var oneOverlap=HybridRecommender.Rank(library,library,new[]{Rate(1,"a",5,1),Rate(2,"a",5,2),Rate(2,"c",5,3)},new HashSet<string>(),1,0,0,"hybrid");
        Check(oneOverlap.All(x=>x.CollaborativeScore==null),"One shared item must not create CF confidence.");
        Check(TrailerLookup.ValidUrl("https://youtu.be/12345678901"),"Valid short trailer rejected.");
        foreach(var url in new[]{"http://youtu.be/12345678901","https://youtube.com.evil.test/watch?v=12345678901","https://user@youtube.com/watch?v=12345678901","https://youtube.com/watch?v=short","https://youtube.com/watch?v=12345678901&v=12345678902"}) Check(!TrailerLookup.ValidUrl(url),"Unsafe trailer accepted.");
        using var videos=JsonDocument.Parse("""{"results":[{"site":"YouTube","type":"Trailer","official":false,"key":"00000000000"},{"site":"YouTube","type":"Trailer","official":true,"key":"12345678901"}]}""");
        Check(TrailerLookup.Select(videos.RootElement)=="https://www.youtube.com/watch?v=12345678901","Unofficial video selected.");
        var handler=new TrailerHandler();
        using var client=new HttpClient(handler);
        Check(await TrailerLookup.Find(client,"fixture-token","tt3896198",CancellationToken.None)=="https://www.youtube.com/watch?v=12345678901","IMDb trailer lookup failed.");
        Check(handler.Paths.SequenceEqual(new[]{"/3/find/tt3896198?external_source=imdb_id","/3/movie/42/videos"}),"Lookup did not resolve by exact IMDb ID.");
        handler.Ambiguous=true;handler.Paths.Clear();
        Check(await TrailerLookup.Find(client,"fixture-token","tt3896198",CancellationToken.None)==null&&handler.Paths.Count==1,"Ambiguous movie must not auto-fill a trailer.");
        return count;
    }
}

sealed class TrailerHandler : HttpMessageHandler {
    public bool Ambiguous;
    public List<string> Paths=new();
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct) {
        if(request.Headers.Authorization?.Scheme!="Bearer")throw new Exception("Missing TMDB authentication.");
        Paths.Add(request.RequestUri!.PathAndQuery);
        var body=request.RequestUri.AbsolutePath.Contains("/find/")
            ? (Ambiguous ? "{\"movie_results\":[{\"id\":42},{\"id\":43}]}" : "{\"movie_results\":[{\"id\":42}]}")
            : "{\"results\":[{\"site\":\"YouTube\",\"type\":\"Trailer\",\"official\":true,\"key\":\"12345678901\"}]}";
        return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK){Content=new StringContent(body)});
    }
}
