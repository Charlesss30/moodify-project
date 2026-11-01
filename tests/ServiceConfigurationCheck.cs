using Google.Apis.Auth.OAuth2;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;

public static class ServiceConfigurationCheck {
    public static async Task Run(string root) {
        var config=new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(root,"backend/appsettings.Development.json")).Build();
        using(var smtp=new SmtpClient {Timeout=15000}) {
            await smtp.ConnectAsync(config["Email:Host"]!,config.GetValue("Email:Port",587),SecureSocketOptions.StartTls);
            await smtp.AuthenticateAsync(config["Email:Username"]!,config["Email:Password"]!);
            await smtp.DisconnectAsync(true);
        }
        Console.WriteLine("PASS: Gmail SMTP TLS/authentication. No email sent.");
        var file=config["Firebase:ServiceAccountPath"]!;
        if(!Path.IsPathRooted(file))file=Path.Combine(root,"backend",file);
        var credential=CredentialFactory.FromFile<ServiceAccountCredential>(file);
        if(credential.ProjectId!=config["Firebase:ProjectId"])throw new Exception("Firebase project mismatch.");
        var google=credential.ToGoogleCredential().CreateScoped("https://www.googleapis.com/auth/firebase.messaging");
        await google.UnderlyingCredential.GetAccessTokenForRequestAsync();
        Console.WriteLine("PASS: Firebase service-account authentication. No push sent.");
        if(string.IsNullOrWhiteSpace(config["Firebase:GoogleServerClientId"]))
            throw new Exception("Google OAuth client is missing.");
        Console.WriteLine("PASS: Google server OAuth client configured.");
    }
}
