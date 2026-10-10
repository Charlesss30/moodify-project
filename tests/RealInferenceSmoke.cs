using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using Microsoft.Extensions.Configuration;
using mood_recommendation.Data;
using mood_recommendation.Models;

public static class RealInferenceSmoke
{
    public static Task RunUnavailable(string root, string connection) => Run(root, connection, false);

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task CopyCollection(
        IMongoDatabase source, IMongoDatabase destination, string name, bool activeContentOnly = false)
    {
        var filter = activeContentOnly
            ? new BsonDocument("deleted", new BsonDocument("$ne", true))
            : FilterDefinition<BsonDocument>.Empty;
        var documents = await source.GetCollection<BsonDocument>(name)
            .Find(filter).ToListAsync();
        if (documents.Count > 0)
            await destination.GetCollection<BsonDocument>(name).InsertManyAsync(documents);
    }

    private static Process StartBackend(string root, string connection, string database, string key, string aiUrl, int port)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = Path.Combine(root, "backend"),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add(Environment.GetEnvironmentVariable("MOODIFY_BACKEND_DLL")
            ?? Path.Combine(root, "backend", "bin", "Debug", "net10.0", "mood_recommendation.dll"));
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        start.Environment["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}";
        start.Environment["Mongo__ConnectionString"] = connection;
        start.Environment["Mongo__Database"] = database;
        start.Environment["Jwt__Key"] = key;
        start.Environment["Spotify__ClientId"] = "";
        start.Environment["Spotify__ClientSecret"] = "";
        start.Environment["Omdb__ApiKey"] = "";
        start.Environment["AI__Enabled"] = "true";
        start.Environment["AI__ServiceUrl"] = aiUrl;
        var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start isolated backend.");
        process.OutputDataReceived += (_, _) => { };
        process.ErrorDataReceived += (_, _) => { };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    public static Task Run(string root, string connection) => Run(root, connection, true);

    private static async Task Run(string root, string connection, bool requireLiveInference)
    {
        var database = "moodify_real_ai_" + Guid.NewGuid().ToString("N");
        var mongo = new MongoClient(connection);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Mongo:Database"] = database })
            .Build();
        var store = new MongoStore(mongo, config);
        var source = mongo.GetDatabase("mood_recommendation");
        var destination = mongo.GetDatabase(database);
        var aiUrl = Environment.GetEnvironmentVariable("MOODIFY_AI_SERVICE_URL") ?? "http://127.0.0.1:8001/";
        Process? backend = null;
        try
        {
            using var ai = new HttpClient { BaseAddress = new Uri(aiUrl), Timeout = TimeSpan.FromSeconds(15) };
            JsonNode? ready = null;
            if (requireLiveInference)
            {
                using var health = await ai.GetAsync("health/ready");
                health.EnsureSuccessStatusCode();
                ready = JsonNode.Parse(await health.Content.ReadAsStringAsync())
                    ?? throw new InvalidDataException("AI readiness response is empty.");
                if (ready["model_version"]?.GetValue<string>() != "roberta_large_asl_en_01"
                    || ready["inference_device"]?.GetValue<string>() is not ("cpu" or "cuda")
                    || ready["artifact_hash"]?.GetValue<string>() != "ea865317d9eb7cf1b39008062276bae441801a7d719d33177a3ddd1851af7b09")
                    throw new InvalidOperationException("Live AI service readiness does not match the allowlisted artifact.");
            }
            else
            {
                try
                {
                    using var health = await ai.GetAsync("health/ready");
                    throw new InvalidOperationException($"Expected stopped AI service, but readiness returned {(int)health.StatusCode}.");
                }
                catch (HttpRequestException) { }
            }

            await store.Initialize(Path.Combine(root, "backend", "Data", "mongo-schema.json"));
            foreach (var collection in new[] { "moods", "genres", "movies", "music" })
                await CopyCollection(source, destination, collection);
            await CopyCollection(source, destination, "contents", activeContentOnly: true);
            var happy = await store.TamTrangs.FirstOrDefaultAsync(mood => mood.TenTamTrang == "Happy");
            if (happy is null) throw new InvalidOperationException("The live database snapshot has no Happy mood.");
            var adminPassword = "Real-AI-" + Guid.NewGuid().ToString("N");
            var userPassword = "Real-AI-" + Guid.NewGuid().ToString("N");
            await store.Insert(new TaiKhoan
            {
                TenDangNhap = "real_ai_admin",
                Email = "real_ai_admin@test.invalid",
                VaiTro = "Admin",
                MatKhau = BCrypt.Net.BCrypt.HashPassword(adminPassword)
            });
            await store.Insert(new TaiKhoan
            {
                TenDangNhap = "real_ai_user",
                Email = "real_ai_user@test.invalid",
                MatKhau = BCrypt.Net.BCrypt.HashPassword(userPassword)
            });
            if (!requireLiveInference)
            {
                var modelDirectory = Path.Combine(root, "code-train", "train-KLCN", "output",
                    "roberta_large_asl_en_01", "best_model");
                var configHash = Convert.ToHexString(SHA256.HashData(
                    await File.ReadAllBytesAsync(Path.Combine(modelDirectory, "inference_config.json"))))
                    .ToLowerInvariant();
                await destination.GetCollection<BsonDocument>("aiModels").InsertOneAsync(new BsonDocument
                {
                    ["_id"] = "roberta_large_asl_en_01",
                    ["displayName"] = "RoBERTa Large English GoEmotions",
                    ["task"] = "multi-label emotion classification",
                    ["language"] = "en",
                    ["artifactHash"] = "ea865317d9eb7cf1b39008062276bae441801a7d719d33177a3ddd1851af7b09",
                    ["configHash"] = configHash,
                    ["validationStatus"] = "validated",
                    ["validatedAt"] = DateTime.UtcNow,
                    ["validatedBy"] = 1
                });
                await destination.GetCollection<BsonDocument>("aiModelState").InsertOneAsync(new BsonDocument
                {
                    ["_id"] = "active",
                    ["activeVersion"] = "roberta_large_asl_en_01",
                    ["previousVersion"] = BsonNull.Value,
                    ["revision"] = 1L,
                    ["updatedAt"] = DateTime.UtcNow,
                    ["updatedBy"] = 1
                });
            }

            var port = FreePort();
            var key = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48));
            backend = StartBackend(root, connection, database, key, aiUrl, port);
            using var client = new HttpClient
            {
                BaseAddress = new Uri($"http://127.0.0.1:{port}"),
                Timeout = TimeSpan.FromSeconds(25)
            };
            var available = false;
            for (var attempt = 0; attempt < 80; attempt++)
            {
                if (backend.HasExited) throw new InvalidOperationException("Isolated backend exited at startup.");
                try
                {
                    using var response = await client.GetAsync("/api/Genres");
                    if (response.IsSuccessStatusCode) { available = true; break; }
                }
                catch (HttpRequestException) { }
                await Task.Delay(150);
            }
            if (!available) throw new TimeoutException("Isolated backend did not become ready.");

            async Task<JsonNode?> Call(string method, string path, object? body, int expectedStatus, string? token = null)
            {
                using var request = new HttpRequestMessage(new HttpMethod(method), "/api" + path);
                if (body is not null) request.Content = JsonContent.Create(body);
                if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using var response = await client.SendAsync(request);
                var text = await response.Content.ReadAsStringAsync();
                if ((int)response.StatusCode != expectedStatus)
                    throw new InvalidOperationException($"{method} {path}: expected {expectedStatus}, got {(int)response.StatusCode}: {text[..Math.Min(text.Length, 400)]}");
                return text.Length > 0 ? JsonNode.Parse(text) : null;
            }

            async Task<string> Login(string email, string password) =>
                (await Call("POST", "/Auth/login", new { identifier = email, matKhau = password }, 200))!["token"]!.GetValue<string>();

            var admin = await Login("real_ai_admin@test.invalid", adminPassword);
            var user = await Login("real_ai_user@test.invalid", userPassword);
            if (!requireLiveInference)
            {
                var modelStatus = await Call("GET", "/Models", null, 200, admin);
                if (modelStatus!["activeVersion"]!.GetValue<string>() != "roberta_large_asl_en_01"
                    || modelStatus["runtime"]!["runtimeStatus"]!.GetValue<string>() != "Unavailable")
                    throw new InvalidOperationException("Unavailable runtime was not shown separately from persistent Active state.");
                var unavailable = await Call("POST", "/MoodAnalysis",
                    new { text = "The service is stopped; manual fallback must remain available.", language = "en" }, 503, user);
                if (unavailable!["status"]!.GetValue<string>() != "unavailable")
                    throw new InvalidOperationException("Stopped Python service did not return explicit fallback status.");
                await Call("POST", "/Recommendation",
                    new { tamTrangID = happy.TamTrangID, limit = 2, source = "manual" }, 200, user);
                var fallbackHistory = await Call("GET", "/History", null, 200, user);
                if (fallbackHistory!["items"]!.AsArray().Count != 1
                    || fallbackHistory["items"]![0]!["analysisSource"] is not null
                    || fallbackHistory["items"]![0]!["decisionSource"]!.GetValue<string>() != "manual")
                    throw new InvalidOperationException("Manual fallback did not save one history row with manual provenance.");
                Console.WriteLine("PASS: stopped Python service → authenticated .NET unavailable response → manual recommendation on live-content snapshot; registry correctly reports active but runtime unavailable.");
                return;
            }

            var validation = await Call("POST", "/Models/roberta_large_asl_en_01/validate", null, 200, admin);
            if (validation!["validationStatus"]!.GetValue<string>() != "validated")
                throw new InvalidOperationException("Real-model validation did not persist.");
            await Call("POST", "/Models/roberta_large_asl_en_01/activate", new { expectedRevision = 0 }, 200, admin);

            var analysis = await Call("POST", "/MoodAnalysis",
                new { text = "I feel joyful and amused today.", language = "en" }, 200, user);
            if (analysis!["analysisId"] is null || analysis["candidates"]!.AsArray().Count != 5)
                throw new InvalidOperationException("The real model gateway response failed the analysis contract.");
            Console.WriteLine($"Real model via .NET: device={ready!["inference_device"]}, status={analysis["status"]}, topLabel={analysis["candidates"]![0]!["label"]}; user text was not logged.");

            var manual = await Call("POST", "/Recommendation",
                new { tamTrangID = happy.TamTrangID, limit = 5, source = "manual" }, 200, user);
            var confirmed = await Call("POST", "/Recommendation",
                new { tamTrangID = happy.TamTrangID, limit = 5, analysisId = analysis["analysisId"]!.GetValue<string>() }, 200, user);
            if (!FeatureTests.SameRanking(manual!["items"], confirmed!["items"])
                || manual["valence"]!.GetValue<decimal>() != confirmed["valence"]!.GetValue<decimal>()
                || manual["arousal"]!.GetValue<decimal>() != confirmed["arousal"]!.GetValue<decimal>())
                throw new InvalidOperationException("Real-model/manual same-mood ranking or VA differed.");
            var history = await Call("GET", "/History", null, 200, user);
            if (!history!["items"]!.AsArray().Any(item =>
                    item!["analysisId"]?.GetValue<string>() == analysis["analysisId"]!.GetValue<string>()
                    && item["analysisSource"]?.GetValue<string>() == "ai"))
                throw new InvalidOperationException("Real-model recommendation history provenance was not saved.");
            await Call("POST", "/Models/deactivate", new { expectedRevision = 1 }, 200, admin);
            var fallback = await Call("POST", "/MoodAnalysis",
                new { text = "AI is deactivated; manual mood selection remains available.", language = "en" }, 503, user);
            if (fallback!["status"]!.GetValue<string>() != "unavailable")
                throw new InvalidOperationException("Disabled AI did not return the explicit manual fallback.");
            await Call("POST", "/Recommendation",
                new { tamTrangID = happy.TamTrangID, limit = 2, source = "manual" }, 200, user);
            Console.WriteLine("PASS: actual Python model → authenticated .NET gateway → real-content snapshot recommendation/history, same-mood comparison, and disabled-registry manual fallback.");
        }
        finally
        {
            if (backend is { HasExited: false })
            {
                backend.Kill(entireProcessTree: true);
                await backend.WaitForExitAsync();
            }
            backend?.Dispose();
            if (database.StartsWith("moodify_real_ai_", StringComparison.Ordinal))
                await mongo.DropDatabaseAsync(database);
        }
    }
}
