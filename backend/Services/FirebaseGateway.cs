using FirebaseAdmin;
using FirebaseAdmin.Auth;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using System.Text.Json;

namespace mood_recommendation.Services;

public sealed record GoogleIdentity(string Uid, string Email, string Name);
public sealed record PushDelivery(bool Accepted, bool Unregistered = false);
public interface IGoogleIdentityVerifier {
    bool IsConfigured { get; }
    Task<GoogleIdentity?> VerifyAsync(string idToken, CancellationToken ct);
}
public interface IPushDelivery {
    bool IsConfigured { get; }
    Task<PushDelivery> SendAsync(string token, string title, string body, string screen, CancellationToken ct);
}
public sealed class FirebaseGateway : IGoogleIdentityVerifier, IPushDelivery
{
    private readonly FirebaseApp? app;
    public string? GoogleClientId { get; }
    public bool IsConfigured => app is not null;
    public FirebaseGateway(IConfiguration config, ILogger<FirebaseGateway> log, IHostEnvironment env)
    {
        GoogleClientId=config["Firebase:GoogleServerClientId"];
        var project=config["Firebase:ProjectId"];
        var file=config["Firebase:ServiceAccountPath"];
        if (!string.IsNullOrWhiteSpace(file) && !Path.IsPathRooted(file)) file=Path.Combine(env.ContentRootPath,file);
        if (string.IsNullOrWhiteSpace(project) || string.IsNullOrWhiteSpace(file) || !File.Exists(file)) return;
        try {
            var credential=CredentialFactory.FromFile<ServiceAccountCredential>(file);
            if(credential.ProjectId!=project) {
                log.LogWarning("Firebase project ID does not match the service account.");
                return;
            }
            app=FirebaseApp.Create(new AppOptions {
                ProjectId=project, Credential=credential.ToGoogleCredential()
            }, "moodify-"+Guid.NewGuid().ToString("N"));
        } catch (Exception error) when (error is IOException or ArgumentException or JsonException or InvalidOperationException or FormatException) {
            log.LogWarning("Firebase configuration could not be loaded ({ErrorType}).", error.GetType().Name);
        }
    }
    public async Task<GoogleIdentity?> VerifyAsync(string idToken, CancellationToken ct)
    {
        if (app is null) return null;
        try {
            var token=await FirebaseAuth.GetAuth(app).VerifyIdTokenAsync(idToken, ct);
            return IdentityFromVerifiedClaims(token.Uid, token.Claims);
        } catch (FirebaseAuthException) {return null;}
        catch (ArgumentException) {return null;}
    }
    // Call only after Firebase Admin has verified the token signature, audience and expiry.
    public static GoogleIdentity? IdentityFromVerifiedClaims(string uid, IReadOnlyDictionary<string, object> claims)
    {
        if (string.IsNullOrWhiteSpace(uid)
            || !claims.TryGetValue("email_verified", out var verified) || verified is not true
            || !claims.TryGetValue("email", out var emailValue) || emailValue is not string email
            || string.IsNullOrWhiteSpace(email)
            || !claims.TryGetValue("firebase", out var firebase)) return null;

        // Nested Firebase Admin claims are Newtonsoft JObject/JToken instances.
        // System.Text.Json serializes their enumerable internals as arrays, losing values.
        var firebaseJson = firebase is Newtonsoft.Json.Linq.JToken sdkClaim
            ? sdkClaim.ToString(Newtonsoft.Json.Formatting.None)
            : JsonSerializer.Serialize(firebase);
        using var data = JsonDocument.Parse(firebaseJson);
        if (data.RootElement.ValueKind != JsonValueKind.Object
            || !data.RootElement.TryGetProperty("sign_in_provider", out var provider)) return null;

        // Firebase claims can contain the provider as a string or a singleton array.
        // Reject unexpected types, empty/mixed arrays and other providers without throwing.
        var isGoogle = provider.ValueKind switch {
            JsonValueKind.String => provider.GetString() == "google.com",
            JsonValueKind.Array => provider.GetArrayLength() == 1
                && provider[0].ValueKind == JsonValueKind.String
                && provider[0].GetString() == "google.com",
            _ => false
        };
        if (!isGoogle) return null;
        var name = claims.TryGetValue("name", out var value) ? value?.ToString() : null;
        return new(uid, email, name ?? email.Split('@')[0]);
    }

    public async Task<PushDelivery> SendAsync(string token,string title,string body,string screen,CancellationToken ct)
    {
        if (app is null) return new(false);
        try {
            await FirebaseMessaging.GetMessaging(app).SendAsync(new Message {
                Fid=token, Notification=new Notification {Title=title, Body=body},
                Data=new Dictionary<string,string> {["screen"]=screen},
                Android=new AndroidConfig {
                    Priority=Priority.High, Notification=new AndroidNotification { ChannelId="moodify_reminders" }
                }
            },ct);
            return new(true);
        } catch (FirebaseMessagingException e) when (e.MessagingErrorCode==MessagingErrorCode.Unregistered) {
            return new(false,true);
        } catch (FirebaseMessagingException) { return new(false); }
    }
}
