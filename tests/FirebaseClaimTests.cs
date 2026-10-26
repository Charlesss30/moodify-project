using System.Text.Json;
using mood_recommendation.Services;

public static class FirebaseClaimTests
{
    public static int Run()
    {
        var count = 0;
        void Check(bool valid, string message) {
            if (!valid) throw new Exception(message);
            count++;
        }
        Dictionary<string, object> Claims(object? provider) => new() {
            ["email_verified"] = true,
            ["email"] = "google-user@test.invalid",
            ["name"] = "Google User",
            ["firebase"] = new Dictionary<string, object?> { ["sign_in_provider"] = provider }
        };
        var standard = FirebaseGateway.IdentityFromVerifiedClaims("fixture-uid", Claims("google.com"));
        Check(standard is { Uid: "fixture-uid", Email: "google-user@test.invalid", Name: "Google User" },
            "A verified Google string provider should retain the account identity.");
        // Firebase Admin uses Newtonsoft.Json for nested claim objects.
        var sdkClaims = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, object>>(
            """{"email_verified":true,"email":"google-user@test.invalid","name":"Google User","firebase":{"sign_in_provider":"google.com","identities":{"google.com":["fixture-uid"],"email":["google-user@test.invalid"]}}}""")!;
        Check(sdkClaims["firebase"] is Newtonsoft.Json.Linq.JObject,
            "SDK fixture must use Newtonsoft JObject, not a plain dictionary.");
        Check(FirebaseGateway.IdentityFromVerifiedClaims("fixture-uid", sdkClaims) == standard,
            "Verified Firebase SDK JObject claims must retain Google identity.");
        var sdkPassword = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, object>>(
            """{"email_verified":true,"email":"google-user@test.invalid","firebase":{"sign_in_provider":"password"}}""")!;
        Check(FirebaseGateway.IdentityFromVerifiedClaims("fixture-uid", sdkPassword) is null,
            "SDK JObject claims for another provider must be rejected.");
        var sdkUnverified = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, object>>(
            """{"email_verified":false,"email":"google-user@test.invalid","firebase":{"sign_in_provider":"google.com"}}""")!;
        Check(FirebaseGateway.IdentityFromVerifiedClaims("fixture-uid", sdkUnverified) is null,
            "An SDK JObject must not bypass the email verification requirement.");
        var array = FirebaseGateway.IdentityFromVerifiedClaims("fixture-uid", Claims(new[] { "google.com" }));
        Check(array == standard, "A singleton Google provider array should match the standard string shape.");
        var jsonArray = Claims("google.com");
        using (var document = JsonDocument.Parse("""{"sign_in_provider":["google.com"]}""")) {
            jsonArray["firebase"] = document.RootElement.Clone();
        }
        Check(FirebaseGateway.IdentityFromVerifiedClaims("fixture-uid", jsonArray) == standard,
            "Firebase JsonElement array claims should not throw or change identity.");
        foreach (var provider in new object?[] {
            "password", new[] { "password" }, Array.Empty<string>(),
            new[] { "google.com", "password" }, new[] { "password", "google.com" },
            new[] { "google.com", "google.com" }, null, true, 42,
            new object[] { 42 }, new Dictionary<string, string> { ["value"] = "google.com" }
        }) {
            Check(FirebaseGateway.IdentityFromVerifiedClaims("fixture-uid", Claims(provider)) is null,
                "Unexpected or non-Google provider claims must be rejected without exceptions.");
        }
        var missingProvider = Claims("google.com");
        missingProvider["firebase"] = new Dictionary<string, object>();
        Check(FirebaseGateway.IdentityFromVerifiedClaims("fixture-uid", missingProvider) is null,
            "Missing provider must be rejected.");
        foreach (var firebase in new object?[] { null, "google.com", Array.Empty<string>(), true, 42 }) {
            var malformed = Claims("google.com");
            malformed["firebase"] = firebase!;
            Check(FirebaseGateway.IdentityFromVerifiedClaims("fixture-uid", malformed) is null,
                "Non-object firebase claim must be rejected without an exception.");
        }
        foreach (var verified in new object?[] { false, "true", new[] { true }, null }) {
            var unverified = Claims("google.com");
            unverified["email_verified"] = verified!;
            Check(FirebaseGateway.IdentityFromVerifiedClaims("fixture-uid", unverified) is null,
                "Only an explicit verified email is accepted.");
        }
        foreach (var email in new object?[] { null, "", new[] { "google-user@test.invalid" } }) {
            var invalidEmail = Claims("google.com");
            invalidEmail["email"] = email!;
            Check(FirebaseGateway.IdentityFromVerifiedClaims("fixture-uid", invalidEmail) is null,
                "Missing, empty or malformed email must be rejected.");
        }
        Check(FirebaseGateway.IdentityFromVerifiedClaims("", Claims("google.com")) is null,
            "An empty Google UID must not be accepted.");
        var missingFirebase = Claims("google.com");
        missingFirebase.Remove("firebase");
        Check(FirebaseGateway.IdentityFromVerifiedClaims("fixture-uid", missingFirebase) is null,
            "Missing firebase claim must be rejected.");
        var noName = Claims("google.com");
        noName.Remove("name");
        Check(FirebaseGateway.IdentityFromVerifiedClaims("fixture-uid", noName)?.Name == "google-user",
            "Missing display name should use the verified email prefix.");
        return count;
    }
}
