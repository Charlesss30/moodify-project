using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using mood_recommendation.Models;
using System.ComponentModel.DataAnnotations;

namespace mood_recommendation.Data;

public sealed class MongoStore
{
    public IMongoDatabase Database { get; }
    // Serializes application requests in this single-instance, standalone deployment.
    public SemaphoreSlim Gate { get; } = new(1,1);
    public MongoStore(IMongoClient client, IConfiguration config)
    {
        Database = client.GetDatabase(config["Mongo:Database"] ?? "mood_recommendation");
    }
    public static string CollectionName(string name) => name switch { "TaiKhoan" => "accounts", "NoiDungGiaiTri" => "contents", "Phim" => "movies", "Nhac" => "music", "TamTrang" => "moods", "TheLoai" => "genres", "DanhGia" => "ratings", "LichSuTamTrang" => "moodHistory", "_Counters" => "counters", _ => throw new ArgumentException("Unknown collection: " + name) };
    public IMongoCollection<T> Collection<T>() => Database.GetCollection<T>(CollectionName(typeof(T).Name));
    public IQueryable<TaiKhoan> TaiKhoans => Collection<TaiKhoan>().AsQueryable();
    public IQueryable<NoiDungGiaiTri> NoiDungGiaiTris => Collection<NoiDungGiaiTri>().AsQueryable().Where(x=>!x.Deleted);
    public IQueryable<Phim> Phims => Collection<Phim>().AsQueryable();
    public IQueryable<Nhac> Nhacs => Collection<Nhac>().AsQueryable();
    public IQueryable<TamTrang> TamTrangs => Collection<TamTrang>().AsQueryable();
    public IQueryable<TheLoai> TheLoais => Collection<TheLoai>().AsQueryable();
    public IQueryable<DanhGia> DanhGias => Collection<DanhGia>().AsQueryable();
    public IQueryable<LichSuTamTrang> LichSuTamTrangs => Collection<LichSuTamTrang>().AsQueryable();
    public Task<T?> Find<T>(object id) => Collection<T>().Find(new BsonDocument("_id",BsonValue.Create(id))).FirstOrDefaultAsync()!;
    public static string Normalize(string s) => s.Trim().ToUpperInvariant();

