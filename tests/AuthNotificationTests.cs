using MongoDB.Driver.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using MimeKit;
using MongoDB.Driver;
using mood_recommendation.Controllers;
using mood_recommendation.Data;
using mood_recommendation.Models;
using mood_recommendation.Services;

public static class AuthNotificationTests {
    public static async Task Run(MongoStore db,
        Func<string,string,object?,int,string?,Task<JsonNode?>> call,Action<bool,string> check,
        string root,string database,int userId,string userToken,string otherToken)
    {
        var folder=Path.Combine(root,".local-build","recovery-mail",database);
        await call("POST","/Auth/forgot-password",new{email="bad-address"},400,null);
        var account=new TaiKhoan {TenDangNhap="recovery_user",Email="recovery@test.invalid",
            MatKhau=BCrypt.Net.BCrypt.HashPassword("Old-password-1")};
        await db.Insert(account);
        var session=await call("POST","/Auth/login",new{identifier=account.Email,matKhau="Old-password-1"},200,null);
        var oldToken=session!["token"]!.GetValue<string>();
        var request=await call("POST","/Auth/forgot-password",new{email=account.Email},200,null);
        var challenge=request!["challengeId"]!.GetValue<string>();
        check(request["code"] is null && request["token"] is null,"Recovery response leaked a credential.");
        await call("POST","/Auth/forgot-password",new{email=account.Email},429,null);
        var unknown=await call("POST","/Auth/forgot-password",new{email="unknown@test.invalid"},200,null);
        check(unknown!["message"]!.GetValue<string>()==request["message"]!.GetValue<string>(),
            "Recovery disclosed mailbox existence.");
        string? code=null;
        for(var tries=0;tries<80 && code is null;tries++) {
            if(Directory.Exists(folder))foreach(var path in Directory.GetFiles(folder,"*.eml")) {
                var email=await MimeMessage.LoadAsync(path);
                if(email.To.Mailboxes.Any(x=>x.Address==account.Email))
                    code=Regex.Match(email.TextBody??"",@"\b[0-9]{6}\b").Value;
            }
            if(string.IsNullOrEmpty(code)){code=null;await Task.Delay(50);}
        }
        check(code is not null,"Recovery email was not written to the isolated pickup folder.");
        var wrong=code=="111111"?"222222":"111111";
        await call("POST","/Auth/reset-password",new{challengeId=challenge,code=wrong,newPassword="New-password-2"},400,null);
        await call("POST","/Auth/reset-password",new{challengeId=challenge,code,newPassword="short"},400,null);
        await call("POST","/Auth/reset-password",new{challengeId=challenge,code,newPassword="New-password-2"},200,null);
        await call("POST","/Auth/reset-password",new{challengeId=challenge,code,newPassword="Another-password-3"},400,null);
        await call("GET","/Auth/me",null,401,oldToken);
        await call("POST","/Auth/login",new{identifier=account.Email,matKhau="Old-password-1"},401,null);
        await call("POST","/Auth/login",new{identifier=account.Email,matKhau="New-password-2"},200,null);
        var challenges=db.Database.GetCollection<ResetChallenge>("passwordResets");
        check((await challenges.Find(x=>x.Id==challenge).FirstAsync()).CodeHash!=code,"Recovery code stored in plaintext.");
        var currentAccount=await db.Find<TaiKhoan>(account.TaiKhoanID);
        await challenges.UpdateOneAsync(x=>x.Id==challenge,
            Builders<ResetChallenge>.Update.Set(x=>x.Consumed,false).Set(x=>x.Attempts,5)
                .Set(x=>x.PasswordHash,currentAccount!.MatKhau).Set(x=>x.ExpiresAt,DateTime.UtcNow.AddMinutes(1)));
        await call("POST","/Auth/reset-password",new{challengeId=challenge,code,newPassword="Locked-password-3"},400,null);
        await challenges.UpdateOneAsync(x=>x.Id==challenge,
            Builders<ResetChallenge>.Update.Set(x=>x.Attempts,0).Set(x=>x.ExpiresAt,DateTime.UtcNow.AddSeconds(-1)));
        await call("POST","/Auth/reset-password",new{challengeId=challenge,code,newPassword="Expired-password-3"},400,null);
        await call("POST","/Auth/google",new{idToken="unsigned-token"},503,null);

        var verifier=new FakeGoogle();
        var cfg=new ConfigurationBuilder().Build();
        var recovery=new PasswordRecoveryService(db,
            new PasswordEmailService(cfg,new FakeEnvironment(),Microsoft.Extensions.Logging.Abstractions.NullLogger<PasswordEmailService>.Instance),
            new RecoverySecret(new byte[32]));
        var controller=new AuthProvidersController(db,recovery,verifier,
            new TokenService(new string('x',64)),cfg) {
            ControllerContext=new ControllerContext {HttpContext=new DefaultHttpContext()}
        };
        var newGoogle=await controller.Google(new GoogleLoginDto {IdToken="valid"},default);
        check(newGoogle is OkObjectResult,"Verified Google account did not create a Moodify session.");
        var googleUser=await db.TaiKhoans.FirstAsync(x=>x.GoogleUid=="google-fixture");
        check(googleUser.VaiTro=="User" && !googleUser.HasPassword,"Google signup elevated role or invented a password.");
        verifier.Identity=new("google-existing",account.Email,"Existing");
        check(await controller.Google(new GoogleLoginDto {IdToken="valid"},default) is ConflictObjectResult,
            "Existing password account was automatically linked.");
        check(await controller.Google(new GoogleLoginDto {IdToken="valid",CurrentPassword="bad"},default) is UnauthorizedObjectResult,
            "Google linking accepted wrong existing password.");
        check(await controller.Google(new GoogleLoginDto {IdToken="valid",CurrentPassword="New-password-2"},default) is OkObjectResult,
            "Google linking with verified identity and password failed.");
        verifier.Identity=new("google-admin","admin@test.invalid","Admin");
        check(await controller.Google(new GoogleLoginDto {IdToken="valid"},default) is ObjectResult{StatusCode:403},
            "Google login linked a privileged account.");
        check(await controller.Google(new GoogleLoginDto {IdToken="invalid"},default) is UnauthorizedObjectResult,
            "Google login accepted an unverified identity.");

        await call("GET","/Notifications/settings",null,401,null);
        var settings=await call("GET","/Notifications/settings",null,200,userToken);
        check(settings!["enabled"]!.GetValue<bool>()==false,"Notifications enabled without consent.");
        await call("PUT","/Notifications/settings",new{enabled=true,hour=20,minute=0,offsetMinutes=420},503,userToken);
        await call("PUT","/Notifications/settings",new{enabled=false,hour=25},400,userToken);
        var delivery=new FakePush();
        var push=new PushNotificationService(db,delivery);
        await push.InitializeAsync();
        await push.SaveAsync(new PushPreferences{AccountId=userId,Enabled=true,Hour=0},default);
        const string fid="test_installation_123456789";
        await push.RegisterAsync(userId,fid,default);
        await call("POST","/Notifications/unregister",new{fid},204,otherToken);
        check(await push.Devices.Find(x=>x.AccountId==userId).AnyAsync(),"Another account unregistered this installation.");
        var now=DateTime.UtcNow;
        await push.DispatchDueAsync(now,default);
        await push.DispatchDueAsync(now,default);
        check(delivery.Sends==1,"Scheduled push duplicated the same daily installation reminder.");
        await push.SaveAsync(new PushPreferences{AccountId=userId,Enabled=false},default);
        check(!await push.Devices.Find(x=>x.AccountId==userId).AnyAsync(),"Opt-out retained registered devices.");
        await push.DispatchDueAsync(now,default);
        check(delivery.Sends==1,"Push sent after opt-out.");
    }
    sealed class FakeGoogle:IGoogleIdentityVerifier {
        public bool IsConfigured=>true;
        public GoogleIdentity Identity=new("google-fixture","google@test.invalid","Google fixture");
        public Task<GoogleIdentity?> VerifyAsync(string token,CancellationToken ct)=>
            Task.FromResult<GoogleIdentity?>(token=="valid"?Identity:null);
    }
    sealed class FakePush:IPushDelivery {
        public bool IsConfigured=>true;
        public int Sends;
        public Task<PushDelivery> SendAsync(string fid,string title,string body,string screen,CancellationToken ct) {
            Sends++;return Task.FromResult(new PushDelivery(true));
        }
    }
    sealed class FakeEnvironment:Microsoft.Extensions.Hosting.IHostEnvironment {
        public string EnvironmentName {get;set;}="Development";
        public string ApplicationName {get;set;}="Tests";
        public string ContentRootPath {get;set;}="";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider {get;set;}=
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
