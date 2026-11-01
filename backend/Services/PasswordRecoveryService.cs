using System.Security.Cryptography;
using System.Text;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using mood_recommendation.Data;
using mood_recommendation.Models;

namespace mood_recommendation.Services;

public sealed record RecoverySecret(byte[] Value);
public sealed class ResetChallenge {
    [BsonId] public string Id {get;set;}="";
    public int? AccountId {get;set;}
    public string EmailHash {get;set;}="";
    public string CodeHash {get;set;}="";
    public string PasswordHash {get;set;}="";
    public DateTime CreatedAt {get;set;}
    public DateTime ExpiresAt {get;set;}
    public int Attempts {get;set;}
    public bool Consumed {get;set;}
}
public sealed class RecoveryThrottledException : Exception { }
public sealed class PasswordRecoveryService(MongoStore db,PasswordEmailService email,RecoverySecret secret)
{
    private IMongoCollection<ResetChallenge> Challenges=>db.Database.GetCollection<ResetChallenge>("passwordResets");
    private string Hash(string value)=>Convert.ToHexString(HMACSHA256.HashData(secret.Value,Encoding.UTF8.GetBytes(value)));
    public bool IsConfigured=>email.IsConfigured;
    public async Task InitializeAsync(CancellationToken ct=default) {
        await Challenges.Indexes.CreateOneAsync(new CreateIndexModel<ResetChallenge>(
            Builders<ResetChallenge>.IndexKeys.Ascending(x=>x.ExpiresAt),
            new CreateIndexOptions {ExpireAfter=TimeSpan.Zero}),cancellationToken:ct);
    }
    public async Task<string> RequestAsync(string address,CancellationToken ct) {
        var normalized=MongoStore.Normalize(address);
        var emailHash=Hash("email:"+normalized);
        var now=DateTime.UtcNow;
        if(await Challenges.Find(x=>x.EmailHash==emailHash && x.CreatedAt>now.AddSeconds(-60)).AnyAsync(ct))
            throw new RecoveryThrottledException();
        var user=await db.TaiKhoans.FirstOrDefaultAsync(x=>x.EmailNormalized==normalized && x.TrangThai,ct);
        var id=Guid.NewGuid().ToString("N");
        var code=RandomNumberGenerator.GetInt32(100000,1000000).ToString();
        await Challenges.InsertOneAsync(new ResetChallenge {
            Id=id,AccountId=user?.TaiKhoanID,EmailHash=emailHash,CodeHash=Hash(id+":"+code),
            PasswordHash=user?.MatKhau??"",CreatedAt=now,ExpiresAt=now.AddMinutes(10)
        },cancellationToken:ct);
        if(user is not null)email.Enqueue(user.Email,code);
        return id; // Same response shape for known and unknown mailboxes.
    }
    public async Task<bool> ResetAsync(string challengeId,string code,string password,CancellationToken ct) {
        var now=DateTime.UtcNow;
        var f=Builders<ResetChallenge>.Filter;
        var active=f.Eq(x=>x.Id,challengeId)&f.Eq(x=>x.Consumed,false)&f.Gt(x=>x.ExpiresAt,now)&f.Lt(x=>x.Attempts,5);
        var challenge=await Challenges.FindOneAndUpdateAsync(
            active & f.Eq(x=>x.CodeHash,Hash(challengeId+":"+code)),
            Builders<ResetChallenge>.Update.Set(x=>x.Consumed,true),cancellationToken:ct);
        if(challenge?.AccountId is not int userId) {
            await Challenges.UpdateOneAsync(active,Builders<ResetChallenge>.Update.Inc(x=>x.Attempts,1),cancellationToken:ct);
            return false;
        }
        var user=await db.TaiKhoans.FirstOrDefaultAsync(x=>x.TaiKhoanID==userId,ct);
        if(user is null || !user.TrangThai || Hash("email:"+user.EmailNormalized)!=challenge.EmailHash) return false;
        var result=await db.Collection<TaiKhoan>().UpdateOneAsync(
            x=>x.TaiKhoanID==userId && x.TrangThai && x.MatKhau==challenge.PasswordHash
                && x.EmailNormalized==user.EmailNormalized,
            Builders<TaiKhoan>.Update.Set(x=>x.MatKhau,BCrypt.Net.BCrypt.HashPassword(password))
                .Set(x=>x.HasPassword,true).Inc(x=>x.SessionVersion,1).Set(x=>x.UpdatedAt,now),
            cancellationToken:ct);
        return result.ModifiedCount==1;
    }
}