    public async Task Initialize(string schemaFile)
    {
        var schemas = BsonDocument.Parse(await File.ReadAllTextAsync(schemaFile));
        var names = (await Database.ListCollectionNamesAsync()).ToList();
        if (names.Any(n => new[]{"TaiKhoan","NoiDungGiaiTri","Phim","Nhac","TamTrang","TheLoai","DanhGia","LichSuTamTrang"}.Contains(n))) throw new InvalidOperationException("Run node scripts/mongo-english.mjs --apply before starting the backend.");
        foreach(var entry in schemas)
            if(!names.Contains(entry.Name))
                await Database.CreateCollectionAsync(entry.Name,new CreateCollectionOptions<BsonDocument>{ Validator=entry.Value.AsBsonDocument });
        foreach(var name in new[]{"accounts","moods","ratings","moodHistory"})
        {
            var latest = await Database.GetCollection<BsonDocument>(name).Find(FilterDefinition<BsonDocument>.Empty).Sort(new BsonDocument("_id",-1)).FirstOrDefaultAsync();
            await Database.GetCollection<BsonDocument>("counters").UpdateOneAsync(new BsonDocument("_id",name),
                new BsonDocument("$max",new BsonDocument("value",latest?["_id"].ToInt64() ?? 0)),new UpdateOptions{IsUpsert=true});
        }
        foreach(var (name,field) in new[]{("accounts","emailNormalized"),("accounts","usernameNormalized"),("genres","nameNormalized"),("moods","nameNormalized")})
            await Database.GetCollection<BsonDocument>(name).Indexes.CreateOneAsync(new CreateIndexModel<BsonDocument>(
                new BsonDocument(field,1),new CreateIndexOptions{Unique=true}));
        await Collection<Nhac>().Indexes.CreateOneAsync(new CreateIndexModel<Nhac>(
            new BsonDocument{{"source",1},{"externalId",1}},new CreateIndexOptions<Nhac>{Unique=true,PartialFilterExpression=new BsonDocument("externalId",new BsonDocument("$type","string"))}));
        await Collection<Phim>().Indexes.CreateOneAsync(new CreateIndexModel<Phim>(new BsonDocument("genreIds",1)));
        await Collection<Nhac>().Indexes.CreateOneAsync(new CreateIndexModel<Nhac>(new BsonDocument("genreId",1)));
        await Collection<LichSuTamTrang>().Indexes.CreateOneAsync(new CreateIndexModel<LichSuTamTrang>(new BsonDocument{{"accountId",1},{"createdAt",-1}}));
        await Collection<DanhGia>().Indexes.CreateOneAsync(new CreateIndexModel<DanhGia>(new BsonDocument{{"accountId",1},{"contentId",1}}));
        await Collection<DanhGia>().Indexes.CreateOneAsync(new CreateIndexModel<DanhGia>(new BsonDocument("contentId",1)));
        // Finish interrupted deletes / remove incomplete inserts after a process restart.
        foreach(var item in await Collection<NoiDungGiaiTri>().Find(x=>x.Deleted).ToListAsync()) await DeleteContent(item.NoiDungID);
    }
    private async Task<long> NextId(string name)
    {
        name = CollectionName(name);
        var row=await Database.GetCollection<BsonDocument>("counters").FindOneAndUpdateAsync(
            new BsonDocument("_id",name),new BsonDocument("$inc",new BsonDocument("value",1L)),
            new FindOneAndUpdateOptions<BsonDocument>{IsUpsert=true,ReturnDocument=ReturnDocument.After});
        return row["value"].ToInt64();
    }
    private static void NormalizeFields<T>(T entity)
    {
        switch(entity)
        {
            case NoiDungGiaiTri x: x.UpdatedAt=DateTime.UtcNow;break;
            case TaiKhoan x: x.UpdatedAt=DateTime.UtcNow;x.TenDangNhap=x.TenDangNhap.Trim();x.Email=x.Email.Trim();x.EmailNormalized=Normalize(x.Email);x.TenDangNhapNormalized=Normalize(x.TenDangNhap);break;
            case TheLoai x: x.UpdatedAt=DateTime.UtcNow;x.TenTheLoai=x.TenTheLoai.Trim();x.TenTheLoaiNormalized=Normalize(x.TenTheLoai);break;
            case TamTrang x: x.UpdatedAt=DateTime.UtcNow;x.TenTamTrang=x.TenTamTrang.Trim();x.TenTamTrangNormalized=Normalize(x.TenTamTrang);break;
        }
    }
    public async Task Insert<T>(T entity)
    {
        NormalizeFields(entity);
        switch(entity)
        {
            case TaiKhoan x when x.TaiKhoanID==0: x.TaiKhoanID=checked((int)await NextId(nameof(TaiKhoan)));break;
            case TamTrang x when x.TamTrangID==0: x.TamTrangID=checked((int)await NextId(nameof(TamTrang)));break;
            case DanhGia x when x.DanhGiaID==0: x.DanhGiaID=await NextId(nameof(DanhGia));break;
            case LichSuTamTrang x when x.LichSuID==0: x.LichSuID=await NextId(nameof(LichSuTamTrang));break;
            case TheLoai x when string.IsNullOrEmpty(x.TheLoaiID): x.TheLoaiID="GENRE_"+Guid.NewGuid().ToString("N");break;
        }
        await Collection<T>().InsertOneAsync(entity);
    }
    public async Task Update<T>(T entity)
    {
        NormalizeFields(entity);
        var doc=entity.ToBsonDocument();
        var result=await Collection<T>().ReplaceOneAsync(new BsonDocument("_id",doc["_id"]),entity);
        if(result.MatchedCount==0)throw new ValidationException("This item was deleted. Please reload.");
    }
    public Task Delete<T>(T entity) => Collection<T>().DeleteOneAsync(new BsonDocument("_id",entity.ToBsonDocument()["_id"]));

