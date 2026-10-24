using System.Text.Json.Nodes;
using MongoDB.Bson;
using MongoDB.Driver;
using mood_recommendation.Data;
using mood_recommendation.Models;

public static class StatisticsTests {
 public static async Task Run(MongoStore db, Func<string,string,object?,int,string?,Task<JsonNode?>> call, Action<bool,string> check,int userId,string user,string other) {
  await call("GET","/Statistics",null,401,null);
  await call("GET","/Statistics?days=999",null,400,user);
  await call("GET","/Statistics?offsetMinutes=900",null,400,user);
  var since=DateTime.UtcNow.AddMinutes(-1);
  // More than the history page size: aggregation must count all records.
  for(var i=0;i<61;i++)await db.Insert(new LichSuTamTrang {TaiKhoanID=userId,TamTrangID=9901,ThoiGian=since,Valence=.4m,Arousal=-.2m});
  var first=DateTime.UtcNow.Date.AddDays(-6);
  await db.Insert(new LichSuTamTrang {TaiKhoanID=userId,TamTrangID=9901,ThoiGian=first.AddDays(-1),Valence=-.4m,Arousal=.2m});
  var stats=await call("GET","/Statistics?days=7&offsetMinutes=0",null,200,user);
  check(stats!["trend"]!.AsArray().Count==7,"Statistics must fill each calendar day.");
  check(stats["summary"]!["moodEntries"]!.GetValue<long>()>=61,"Statistics truncated at history page size.");
  check(stats["moods"]!.AsArray().Any(x=>x!["name"]!.GetValue<string>()=="Mood đã xóa"),"Deleted mood histories must remain in statistics.");
  check(stats["previous"]!["moodEntries"]!.GetValue<long>()>=1,"Previous period missing.");
  var previousOther=await call("GET","/Statistics?days=7",null,200,other);
  check(previousOther!["summary"]!["moodEntries"]!.GetValue<long>()<61,"Statistics leaked another account.");
  var genre=new TheLoai{TheLoaiID="stats-genre",TenTheLoai="Stats genre"};await db.Insert(genre);
  await db.SaveContent(new NoiDungGiaiTri{NoiDungID="stats-song",TieuDe="Stats song",LoaiNoiDung="Song",Valence=0,Arousal=0,Nhac=new Nhac{TheLoaiID="stats-genre",Genre="Stats genre"}},create:true);
  await db.Database.GetCollection<BsonDocument>("interactions").InsertOneAsync(new BsonDocument{
   ["_id"]="stats-event",["accountId"]=userId,["contentId"]="stats-song",["contentType"]="Song",["action"]="external_open",["createdAt"]=since});
  stats=await call("GET","/Statistics?days=7&offsetMinutes=420",null,200,user);
  check(stats!["contentTypes"]!.AsArray().Any(x=>x!["name"]!.GetValue<string>()=="Song"),"Music interactions missing.");
  check(stats["genres"]!.AsArray().Any(x=>x!["name"]!.GetValue<string>()=="Stats genre"),"Genre aggregation missing.");
 }
}
