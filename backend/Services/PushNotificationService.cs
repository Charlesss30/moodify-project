using System.Security.Cryptography;
using System.Text;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using mood_recommendation.Data;

namespace mood_recommendation.Services;

public sealed class PushPreferences {
    [BsonId] public int AccountId {get;set;}
    public bool Enabled {get;set;}
    public bool DailyMoodReminder {get;set;}=true;
    public bool WeeklySummary {get;set;}
    public int Hour {get;set;}=20;
    public int Minute {get;set;}
    public int OffsetMinutes {get;set;}=420;
}
public sealed class PushDevice {
    [BsonId] public string Id {get;set;}="";
    public int AccountId {get;set;}
    public string Fid {get;set;}="";
    public DateTime UpdatedAt {get;set;}
}
public sealed class PushReceipt {
    [BsonId] public string Id {get;set;}="";
    public DateTime ExpiresAt {get;set;}
}
public sealed class PushNotificationService(MongoStore db,IPushDelivery delivery)
{
    public bool IsConfigured=>delivery.IsConfigured;
    public IMongoCollection<PushPreferences> Preferences=>db.Database.GetCollection<PushPreferences>("notificationPreferences");
    public IMongoCollection<PushDevice> Devices=>db.Database.GetCollection<PushDevice>("pushDevices");
    private IMongoCollection<PushReceipt> Receipts=>db.Database.GetCollection<PushReceipt>("pushReceipts");
    public static string DeviceId(string token)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    public async Task InitializeAsync(CancellationToken ct=default) {
        await Devices.Indexes.CreateOneAsync(new CreateIndexModel<PushDevice>(
            Builders<PushDevice>.IndexKeys.Ascending(x=>x.AccountId)),cancellationToken:ct);
        await Devices.Indexes.CreateOneAsync(new CreateIndexModel<PushDevice>(
            Builders<PushDevice>.IndexKeys.Ascending(x=>x.UpdatedAt),
            new CreateIndexOptions {ExpireAfter=TimeSpan.FromDays(90)}),cancellationToken:ct);
        await Receipts.Indexes.CreateOneAsync(new CreateIndexModel<PushReceipt>(
            Builders<PushReceipt>.IndexKeys.Ascending(x=>x.ExpiresAt),
            new CreateIndexOptions {ExpireAfter=TimeSpan.Zero}),cancellationToken:ct);
    }
    public async Task<PushPreferences> GetAsync(int id,CancellationToken ct)=>
        await Preferences.Find(x=>x.AccountId==id).FirstOrDefaultAsync(ct)??new PushPreferences{AccountId=id};
    public async Task SaveAsync(PushPreferences value,CancellationToken ct) {
        await Preferences.ReplaceOneAsync(x=>x.AccountId==value.AccountId,value,new ReplaceOptions{IsUpsert=true},ct);
        if(!value.Enabled)await Devices.DeleteManyAsync(x=>x.AccountId==value.AccountId,ct);
    }
    public Task RegisterAsync(int id,string token,CancellationToken ct) =>
        Devices.ReplaceOneAsync(x=>x.Id==DeviceId(token),new PushDevice{
            Id=DeviceId(token),AccountId=id,Fid=token,UpdatedAt=DateTime.UtcNow
        },new ReplaceOptions{IsUpsert=true},ct);
    public Task UnregisterAsync(int id,string token,CancellationToken ct)=>
        Devices.DeleteOneAsync(x=>x.Id==DeviceId(token) && x.AccountId==id,ct);

    public async Task<int> SendAsync(int id,string kind,string? occurrence,CancellationToken ct) {
        var prefs=await GetAsync(id,ct);
        if(!prefs.Enabled || !IsConfigured)return 0;
        var account=await db.Find<mood_recommendation.Models.TaiKhoan>(id);
        if(account?.TrangThai!=true)return 0;
        var devices=await Devices.Find(x=>x.AccountId==id && x.UpdatedAt>DateTime.UtcNow.AddDays(-90)).ToListAsync(ct);
        var accepted=0;
        foreach(var device in devices) {
            string? receipt=occurrence is null?null:$"{id}:{device.Id}:{kind}:{occurrence}";
            if(receipt is not null) {
                try {await Receipts.InsertOneAsync(new PushReceipt{Id=receipt,ExpiresAt=DateTime.UtcNow.AddDays(8)},cancellationToken:ct);}
                catch(MongoWriteException e) when(e.WriteError.Category==ServerErrorCategory.DuplicateKey) {continue;}
            }
            var weekly=kind=="weekly";
            PushDelivery result;
            try {result=await delivery.SendAsync(device.Fid,"Moodify",
                weekly?"Dành một phút nhìn lại tâm trạng và hành trình giải trí tuần qua."
                    :"Hôm nay bạn thế nào? Ghi nhận cảm xúc và tìm một bộ phim, bài hát phù hợp.",
                weekly?"statistics":"discover",ct);}
            catch(Exception) when(!ct.IsCancellationRequested) {result=new(false);}
            if(result.Accepted)accepted++;
            else if(result.Unregistered)await Devices.DeleteOneAsync(x=>x.Id==device.Id,ct);
            else if(receipt is not null)await Receipts.DeleteOneAsync(x=>x.Id==receipt,ct);
        }
        return accepted;
    }
    public async Task DispatchDueAsync(DateTime utcNow,CancellationToken ct) {
        if(!IsConfigured)return;
        var settings=await Preferences.Find(x=>x.Enabled).ToListAsync(ct);
        foreach(var value in settings) {
            var local=utcNow.AddMinutes(value.OffsetMinutes);
            if(local.Hour*60+local.Minute<value.Hour*60+value.Minute)continue;
            var day=local.ToString("yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture);
            if(value.DailyMoodReminder)await SendAsync(value.AccountId,"daily",day,ct);
            if(value.WeeklySummary && local.DayOfWeek==DayOfWeek.Sunday)
                await SendAsync(value.AccountId,"weekly",day,ct);
        }
    }
}
public sealed class PushNotificationWorker(PushNotificationService push,ILogger<PushNotificationWorker> log):BackgroundService {
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        using var timer=new PeriodicTimer(TimeSpan.FromMinutes(1));
        while(await timer.WaitForNextTickAsync(stoppingToken)) {
            try {await push.DispatchDueAsync(DateTime.UtcNow,stoppingToken);}
            catch(Exception error) when(!stoppingToken.IsCancellationRequested) {
                log.LogWarning("Notification schedule check failed ({ErrorType}).",error.GetType().Name);
            }
        }
    }
}