    public async Task<List<NoiDungGiaiTri>> Hydrate(List<NoiDungGiaiTri> items)
    {
        if(items.Count==0)return items;
        var ids=items.Select(x=>x.NoiDungID).ToArray();
        var movies=await Phims.Where(x=>ids.Contains(x.NoiDungID)).ToListAsync();
        var songs=await Nhacs.Where(x=>ids.Contains(x.NoiDungID)).ToListAsync();
        var genreIds=movies.SelectMany(x=>x.TheLoaiIDs).Concat(songs.Where(x=>x.TheLoaiID!=null).Select(x=>x.TheLoaiID!)).Distinct().ToArray();
        var genres=(await TheLoais.Where(x=>genreIds.Contains(x.TheLoaiID)).ToListAsync()).ToDictionary(x=>x.TheLoaiID,x=>x.TenTheLoai);
        foreach(var movie in movies)movie.TheLoai=string.Join(", ",movie.TheLoaiIDs.Where(genres.ContainsKey).Select(id=>genres[id]));
        foreach(var song in songs)song.Genre=song.TheLoaiID!=null?genres.GetValueOrDefault(song.TheLoaiID):null;
        var movieMap=movies.ToDictionary(x=>x.NoiDungID);var songMap=songs.ToDictionary(x=>x.NoiDungID);
        foreach(var item in items){item.Phim=movieMap.GetValueOrDefault(item.NoiDungID);item.Nhac=songMap.GetValueOrDefault(item.NoiDungID);}
        return items;
    }
    public async Task<NoiDungGiaiTri?> GetContent(string id)
    {
        var item=await NoiDungGiaiTris.FirstOrDefaultAsync(x=>x.NoiDungID==id);
        return item==null?null:(await Hydrate([item]))[0];
    }
    public async Task<List<NoiDungGiaiTri>> Contents(string type) => await Hydrate(await NoiDungGiaiTris.Where(x=>x.LoaiNoiDung==type).ToListAsync());
    private async Task<string[]> ResolveGenres(string? names,bool multiple)
    {
        var values = multiple ? (names??"").Split([',',';','/','|'],StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries) : string.IsNullOrWhiteSpace(names)?[]:new[]{names.Trim()};
        var ids=new List<string>();
        foreach(var name in values.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var normalized=Normalize(name);var genre=await TheLoais.FirstOrDefaultAsync(x=>x.TenTheLoaiNormalized==normalized);
            if(genre==null){genre=new TheLoai{TenTheLoai=name};await Insert(genre);}
            ids.Add(genre.TheLoaiID);
        }
        return ids.ToArray();
    }
    public async Task SaveContent(NoiDungGiaiTri content,bool create=false)
    {
        var previous=create?null:await GetContent(content.NoiDungID);
        if(!create&&previous==null)throw new ValidationException("Content not found.");
        if(content.Phim!=null){content.Phim.NoiDungID=content.NoiDungID;content.Phim.TheLoaiIDs=await ResolveGenres(content.Phim.TheLoai,true);}
        if(content.Nhac!=null){content.Nhac.NoiDungID=content.NoiDungID;content.Nhac.TheLoaiID=(await ResolveGenres(content.Nhac.Genre,false)).FirstOrDefault();}
        if(create){content.Deleted=true;await Insert(content);}
        try {
            if(content.Phim!=null) {if(create)await Insert(content.Phim);else await Update(content.Phim);}
            if(content.Nhac!=null) {if(create)await Insert(content.Nhac);else await Update(content.Nhac);}
            content.Deleted=false;await Update(content);
        }catch {
            if(create)await DeleteContent(content.NoiDungID);
            else if(previous!=null){if(previous.Phim!=null)await Update(previous.Phim);if(previous.Nhac!=null)await Update(previous.Nhac);await Update(previous);}
            throw;
        }
    }
    public async Task DeleteContent(string id)
    {
        await Collection<NoiDungGiaiTri>().UpdateOneAsync(x=>x.NoiDungID==id,Builders<NoiDungGiaiTri>.Update.Set(x=>x.Deleted,true));
        await Collection<DanhGia>().DeleteManyAsync(x=>x.NoiDungID==id);
        await Collection<Phim>().DeleteOneAsync(x=>x.NoiDungID==id);
        await Collection<Nhac>().DeleteOneAsync(x=>x.NoiDungID==id);
        await Collection<NoiDungGiaiTri>().DeleteOneAsync(x=>x.NoiDungID==id);
    }
}
