using MongoDB.Driver;
using MongoDB.Driver.Linq;
using Microsoft.Extensions.Configuration;
using mood_recommendation.Data;
using System.Text.Json;
using System.Text.Json.Nodes;

public static class MongoReadCheck
{
    public static async Task Run(string connection)
    {
        var db = new MongoStore(new MongoClient(connection),new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string,string?>{{"Mongo:Database","mood_recommendation"}}).Build());
        var movies=await db.Contents("Movie");var music=await db.Contents("Song");
        var moods=await db.TamTrangs.ToListAsync();
        var accounts=await db.TaiKhoans.Select(x=>new {x.TaiKhoanID,x.TenDangNhap,x.VaiTro,x.TrangThai}).ToListAsync();
        var history=await db.LichSuTamTrangs.ToListAsync();var ratings=await db.DanhGias.ToListAsync();
        var registryState=await db.Database.GetCollection<MongoDB.Bson.BsonDocument>("aiModelState")
            .Find(new MongoDB.Bson.BsonDocument("_id","active")).FirstOrDefaultAsync();
        var softDeleted=await db.Database.GetCollection<MongoDB.Bson.BsonDocument>("contents")
            .CountDocumentsAsync(new MongoDB.Bson.BsonDocument("deleted",true));
        if(moods.Any(x=>x.MinValence < -1 || x.MaxValence > 1 || x.MinValence > x.MaxValence
            || x.MinArousal < -1 || x.MaxArousal > 1 || x.MinArousal > x.MaxArousal))
            throw new Exception("Mood bounds are invalid.");
        if(moods.GroupBy(x=>x.TenTamTrang,StringComparer.OrdinalIgnoreCase).Any(group=>group.Count()>1))
            throw new Exception("Mood names are not unique.");
        if(movies.Concat(music).Any(x=>x.Valence is < -1 or > 1 || x.Arousal is < -1 or > 1))
            throw new Exception("Content VA is outside [-1,1].");
        foreach(var song in music)
            if(song.Nhac==null||song.Nhac.Valence is <0 or >1)throw new Exception("Unmigrated music data.");
        if(movies.Any(x=>x.Phim==null||string.IsNullOrWhiteSpace(x.Phim.TheLoai)))throw new Exception("Movie genre mapping missing.");
        _=JsonSerializer.Serialize(movies);_=JsonSerializer.Serialize(music);
        var mapping = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(
            AppContext.BaseDirectory, "../../../../backend", "Data", "emotion-mood-mapping.json")))
            ?? throw new Exception("Mapping JSON is empty.");
        Console.WriteLine($"PASS live MongoDB read-only inventory: {movies.Count} movies, {music.Count} songs, {moods.Count} moods, {accounts.Count} accounts, {history.Count} history, {ratings.Count} ratings, softDeleted={softDeleted}. No records modified.");
        Console.WriteLine("Account roles/status counts: " + string.Join(", ",
            accounts.GroupBy(account => $"{account.VaiTro}:{(account.TrangThai ? "enabled" : "disabled")}")
                .OrderBy(group => group.Key).Select(group => $"{group.Key}={group.Count()}")));
        Console.WriteLine("Enabled Admin/AIEngineer login identifiers: " + string.Join(", ",
            accounts.Where(account => account.TrangThai && account.VaiTro is "Admin" or "AIEngineer")
                .Select(account => $"{account.TenDangNhap} ({account.VaiTro})")));
        Console.WriteLine("Enabled User login identifier: " + string.Join(", ",
            accounts.Where(account => account.TrangThai && account.VaiTro == "User")
                .Select(account => account.TenDangNhap)));
        var activeVersion = registryState?.GetValue("activeVersion", MongoDB.Bson.BsonNull.Value);
        Console.WriteLine($"AI registry (read-only): activeVersion={(activeVersion?.IsString == true ? activeVersion.AsString : "none")} " +
            $"revision={registryState?.GetValue("revision", 0L).ToInt64() ?? 0}");
        var moodNames = moods.ToDictionary(mood => mood.TamTrangID, mood => mood.TenTamTrang);
        Console.WriteLine("Latest history records (content-free provenance summary):");
        foreach (var item in history.OrderByDescending(entry => entry.ThoiGian)
                     .ThenByDescending(entry => entry.LichSuID).Take(5))
            Console.WriteLine($"  id={item.LichSuID} mood={moodNames.GetValueOrDefault(item.TamTrangID, "unknown")} " +
                $"source={item.Source ?? "none"} analysis={item.AnalysisSource ?? "none"} " +
                $"decision={item.DecisionSource ?? "none"} recommendations={item.RecommendedItems?.Count ?? 0} " +
                $"VA=({item.Valence},{item.Arousal}) at={item.ThoiGian:O}");
        Console.WriteLine("Mood bounds:");
        foreach(var mood in moods.OrderBy(x=>x.TamTrangID))
            Console.WriteLine($"  {mood.TamTrangID}\t{mood.TenTamTrang}\tV=[{mood.MinValence},{mood.MaxValence}] A=[{mood.MinArousal},{mood.MaxArousal}]");
        Console.WriteLine("Mapping resolution by exact unique mood name:");
        foreach(var entry in mapping["labels"]!.AsArray())
        {
            var name=entry!["moodName"]?.GetValue<string>();
            var matches=name is null ? [] : moods.Where(x=>string.Equals(x.TenTamTrang,name,StringComparison.OrdinalIgnoreCase)).ToArray();
            Console.WriteLine($"  {entry["label"]}\t{(matches.Length==1?$"{matches[0].TamTrangID}:{matches[0].TenTamTrang}":"unmapped")}");
        }
        Console.WriteLine("Content coverage and closest local IDs (nearest distance squared):");
        foreach(var mood in moods.OrderBy(x=>x.TamTrangID))
        foreach(var (type, content) in new[]{("Movie",movies),("Song",music)})
        {
            var v=(mood.MinValence+mood.MaxValence)/2;
            var a=(mood.MinArousal+mood.MaxArousal)/2;
            var ranked=content.Select(x=>new { Item=x, Distance=(x.Valence-v)*(x.Valence-v)+(x.Arousal-a)*(x.Arousal-a) })
                .OrderBy(x=>x.Distance).ThenBy(x=>x.Item.NoiDungID).ToArray();
            var reviewed=content.Count(x=>x.ReviewStatus=="reviewed");
            var pending=content.Count(x=>x.ReviewStatus=="pending");
            var nearest=string.Join("; ",ranked.Take(3).Select(x=>$"{x.Item.NoiDungID} ({x.Distance:0.0000}, {x.Item.ReviewStatus}, model={x.Item.ModelVersion??"none"}, rule={x.Item.MoodRuleVersion??"none"})"));
            Console.WriteLine($"  {mood.TamTrangID}:{mood.TenTamTrang}\t{type}: total={content.Count}, reviewed={reviewed}, pending={pending}, nearest={nearest}");
        }
    }
}
