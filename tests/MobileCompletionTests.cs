using System.Text.Json.Nodes;
using MongoDB.Driver;
using mood_recommendation.Data;
using mood_recommendation.Models;

public static class MobileCompletionTests
{
    public static async Task Run(MongoStore db,
        Func<string,string,object?,int,string?,Task<JsonNode?>> call,
        Action<bool,string> check, int userId, string user, string other, string admin)
    {
        await call("GET", "/Statistics?year=1999", null, 400, user);
        await call("GET", "/Statistics?year=" + (DateTime.UtcNow.Year + 1), null, 400, user);
        foreach (var (time, value) in new[] {
            (new DateTime(2023,12,31,20,0,0,DateTimeKind.Utc), .1m),
            (new DateTime(2024,2,1,12,0,0,DateTimeKind.Utc), .8m),
            (new DateTime(2024,2,29,12,0,0,DateTimeKind.Utc), -.4m)
        }) await db.Insert(new LichSuTamTrang {
            TaiKhoanID=userId,TamTrangID=9901,ThoiGian=time,Valence=value,Arousal=-.2m
        });
        var year = await call("GET","/Statistics?year=2024&offsetMinutes=420",null,200,user);
        var months = year!["trend"]!.AsArray();
        check(year["days"]!.GetValue<int>() == 366, "Leap-year statistics lost a day.");
        check(year["granularity"]!.GetValue<string>() == "month" && months.Count == 12, "Year must have monthly buckets.");
        check(months[0]!["moodCount"]!.GetValue<long>() == 1, "Timezone year boundary incorrect.");
        check(months[1]!["moodCount"]!.GetValue<long>() == 2 &&
            Math.Abs(months[1]!["valence"]!.GetValue<double>() - .2) < .0001, "Monthly mean must average real entries.");
        check(months[2]!["valence"] is null, "Missing month must remain empty.");
        check(year["summary"]!["moodDays"]!.GetValue<int>() == 3, "Year mood days must count days, not months.");
        var otherYear=await call("GET","/Statistics?year=2024",null,200,other);
        check(otherYear!["summary"]!["moodEntries"]!.GetValue<long>()==0,"Year statistics leaked another account.");

        await call("POST","/Movies", new {noiDungID="completion-movie",tieuDe="Complete movie",
            valence=.2,arousal=-.3,moodSource="manual",releaseYear=2020,runtimeMinutes=125,
            cast=new[]{"Actor A","Actor B"}},201,admin);
        var details=await call("GET","/Movies/completion-movie",null,200,user);
        check(details!["phim"]!["releaseYear"]!.GetValue<int>()==2020 &&
              details["phim"]!["runtimeMinutes"]!.GetValue<int>()==125 &&
              details["phim"]!["cast"]!.AsArray().Count==2, "Movie metadata did not round trip.");
        await call("POST","/Movies",new {noiDungID="bad-duration",tieuDe="Bad",runtimeMinutes=0},400,admin);
        await call("POST","/Movies",new {noiDungID="bad-cast",tieuDe="Bad",cast=new[]{""}},400,admin);

        await db.SaveContent(new NoiDungGiaiTri {NoiDungID="discovery-film",TieuDe="Discovery film",
            LoaiNoiDung="Movie",Phim=new Phim(),Valence=0,Arousal=0},create:true);
        await db.SaveContent(new NoiDungGiaiTri {NoiDungID="discovery-song",TieuDe="Discovery song",
            LoaiNoiDung="Song",Nhac=new Nhac(),Valence=0,Arousal=0},create:true);
        async Task Open(string token, string id, string action="detail_open") {
            await call("POST","/Interactions",new {contentId=id,action,eventId=Guid.NewGuid().ToString("N")},200,token);
        }
        await Open(user,"discovery-film");
        await Open(user,"discovery-film");
        await Open(other,"discovery-film");
        await Open(user,"discovery-song");
        await Open(other,"discovery-song","external_open");
        await call("GET","/Discovery",null,401,null);
        await call("GET","/Discovery?mode=invalid",null,400,user);
        await call("GET","/Discovery?limit=51",null,400,user);
        var popular=await call("GET","/Discovery?type=Movie",null,200,user);
        var film=popular!["items"]!.AsArray().Single(x=>x!["content"]!["noiDungID"]!.GetValue<string>()=="discovery-film");
        check(film!["uniqueVisitors"]!.GetValue<long>()==2,"Popular must deduplicate accounts opening the same content.");
        check(popular["source"]!.GetValue<string>()=="Moodify","Popularity source must be explicit.");
        var music=await call("GET","/Discovery?type=Music",null,200,user);
        var song=music!["items"]!.AsArray().Single(x=>x!["content"]!["noiDungID"]!.GetValue<string>()=="discovery-song");
        check(song!["uniqueVisitors"]!.GetValue<long>()==1,"Player/external opens must not inflate popularity.");
        var updated=await call("GET","/Discovery?mode=updated&type=Music",null,200,user);
        check(updated!["items"]!.AsArray().Any(x=>x!["content"]!["noiDungID"]!.GetValue<string>()=="discovery-song"),"Updated catalog missing new content.");
        check(updated["items"]!.AsArray().All(x=>x!["content"]!["loaiNoiDung"]!.GetValue<string>()=="Song"),"Discovery type filter ignored.");
    }
}
