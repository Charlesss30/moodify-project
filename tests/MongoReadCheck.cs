using MongoDB.Driver;
using MongoDB.Driver.Linq;
using Microsoft.Extensions.Configuration;
using mood_recommendation.Data;
using System.Text.Json;

public static class MongoReadCheck
{
    public static async Task Run(string connection)
    {
        var db = new MongoStore(new MongoClient(connection),new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string,string?>{{"Mongo:Database","mood_recommendation"}}).Build());
        var movies=await db.Contents("Movie");var music=await db.Contents("Song");
        var moods=await db.TamTrangs.ToListAsync();
        var accounts=await db.TaiKhoans.Select(x=>new {x.TaiKhoanID,x.VaiTro,x.TrangThai}).ToListAsync();
        var history=await db.LichSuTamTrangs.ToListAsync();var ratings=await db.DanhGias.ToListAsync();
        foreach(var song in music)
            if(song.Nhac==null||song.Nhac.Valence is <0 or >1)throw new Exception("Unmigrated music data.");
        if(movies.Any(x=>x.Phim==null||string.IsNullOrWhiteSpace(x.Phim.TheLoai)))throw new Exception("Movie genre mapping missing.");
        _=JsonSerializer.Serialize(movies);_=JsonSerializer.Serialize(music);
        Console.WriteLine($"PASS live MongoDB read: {movies.Count} movies, {music.Count} songs, {moods.Count} moods, {accounts.Count} accounts, {history.Count} history, {ratings.Count} ratings. No records modified.");
    }
}
