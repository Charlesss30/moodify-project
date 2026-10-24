using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using Microsoft.Extensions.Configuration;
using mood_recommendation.Data;
using mood_recommendation.Models;
using mood_recommendation.Services;


var root = Environment.GetEnvironmentVariable("MOODIFY_TEST_ROOT") ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
var original=Environment.GetEnvironmentVariable("MONGO_URI") ?? "mongodb://127.0.0.1:27017";
if (args.Contains("--tmdb-live")) { await TmdbTests.Live(root);return; }
if (args.Contains("--live-read")) { await MongoReadCheck.Run(original);return; }
if (args.Contains("--browser-smoke")) { await BrowserSmoke.Run(root,original);return; }
if (args.Contains("--real-inference")) { await RealInferenceSmoke.Run(root,original);return; }
if (args.Contains("--real-inference-off")) { await RealInferenceSmoke.RunUnavailable(root,original);return; }
if (args.Length > 0) throw new ArgumentException("Supported options: --browser-smoke, --live-read, --real-inference, --real-inference-off. Mongo migration: node scripts/mongo-migrate.mjs --apply");
var database="moodify_test_"+Guid.NewGuid().ToString("N");
var mongo=new MongoClient(original);
var cfg=new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"Mongo:Database",database}}).Build();
var db=new MongoStore(mongo,cfg);
Process? server = null;
var log = new System.Text.StringBuilder();
var checks = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
try
{
    await db.Initialize(Path.Combine(root,"backend/Data/mongo-schema.json"));
    checks += await FeatureTests.Run();
    checks += await TmdbTests.Run();
    checks += await SpotifyTests.Run(db);
    checks += await ExternalCatalogTests.Run(db);
    var admin = new TaiKhoan { TenDangNhap = "test_admin", Email = "admin@test.invalid", VaiTro = "Admin", MatKhau = BCrypt.Net.BCrypt.HashPassword("Test-password-1") };
    var user = new TaiKhoan { TenDangNhap = "test_user", Email = "user@test.invalid", MatKhau = BCrypt.Net.BCrypt.HashPassword("Test-password-2") };
    var other = new TaiKhoan { TenDangNhap = "test_other", Email = "other@test.invalid", MatKhau = BCrypt.Net.BCrypt.HashPassword("Test-password-3") };
    var aiEngineer = new TaiKhoan { TenDangNhap = "test_ai_engineer", Email = "ai-engineer@test.invalid", VaiTro = "AIEngineer", MatKhau = BCrypt.Net.BCrypt.HashPassword("Test-password-4") };
    await db.Insert(admin);await db.Insert(user);await db.Insert(other);await db.Insert(aiEngineer);
    var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
    using var inference = new FakeInferenceServer(root);
    inference.Start();
    var key = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48));
    var start = new ProcessStartInfo("dotnet") {
        WorkingDirectory = Path.Combine(root, "backend"), UseShellExecute = false, CreateNoWindow = true,
        RedirectStandardOutput = true, RedirectStandardError = true
    };
    start.ArgumentList.Add((Environment.GetEnvironmentVariable("MOODIFY_BACKEND_DLL") ?? Path.Combine(root, "backend/bin/Debug/net10.0/mood_recommendation.dll")));
    start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
    start.Environment["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}";
    start.Environment["Mongo__ConnectionString"] = original;
    start.Environment["Mongo__Database"] = database;
    start.Environment["Jwt__Key"] = key;
    start.Environment["Spotify__ClientId"] = "";
    start.Environment["Tmdb__AccessToken"] = "";
    start.Environment["Spotify__ClientSecret"] = "";
    start.Environment["AI__Enabled"] = "true";
    start.Environment["AI__ServiceUrl"] = inference.BaseAddress.AbsoluteUri;
    server = Process.Start(start)!;
    server.OutputDataReceived += (_, e) => { lock (log) { if (e.Data != null) log.AppendLine(e.Data); } };
    server.ErrorDataReceived += (_, e) => { lock (log) { if (e.Data != null) log.AppendLine(e.Data); } };
    server.BeginOutputReadLine(); server.BeginErrorReadLine();
    using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(20) };
    var ready = false;
    for (var i = 0; i < 50; i++)
    {
        if (server.HasExited) throw new Exception("Backend exited during startup.");
        try { var response = await client.GetAsync("/api/Genres"); if (response.IsSuccessStatusCode) { ready = true; break; } } catch (HttpRequestException) { }
        await Task.Delay(100);
    }
    Check(ready, "Backend failed to start.");
    async Task<JsonNode?> Call(string method, string path, object? body, int status, string? token = null)
    {
        using var req = new HttpRequestMessage(new HttpMethod(method), "/api" + path);
        if (body != null) req.Content = JsonContent.Create(body);
        if (token != null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var res = await client.SendAsync(req);
        var text = await res.Content.ReadAsStringAsync();
        Check((int)res.StatusCode == status, $"{method} {path}: expected {status}, received {(int)res.StatusCode}. {text[..Math.Min(text.Length, 500)]}");
        return text.Length > 0 && res.Content.Headers.ContentType?.MediaType == "application/json" ? JsonNode.Parse(text) : null;
    }
    async Task<string> Login(string identifier, string password) =>
        (await Call("POST", "/Auth/login", new { identifier, matKhau = password }, 200))!["token"]!.GetValue<string>();

    await Call("POST", "/Auth/login", new { identifier = "", matKhau = "" }, 400);
    await Call("POST", "/Auth/login", new { identifier = user.Email, matKhau = "wrong" }, 401);
    var simultaneous=Enumerable.Range(0,8).Select(i=>new TaiKhoan{TenDangNhap="parallel_"+i,Email="parallel_"+i+"@test.invalid",MatKhau="fixture-not-used-for-login"}).ToArray();
    await Task.WhenAll(simultaneous.Select(x=>db.Insert(x)));
    Check(simultaneous.Select(x=>x.TaiKhoanID).Distinct().Count()==8,"Concurrent counter generated duplicate IDs.");
    try {await db.Insert(new TaiKhoan{TenDangNhap="duplicate_email",Email=admin.Email.ToUpperInvariant(),MatKhau="fixture"});throw new Exception("Duplicate email accepted.");}
    catch(MongoWriteException e){Check(e.WriteError.Category==ServerErrorCategory.DuplicateKey,"Email unique index not enforced.");}
    var at = await Login(admin.Email.ToUpperInvariant(), "Test-password-1");
    var ut = await Login(user.Email, "Test-password-2");
    var ot = await Login(other.Email, "Test-password-3");
    var eit = await Login(aiEngineer.Email, "Test-password-4");
    await AiWorkspaceTests.Run((method, path, body, status, token) => Call(method, path, body, status, token), Check, at, eit, ut);
    var modelStatus = await Call("GET", "/Models", null, 200, eit);
    Check(modelStatus!["runtime"]!["runtimeStatus"]!.GetValue<string>() == "Ready"
        && modelStatus["runtime"]!["inferenceDevice"]!.GetValue<string>() == "cpu",
        "Registry must report runtime readiness separately from active state.");
    await Call("GET", "/Models/audit", null, 200, eit);
    await Call("GET", "/Users", null, 403, eit);
    await Call("GET", "/Models", null, 403, ut);
    await Call("POST", "/MoodAnalysis", new { text = "I feel hopeful today", language = "en" }, 503, ut);
    await Call("POST", "/Models/roberta_large_asl_en_01/validate", null, 403, ut);
    var validation = await Call("POST", "/Models/roberta_large_asl_en_01/validate", null, 200, eit);
    Check(validation!["validationStatus"]!.GetValue<string>() == "validated",
        "Registry did not persist model validation.");
    await Call("POST", "/Models/not-allowlisted/validate", null, 404, eit);
    await Call("POST", "/Models/roberta_large_asl_en_01/activate", new { expectedRevision = 0 }, 403, ut);
    var activated = await Call("POST", "/Models/roberta_large_asl_en_01/activate", new { expectedRevision = 0 }, 200, eit);
    Check(activated!["revision"]!.GetValue<long>() == 1, "Model registry activation did not advance revision.");
    await Call("POST", "/Models/roberta_large_asl_en_01/activate", new { expectedRevision = 0 }, 409, at);
    var unsupported = await Call("POST", "/MoodAnalysis", new { text = "Tôi thấy vui", language = "vi" }, 200, ut);
    Check(unsupported!["status"]!.GetValue<string>() == "unsupported_language"
        && unsupported["analysisId"] == null, "Unsupported language must not create an analysis token.");
    // Default user-host login hands an admin session to the dashboard once.
    using (var browser = new HttpClient(new HttpClientHandler { UseCookies = false }) { BaseAddress = client.BaseAddress })
    {
        async Task<string?> PendingCookie(string email, string password)
        {
            using var response = await browser.PostAsJsonAsync("/api/Auth/login", new { identifier = email, matKhau = password });
            Check(response.IsSuccessStatusCode, "Handoff login failed.");
            Check(response.Headers.CacheControl?.NoStore == true, "Login must not be cached.");
            if (!response.Headers.TryGetValues("Set-Cookie", out var headers)) return null;
            var issued = headers.LastOrDefault(x => x.StartsWith(AdminLoginHandoff.CookieName + "=", StringComparison.Ordinal)
                && x.Split(';')[0].Length > AdminLoginHandoff.CookieName.Length + 1);
            if (issued == null) return null;
            Check(issued.Contains("httponly", StringComparison.OrdinalIgnoreCase), "Handoff cookie must be HttpOnly.");
            Check(issued.Contains("samesite=strict", StringComparison.OrdinalIgnoreCase), "Handoff cookie must be SameSite Strict.");
            Check(issued.Contains("max-age=60", StringComparison.OrdinalIgnoreCase), "Handoff must expire in one minute.");
            return issued.Split(';')[0];
        }
        async Task<string?> Redeem(string? cookie, int expected)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/Auth/admin-session");
            if (cookie != null) req.Headers.Add("Cookie", cookie);
            using var response = await browser.SendAsync(req);
            Check((int)response.StatusCode == expected, "Unexpected handoff response: " + response.StatusCode);
            if (cookie != null)
            {
                var cookies = new CookieContainer();
                cookies.SetCookies(browser.BaseAddress!, cookie + "; Path=" + AdminLoginHandoff.CookiePath);
                if (response.Headers.TryGetValues("Set-Cookie", out var cleared))
                    foreach (var header in cleared) cookies.SetCookies(browser.BaseAddress!, header);
                Check(cookies.GetCookieHeader(new Uri(browser.BaseAddress!, AdminLoginHandoff.CookiePath)) == "",
                    "Redeeming must remove the handoff cookie so reloading keeps the JWT session.");
            }
            if (expected != 200) return null;
            var body = await response.Content.ReadFromJsonAsync<JsonNode>();
            return body!["token"]!.GetValue<string>();
        }
        await Redeem(null, 204);
        var cookie = await PendingCookie(admin.Email, "Test-password-1");
        Check(cookie != null, "Admin login must issue a browser handoff.");
        var jar = new CookieContainer();
        jar.SetCookies(client.BaseAddress!, cookie! + "; Path=" + AdminLoginHandoff.CookiePath);
        var dashboardUri = new UriBuilder(client.BaseAddress!) { Port = 5174, Path = AdminLoginHandoff.CookiePath }.Uri;
        Check(jar.GetCookieHeader(dashboardUri).Contains(cookie!), "Handoff must be available on the admin port.");
        var dashboardToken = await Redeem(cookie, 200);
        await Call("GET", "/Dashboard", null, 200, dashboardToken);
        var identity = await Call("GET", "/Auth/me", null, 200, dashboardToken);
        Check(identity!["taiKhoanID"]!.GetValue<int>() == admin.TaiKhoanID, "Handoff changed identity.");
        await Redeem(cookie, 401);
        await Redeem(AdminLoginHandoff.CookieName + "=forged", 401);
        Check(await PendingCookie(user.Email, "Test-password-2") == null, "Regular users must not get an admin handoff.");

        var engineerCookie = await PendingCookie(aiEngineer.Email, "Test-password-4");
        Check(engineerCookie != null, "AIEngineer login must issue a workspace handoff.");
        var engineerToken = await Redeem(engineerCookie, 200);
        var engineerIdentity = await Call("GET", "/Auth/me", null, 200, engineerToken);
        Check(engineerIdentity!["vaiTro"]!.GetValue<string>() == "AIEngineer", "Handoff must preserve engineer role.");
        await Call("GET", "/AiWorkspace/jobs", null, 200, engineerToken);
        await Call("GET", "/Users", null, 403, engineerToken);
        await Redeem(engineerCookie, 401);
        var revokedEngineerCookie = await PendingCookie(aiEngineer.Email, "Test-password-4");
        aiEngineer.VaiTro = "User"; await db.Update(aiEngineer);
        await Redeem(revokedEngineerCookie, 403);
        aiEngineer.VaiTro = "AIEngineer"; await db.Update(aiEngineer);

        var lockedCookie = await PendingCookie(admin.Email, "Test-password-1");
        admin.TrangThai = false; await db.Update(admin);
        await Redeem(lockedCookie, 403);
        admin.TrangThai = true; await db.Update(admin);

        var changedRoleCookie = await PendingCookie(admin.Email, "Test-password-1");
        admin.VaiTro = "User"; await db.Update(admin);
        await Redeem(changedRoleCookie, 403);
        admin.VaiTro = "Admin"; await db.Update(admin);
    }

    await Call("GET", "/Spotify?query=test", null, 401);
    await Call("GET", "/Spotify?query=test", null, 403, ut);
    await Call("GET", "/Spotify?query=test", null, 503, at);
    await Call("POST", "/Spotify/import", new { songIds = new[] { "../bad" }, tamTrangID = 1 }, 400, at);
    await Call("GET", "/Tmdb/lookup?query=tt3896198", null, 401);
    await Call("GET", "/Tmdb/lookup?query=tt3896198", null, 403, ut);
    await Call("GET", "/Tmdb/lookup?query=tt3896198", null, 503, at);
    await Call("GET", "/Tmdb/search?query=movie&year=1", null, 400, at);
    await Call("GET", "/Zing", null, 404, at);
    await Call("GET", "/Spotify?query=test&limit=20", null, 400, at);
    await Call("GET", "/Spotify/status", null, 503, at);
    await Call("GET", "/Spotify/11dFghVXANMlKmJXsNCbNl/preview", null, 403, ut);
    var embed = await Call("GET", "/Spotify/11dFghVXANMlKmJXsNCbNl/preview", null, 200, at);
    Check(embed!["mode"]!.GetValue<string>() == "embed" && embed["url"] == null, "Spotify must not claim a direct MP3 stream.");
    await Call("POST", "/Spotify/import", new { songIds = new[] { "11dFghVXANMlKmJXsNCbNl" }, tamTrangID = 999999 }, 404, at);
    await Call("GET", "/Auth/me", null, 200, at);
    foreach (var path in new[] { "/Users", "/Dashboard", "/Test/accounts", "/History", "/Models" })
        await Call("GET", path, null, 401);
    foreach (var path in new[] { "/Users", "/Dashboard", "/Test/accounts", "/Models" })
        await Call("GET", path, null, 403, ut);
    await Call("GET", "/Auth/me", null, 401, at + "tampered");
    await Call("GET", "/Auth/me", null, 401, new TokenService(key).Create(admin, DateTime.UtcNow.AddMinutes(-5)));
    var users = await Call("GET", "/Users", null, 200, at);
    Check(!users!.ToJsonString().Contains("matKhau", StringComparison.OrdinalIgnoreCase), "Password fields exposed.");

    await Call("POST", "/Genres", new { tenTheLoai = "Drama" }, 401);
    await Call("POST", "/Genres", new { tenTheLoai = "Drama" }, 403, ut);
    var genre = await Call("POST", "/Genres", new { tenTheLoai = "Drama", contentType="Movie", defaultValence=-.2, defaultArousal=.1 }, 201, at);
    var gid = genre!["theLoaiID"]!.GetValue<string>();
    Check(gid.StartsWith("GENRE_"), "Genre ID is not server-generated.");
    await Call("PUT", "/Genres/g123", new { tenTheLoai = "x" }, 404, at);
    await Call("PUT", $"/Genres/{gid}", new { tenTheLoai = "Drama 2" }, 200, at);
    var g2 = await Call("POST", "/Genres", new { tenTheLoai = "Comedy", contentType="Movie", defaultValence=.7, defaultArousal=.4 }, 201, at);
    await Call("PUT", $"/Genres/{gid}", new { tenTheLoai = "Comedy" }, 409, at);

    await Call("GET", "/Movies/mood-suggestion?genres=Comedy", null, 401);
    await Call("GET", "/Movies/mood-suggestion?genres=Comedy", null, 403, ut);
    await Call("POST", "/Genres", new {tenTheLoai="Action",contentType="Movie",defaultValence=.1,defaultArousal=.8},201,at);
    var suggestion = await Call("GET", "/Movies/mood-suggestion?genres=Action,Comedy,action,Unknown", null, 200, at);
    Check(suggestion!["valence"]!.GetValue<decimal>() == .4m && suggestion["arousal"]!.GetValue<decimal>() == .6m, "Genre averaging/dedup failed.");
    await Call("POST", "/Movies", new { noiDungID="mood-unknown", tieuDe="Unknown", theLoai="Unknown" }, 400, at);
    await Call("POST", "/Movies", new { noiDungID="mood-invalid", tieuDe="Invalid", valence=2, arousal=0 }, 400, at);
    await Call("POST", "/Movies", new { noiDungID="mood-partial", tieuDe="Partial", valence=.5m }, 400, at);
    var autoMovie = await Call("POST", "/Movies", new { noiDungID="mood-auto", tieuDe="Auto", theLoai="Comedy" }, 201, at);
    Check(autoMovie!["valence"]!.GetValue<decimal>()==.7m && autoMovie["phim"]!["moodSource"]!.GetValue<string>()=="genre-default", "Automatic mood not persisted.");
    var manualMovie = await Call("PUT", "/Movies/mood-auto", new { noiDungID="mood-auto", tieuDe="Manual", theLoai="Comedy", valence=-.8m, arousal=-.2m, moodSource="genre-rule-v1" }, 200, at);
    Check(manualMovie!["phim"]!["moodSource"]!.GetValue<string>()=="manual", "Edited value incorrectly claims rule provenance.");
    var preservedMovie = await Call("PUT", "/Movies/mood-auto", new { noiDungID="mood-auto", tieuDe="Metadata", theLoai="Horror" }, 200, at);
    Check(preservedMovie!["valence"]!.GetValue<decimal>()==-.8m, "Metadata update overwrote mood.");
    await Call("DELETE", "/Movies/mood-auto", null, 204, at);
    var movie = new { noiDungID = "test-movie", tieuDe = "Test Movie", director = "Fixture Director", theLoai = "Drama 2", valence = 0m, arousal = 0m, diemDanhGiaTB = 10, hinhAnh = "https://example.com/poster.jpg" };
    await Call("POST", "/Movies", movie, 403, ut);
    await Call("POST", "/Movies", movie, 201, at);
    await Call("DELETE", $"/Genres/{gid}", null, 409, at);
    await Call("PUT", $"/Genres/{gid}", new {tenTheLoai="Drama renamed"}, 200, at);
    var renamed=await Call("GET", "/Movies/test-movie", null,200,at);
    Check(renamed!["phim"]!["theLoai"]!.GetValue<string>()=="Drama renamed","Genre rename not reflected through references.");
    await Call("PUT", $"/Genres/{gid}", new {tenTheLoai="Drama 2"}, 200, at);
    await db.Database.GetCollection<MongoDB.Bson.BsonDocument>("movies").UpdateOneAsync(new MongoDB.Bson.BsonDocument("_id","test-movie"),new MongoDB.Bson.BsonDocument("$set",new MongoDB.Bson.BsonDocument("legacyNote","preserve-me")));
    await Call("PUT", "/Movies/test-movie", movie, 200, at);
    var preserved=await db.Database.GetCollection<MongoDB.Bson.BsonDocument>("movies").Find(new MongoDB.Bson.BsonDocument("_id","test-movie")).FirstAsync();
    Check(preserved["legacyNote"].AsString=="preserve-me","Unmapped fields were lost on update.");
    try {await db.Database.GetCollection<MongoDB.Bson.BsonDocument>("contents").UpdateOneAsync(new MongoDB.Bson.BsonDocument("_id","test-movie"),new MongoDB.Bson.BsonDocument("$set",new MongoDB.Bson.BsonDocument("valence",2)));throw new Exception("Validator accepted invalid coordinates.");}
    catch(MongoWriteException e){Check(e.WriteError.Code==121,"Missing database coordinate validator.");}
    await Call("POST", "/Music", new { noiDungID = "test-song", tieuDe = "Test Song", duration = 210, valence=.2, arousal=.3 }, 201, at);

    await Call("POST", "/Genres", new {tenTheLoai="Invalid pair",defaultValence=.3},400,at);
    await Call("POST", "/Genres", new {tenTheLoai="Invalid range",defaultValence=2,defaultArousal=.3},400,at);
    var configured = await Call("POST", "/Genres", new {tenTheLoai="Test Chill",contentType="Song",defaultValence=.3,defaultArousal=-.5},201,at);
    var configId=configured!["theLoaiID"]!.GetValue<string>();
    await Call("POST", "/Music", new {noiDungID="missing-label",tieuDe="Unknown",genre="Unmapped"},400,at);
    await Call("POST", "/Music", new {noiDungID="partial-label",tieuDe="Partial",genre="Test Chill",valence=.2},400,at);
    var wrongType=await Call("GET","/Movies/mood-suggestion?genres=Test%20Chill",null,200,at);
    Check(wrongType!["valence"]==null,"Song default leaked into movies.");
    var generated=await Call("POST","/Music",new {noiDungID="genre-song",tieuDe="Automatic song",genre="Test Chill"},201,at);
    Check(generated!["valence"]!.GetValue<decimal>()==.3m && generated["arousal"]!.GetValue<decimal>()==-.5m && generated["nhac"]!["moodSource"]!.GetValue<string>()=="genre-default","Music defaults not applied.");
    Check(generated["reviewStatus"]!.GetValue<string>()=="pending" && generated["moodRuleVersion"]!.GetValue<string>().EndsWith(":1"),"Automatic provenance missing.");
    await Call("PUT","/Genres/"+configId,new {tenTheLoai="Test Chill",defaultValence=.8,defaultArousal=.2},200,at);
    var preservedSong=await Call("GET","/Music/genre-song",null,200,at);
    Check(preservedSong!["valence"]!.GetValue<decimal>()==.3m,"Genre change rewrote existing song.");
    var newer=await Call("GET","/Music/mood-suggestion?genres=Test%20Chill",null,200,at);
    Check(newer!["valence"]!.GetValue<decimal>()==.8m && newer["ruleVersion"]!.GetValue<string>().EndsWith(":2"),"Updated defaults/version not used.");
    var overrideSong=await Call("PUT","/Music/genre-song",new {noiDungID="genre-song",tieuDe="Manual song",genre="Test Chill",valence=-.6,arousal=.1,moodSource="manual"},200,at);
    Check(overrideSong!["reviewStatus"]!.GetValue<string>()=="reviewed" && overrideSong["moodRuleVersion"]==null,"Manual provenance incorrect.");
    await Call("PUT","/Music/genre-song",new {noiDungID="genre-song",tieuDe="Metadata edit",genre="Test Chill"},200,at);
    var kept=await Call("GET","/Music/genre-song",null,200,at);
    Check(kept!["valence"]!.GetValue<decimal>()==-.6m,"Metadata edit overwrote manual annotation.");
    await Call("PUT","/Genres/"+configId,new {tenTheLoai="Test Chill",clearMoodDefaults=true},200,at);
    var cleared=await Call("GET","/Music/mood-suggestion?genres=Test%20Chill",null,200,at);
    Check(cleared!["valence"]==null,"Cleared defaults still applied.");
    await Call("DELETE","/Music/genre-song",null,204,at);
    await Call("DELETE","/Genres/"+configId,null,204,at);
    await Call("GET", "/Music/test-song/preview", null, 401);
    await Call("GET", "/Music/test-song/preview", null, 422, ut);
    await Call("PUT", "/Music/test-song", new { noiDungID = "test-song", tieuDe = "Edited Song", duration = 220 }, 200, at);
    await Call("PUT", "/MoodMapping/content/test-movie", new { valence = 2, arousal = 0 }, 400, at);
    await Call("PUT", "/MoodMapping/content/test-movie", new { valence = 0.75, arousal = 0.65 }, 200, at);
    await Call("PUT", "/MoodMapping/content/test-song", new { valence = -0.8, arousal = -0.7 }, 200, at);
    await Call("POST", "/MoodMapping", new { tenTamTrang = "Invalid", minValence = 1, maxValence = -1 }, 400, at);
    var mood = await Call("POST", "/MoodMapping", new { tenTamTrang = "Happy", minValence = 0.5, maxValence = 1, minArousal = 0.4, maxArousal = 0.9 }, 201, at);
    var mid = mood!["tamTrangID"]!.GetValue<int>();
    var sadMood = await Call("POST", "/MoodMapping",
        new { tenTamTrang = "Sad", minValence = -0.8, maxValence = -0.4, minArousal = -0.6, maxArousal = -0.2 }, 201, at);
    var sadId = sadMood!["tamTrangID"]!.GetValue<int>();
    var recommendation = await Call("POST", "/Recommendation", new { tamTrangID = mid, limit = 2 }, 200, ut);
    Check(recommendation!["items"]![0]!["content"]!["noiDungID"]!.GetValue<string>() == "test-movie", "Wrong nearest content.");
    Check(recommendation["items"]![0]!["match"]!.GetValue<double>() == 100, "Exact mood match must be 100%.");
    Check((await Call("GET", "/History", null, 200, ut))!["total"]!.GetValue<int>() == 1, "History not saved.");
    Check((await Call("GET", "/History", null, 200, ot))!["total"]!.GetValue<int>() == 0, "History leaked across accounts.");
    var historySnapshot=(await Call("GET", "/History", null, 200, ut))!["items"]![0]!;
    Check(historySnapshot["recommendedItems"]!.AsArray().Count==2,"Missing recommendation snapshot.");
    Check(historySnapshot["recommendedItems"]![0]!["noiDungID"]!.GetValue<string>()=="test-movie","Snapshot ranking changed.");
    Check(historySnapshot["algorithm"]!.GetValue<string>()=="mood-euclidean-v1" && historySnapshot["source"]!.GetValue<string>()=="manual","Missing history provenance.");
    await Call("POST", "/Recommendation", new {tamTrangID=mid,source="fabricated-ai"},400,ut);

    inference.Scenario = "matched";
    var analysis = await Call("POST", "/MoodAnalysis", new { text = "I feel joyful and amused today.", language = "en" }, 200, ut);
    Check(analysis!["status"]!.GetValue<string>() == "matched"
        && analysis["suggestedMoodId"]!.GetValue<int>() == mid,
        "Approved experimental joy/amusement labels did not resolve to the live fixture mood ID.");
    Check(analysis["candidates"]!.AsArray().Count == 5
        && analysis["decisionCandidates"]!.AsArray().Count == 2
        && analysis["candidates"]![0]!["decisionEligible"]!.GetValue<bool>(),
        "Display candidates were not separated from threshold-qualified decision candidates.");
    var analysisId = analysis["analysisId"]!.GetValue<string>();
    var manualComparison = await Call("POST", "/Recommendation",
        new { tamTrangID = mid, limit = 2, source = "manual" }, 200, ut);
    var aiRecommendation = await Call("POST", "/Recommendation",
        new { tamTrangID = mid, limit = 2, analysisId }, 200, ut);
    Check(FeatureTests.SameRanking(manualComparison!["items"], aiRecommendation!["items"])
        && manualComparison["valence"]!.GetValue<decimal>() == aiRecommendation["valence"]!.GetValue<decimal>()
        && manualComparison["arousal"]!.GetValue<decimal>() == aiRecommendation["arousal"]!.GetValue<decimal>(),
        "Manual and AI-confirmed same-mood recommendation must share midpoint VA and ranking.");
    var aiHistory = (await Call("GET", "/History", null, 200, ut))!["items"]!.AsArray()
        .Select(item => item!.AsObject()).First(item => item["analysisId"]?.GetValue<string>() == analysisId);
    Check(aiHistory["analysisSource"]!.GetValue<string>() == "ai"
        && aiHistory["decisionSource"]!.GetValue<string>() == "ai"
        && aiHistory["algorithm"]!.GetValue<string>() == "mood-euclidean-v1"
        && !string.IsNullOrEmpty(aiHistory["moodConfigHash"]!.GetValue<string>()),
        "AI recommendation history is missing source/configuration provenance.");
    await Call("POST", "/Recommendation", new { tamTrangID = mid, limit = 2, analysisId }, 200, ut);
    Check((await Call("GET", "/History", null, 200, ut))!["total"]!.GetValue<int>() == 3,
        "Retry of a completed analysis duplicated history.");
    await Call("POST", "/Recommendation", new { tamTrangID = mid, limit = 2, analysisId }, 409, ot);
    await Call("POST", "/Recommendation", new { tamTrangID = mid, limit = 3, analysisId }, 409, ut);
    await Call("POST", "/Recommendation", new { tamTrangID = mid, limit = 2, loaiNoiDung = "Music", analysisId }, 409, ut);
    await Call("POST", "/Recommendation", new { tamTrangID = mid, limit = 2, balanced = true, analysisId }, 409, ut);
    var staleAnalysis = await Call("POST", "/MoodAnalysis", new { text = "Mood settings must remain current.", language = "en" }, 200, ut);
    var staleAnalysisId = staleAnalysis!["analysisId"]!.GetValue<string>();
    await Call("PUT", $"/MoodMapping/{mid}",
        new { tenTamTrang = "Happy", minValence = 0.51, maxValence = 1, minArousal = 0.4, maxArousal = 0.9 }, 200, at);
    await Call("POST", "/Recommendation", new { tamTrangID = mid, limit = 2, analysisId = staleAnalysisId }, 409, ut);
    await Call("PUT", $"/MoodMapping/{mid}",
        new { tenTamTrang = "Happy", minValence = 0.5, maxValence = 1, minArousal = 0.4, maxArousal = 0.9 }, 200, at);

    inference.Scenario = "low";
    var lowDecision = await Call("POST", "/MoodAnalysis", new { text = "A low selective-confidence case.", language = "en" }, 200, ut);
    Check(lowDecision!["status"]!.GetValue<string>() == "unknown"
        && lowDecision["suggestedMoodId"] == null
        && lowDecision["candidates"]![0]!["decisionEligible"]!.GetValue<bool>(),
        "Top display score below selective policy must not automatically suggest a mood.");
    inference.Scenario = "ambiguous";
    var ambiguous = await Call("POST", "/MoodAnalysis", new { text = "Strong opposing threshold-qualified emotions.", language = "en" }, 200, ut);
    Check(ambiguous!["status"]!.GetValue<string>() == "ambiguous"
        && ambiguous["suggestedMoodId"] == null
        && ambiguous["decisionCandidates"]!.AsArray().Count == 2,
        $"Conflicting mapped labels above their thresholds must remain ambiguous (status={ambiguous["status"]}, suggestion={ambiguous["suggestedMoodId"]}, candidates={ambiguous["decisionCandidates"]}).");
    inference.Scenario = "unmapped-leading";
    var unmappedLeading = await Call("POST", "/MoodAnalysis", new { text = "An unmapped leading emotion.", language = "en" }, 200, ut);
    Check(unmappedLeading!["status"]!.GetValue<string>() == "unknown"
        && unmappedLeading["suggestedMoodId"] == null,
        "A mapped lower-ranked emotion must not bypass an unmapped leading label.");
    inference.Scenario = "malformed";
    var malformed = await Call("POST", "/MoodAnalysis", new { text = "Malformed response.", language = "en" }, 503, ut);
    Check(malformed!["status"]!.GetValue<string>() == "unavailable"
        && malformed["analysisId"] == null, "Malformed model output must fail closed without issuing a token.");
    await Call("POST", "/Models/roberta_large_asl_en_01/validate", null, 409, eit);
    var failedSmokeAudit = (await Call("GET", "/Models/audit", null, 200, eit))!.AsArray();
    Check(failedSmokeAudit.Any(item => item!["action"]!.GetValue<string>() == "validate"
        && item["outcome"]!.GetValue<string>() == "rejected"),
        "A failed live inference smoke test must be audited and must prevent validation.");
    inference.Scenario = "matched";
    inference.StatusCode = 422;
    await Call("POST", "/MoodAnalysis", new { text = "Rejected by inference validation.", language = "en" }, 200, ut);
    inference.StatusCode = 429;
    var busy = await Call("POST", "/MoodAnalysis", new { text = "Busy inference.", language = "en" }, 503, ut);
    Check(busy!["status"]!.GetValue<string>() == "unavailable", "429 must preserve manual fallback.");
    inference.StatusCode = 503;
    await Call("POST", "/MoodAnalysis", new { text = "Unavailable inference.", language = "en" }, 503, ut);
    inference.StatusCode = 200;
    inference.DelayMilliseconds = 11000;
    var timedOut = await Call("POST", "/MoodAnalysis", new { text = "Timeout inference.", language = "en" }, 503, ut);
    Check(timedOut!["status"]!.GetValue<string>() == "unavailable", "Inference timeout must be explicit and keep fallback.");
    inference.DelayMilliseconds = 0;

    var manualOverrideAnalysis = await Call("POST", "/MoodAnalysis", new { text = "I feel joyful.", language = "en" }, 200, ut);
    var manualOverrideId = manualOverrideAnalysis!["analysisId"]!.GetValue<string>();
    await Call("POST", "/Recommendation", new { tamTrangID = sadId, limit = 2, analysisId = manualOverrideId }, 200, ut);
    var manualOverrideHistory = (await Call("GET", "/History", null, 200, ut))!["items"]!.AsArray()
        .Select(item => item!.AsObject()).First(item => item["analysisId"]?.GetValue<string>() == manualOverrideId);
    Check(manualOverrideHistory["decisionSource"]!.GetValue<string>() == "manual"
        && manualOverrideHistory["analysisSource"]!.GetValue<string>() == "ai",
        "Changing an AI-suggested mood must retain AI analysis provenance and record a manual decision.");
    await Call("POST", "/Models/deactivate", new { expectedRevision = 0 }, 409, at);
    await Call("POST", "/Models/deactivate", new { expectedRevision = 1 }, 403, ut);
    await Call("POST", "/Models/deactivate", new { expectedRevision = 1 }, 200, at);
    var disabledAnalysis = await Call("POST", "/MoodAnalysis", new { text = "AI is deactivated.", language = "en" }, 503, ut);
    Check(disabledAnalysis!["status"]!.GetValue<string>() == "unavailable",
        "Deactivating the registry model must disable user-facing AI analysis.");
    await Call("POST", "/Recommendation", new { tamTrangID = mid, limit = 2, source = "manual" }, 200, ut);
    await Call("POST", "/Models/rollback", new { expectedRevision = 2 }, 200, eit);
    var restoredRegistry = await Call("GET", "/Models", null, 200, eit);
    Check(restoredRegistry!["activeVersion"]!.GetValue<string>() == "roberta_large_asl_en_01"
        && restoredRegistry["revision"]!.GetValue<long>() == 3,
        "Rollback did not restore the last validated and ready model.");
    var registryAudit = await Call("GET", "/Models/audit", null, 200, eit);
    Check(registryAudit!.AsArray().Any(item => item!["action"]!.GetValue<string>() == "rollback"
        && item["outcome"]!.GetValue<string>() == "succeeded"),
        "Registry rollback audit event is missing.");

    var quizFixtures = Enumerable.Range(0,4).SelectMany(i=>new[] {
        new NoiDungGiaiTri {NoiDungID=$"quiz-movie-{i}",TieuDe=$"Quiz movie {i}",LoaiNoiDung="Movie",Valence=.75m,Arousal=.65m},
        new NoiDungGiaiTri {NoiDungID=$"quiz-song-{i}",TieuDe=$"Quiz song {i}",LoaiNoiDung="Song",Valence=-.8m,Arousal=-.7m}
    }).ToList();
    foreach(var fixture in quizFixtures) await db.Insert(fixture);
    var balanced=await Call("POST", "/Recommendation", new {tamTrangID=mid,limit=6,balanced=true},200,ut);
    var balancedItems=balanced!["items"]!.AsArray();
    Check(balancedItems.Count==6 && balancedItems.Count(x=>x!["content"]!["loaiNoiDung"]!.GetValue<string>()=="Movie")==3 && balancedItems.Count(x=>x!["content"]!["loaiNoiDung"]!.GetValue<string>()=="Song")==3,"Balanced recommendations must include three of each type even when movies score higher.");
    Check((await Call("GET", "/History", null, 200, ut))!["total"]!.GetValue<int>()==6,"Quiz must save only one history entry.");
    foreach(var fixture in quizFixtures) await db.Delete(fixture);
    await Call("DELETE", $"/MoodMapping/{mid}", null, 409, at);
    await Call("POST", "/History/ratings", new { noiDungID = "test-movie", soSao = 6 }, 400, ut);
    var rating = await Call("POST", "/History/ratings", new { noiDungID = "test-movie", soSao = 5, nhanXet = "Good" }, 200, ut);
    var ratedMovie = await Call("GET", "/Movies/test-movie", null, 200, ut);
    Check(ratedMovie!["phim"]!["director"]!.GetValue<string>()=="Fixture Director", "Director missing after movie edits.");
    var rid = rating!["danhGiaID"]!.GetValue<long>();
    var feedback=(await Call("GET","/Dashboard/feedback",null,200,at))!["items"]![0]!;
    Check(feedback["content"]!["id"]!.GetValue<string>()=="test-movie" && !string.IsNullOrWhiteSpace(feedback["content"]!["title"]!.GetValue<string>()),"Feedback missing content metadata.");
    Check(feedback["content"]!.AsObject().ContainsKey("imageUrl") && feedback["username"]!=null,"Feedback missing artwork or user identity.");
    await Call("GET","/Dashboard/feedback",null,403,ut);
    await Call("DELETE", $"/History/ratings/{rid}", null, 404, ot);
    Check((await Call("GET", "/Dashboard", null, 200, at))!["ratings"]!.GetValue<int>() == 1, "Dashboard count mismatch.");
    await Call("DELETE", $"/History/ratings/{rid}", null, 204, ut);
    await Call("PUT", $"/Users/{admin.TaiKhoanID}/access", new { vaiTro = "User", trangThai = false }, 400, at);
    await Call("PUT", $"/Users/{user.TaiKhoanID}/access", new { vaiTro = "User", trangThai = false }, 200, at);
    await Call("GET", "/Auth/me", null, 401, ut);
    await Call("POST", "/Auth/login", new { identifier = user.Email, matKhau = "Test-password-2" }, 401);
    await db.Insert(new DanhGia{TaiKhoanID=admin.TaiKhoanID,NoiDungID="test-movie",SoSao=4,ThoiGian=DateTime.UtcNow});
    await Call("DELETE", "/Movies/test-movie", null, 204, at);
    Check(!await db.DanhGias.AnyAsync(x=>x.NoiDungID=="test-movie")&&!await db.Phims.AnyAsync(x=>x.NoiDungID=="test-movie"),"Content delete left related records.");
    await Call("DELETE", "/Music/test-song", null, 204, at);
    await Call("DELETE", $"/Genres/{gid}", null, 204, at);
    await Call("DELETE", $"/Genres/{g2!["theLoaiID"]}", null, 204, at);
    Check(MoodScoring.MatchPercent(8) == 0, "Opposite mood score must be zero.");
    const string referenceId="OMDB_tt1234567";
    await db.Database.GetCollection<ExternalReference>("externalReferences").InsertOneAsync(new(){Id=referenceId});
    await Call("PUT","/Movies/"+referenceId,new {noiDungID=referenceId,tieuDe="Do not persist remote metadata",valence=.3,arousal=.4},409,at);
    await Call("POST","/History/ratings",new {noiDungID=referenceId,soSao=4},200,at);
    await Call("DELETE","/Movies/"+referenceId,null,401);
    await Call("DELETE","/Movies/"+referenceId,null,204,at);
    Check(!await db.DanhGias.AnyAsync(x=>x.NoiDungID==referenceId),"Reference deletion left ratings behind.");
    await Call("PUT", $"/Users/{user.TaiKhoanID}/access", new { vaiTro = "User", trangThai = true }, 200, at);
    server.Kill(entireProcessTree: true);
    await server.WaitForExitAsync();
    server.Dispose();
    start = new ProcessStartInfo("dotnet") {
        WorkingDirectory = Path.Combine(root, "backend"), UseShellExecute = false, CreateNoWindow = true,
        RedirectStandardOutput = true, RedirectStandardError = true
    };
    start.ArgumentList.Add((Environment.GetEnvironmentVariable("MOODIFY_BACKEND_DLL") ?? Path.Combine(root, "backend/bin/Debug/net10.0/mood_recommendation.dll")));
    start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
    start.Environment["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}";
    start.Environment["Mongo__ConnectionString"] = original;
    start.Environment["Mongo__Database"] = database;
    start.Environment["Jwt__Key"] = key;
    start.Environment["Spotify__ClientId"] = "";
    start.Environment["Omdb__ApiKey"] = "";
    start.Environment["Spotify__ClientSecret"] = "";
    start.Environment["AI__Enabled"] = "true";
    start.Environment["AI__ServiceUrl"] = inference.BaseAddress.AbsoluteUri;
    server = Process.Start(start)!;
    server.OutputDataReceived += (_, e) => { lock (log) { if (e.Data != null) log.AppendLine(e.Data); } };
    server.ErrorDataReceived += (_, e) => { lock (log) { if (e.Data != null) log.AppendLine(e.Data); } };
    server.BeginOutputReadLine(); server.BeginErrorReadLine();
    ready = false;
    for (var i = 0; i < 50; i++)
    {
        if (server.HasExited) throw new Exception("Backend exited during registry restart check.");
        try { var response = await client.GetAsync("/api/Genres"); if (response.IsSuccessStatusCode) { ready = true; break; } } catch (HttpRequestException) { }
        await Task.Delay(100);
    }
    Check(ready, "Backend failed to restart against the same isolated database.");
    var restartedRegistry = await Call("GET", "/Models", null, 200, eit);
    Check(restartedRegistry!["activeVersion"]!.GetValue<string>() == "roberta_large_asl_en_01"
        && restartedRegistry["revision"]!.GetValue<long>() == 3,
        "Persistent registry state did not survive backend restart.");
    await Call("POST", "/Recommendation", new { tamTrangID = mid, limit = 2, analysisId }, 200, ut);
    Check((await Call("GET", "/History", null, 200, ut))!["total"]!.GetValue<int>() == 6,
        "History replay after restart duplicated an already persisted recommendation.");
    await Call("POST", "/Recommendation", new { tamTrangID = mid, limit = 2, analysisId = staleAnalysisId }, 409, ut);
    // Stable ID mapping survives renames and backend restarts; edits are role-gated and versioned.
    await Call("PUT", $"/MoodMapping/{mid}", new { tenTamTrang = "Happy renamed", minValence = 0.5, maxValence = 1, minArousal = 0.4, maxArousal = 0.9 }, 200, at);
    var renamedAnalysis = await Call("POST", "/MoodAnalysis", new { text = "A renamed mood still maps.", language = "en" }, 200, ut);
    Check(renamedAnalysis!["suggestedMoodId"]!.GetValue<int>() == mid, "Renaming mood broke stable AI binding.");
    var mapping = await Call("GET", "/Models/mapping", null, 200, eit);
    var revision = mapping!["revision"]!.GetValue<long>();
    var choices = mapping["labels"]!.AsArray().Select(x => new { label = x!["label"]!.GetValue<string>(), moodId = x["moodId"]?.GetValue<int>() }).ToArray();
    await Call("PUT", "/Models/mapping", new { expectedRevision = revision, labels = choices }, 403, ut);
    await Call("PUT", "/Models/mapping", new { expectedRevision = revision, labels = choices.Take(27) }, 400, eit);
    await Call("PUT", "/Models/mapping", new { expectedRevision = revision, labels = choices.Select(x => new { x.label, moodId = (int?)int.MaxValue }) }, 400, eit);
    var savedMapping = await Call("PUT", "/Models/mapping", new { expectedRevision = revision, labels = choices }, 200, eit);
    Check(savedMapping!["version"]!.GetValue<string>() != mapping["version"]!.GetValue<string>(), "Approval must create a new mapping version.");
    await Call("PUT", "/Models/mapping", new { expectedRevision = revision, labels = choices }, 409, eit);
    await Call("POST", "/Recommendation", new { tamTrangID = mid, limit = 2, analysisId = renamedAnalysis["analysisId"]!.GetValue<string>() }, 409, ut);
    var auditRows = await Call("GET", "/Models/audit", null, 200, eit);
    Check(auditRows!.AsArray().Any(x => x!["action"]!.GetValue<string>() == "mapping_approve"), "Mapping approval has no audit event.");
    var evaluation = await Call("GET", "/Models/evaluation", null, 200, eit);
    Check(evaluation!["available"]!.GetValue<bool>() && evaluation["report"]!["full_test"]!["micro_f1"]!.GetValue<double>() < 0.8, "Offline evaluation is missing or misreported.");
    await Call("GET", "/Models/evaluation", null, 403, ut);
    var coverageRows = await Call("GET", "/Models/coverage", null, 200, eit);
    Check(coverageRows!.AsArray().Count == (await db.TamTrangs.CountAsync()) * 2, "Coverage must report every mood and both content types.");
    await Call("GET", "/Models/coverage", null, 403, ut);
    var freshMood = await Call("POST", "/MoodMapping", new { tenTamTrang = "Temporary mapped mood", minValence = 0, maxValence = 1, minArousal = 0, maxArousal = 1 }, 201, at);
    var freshId = freshMood!["tamTrangID"]!.GetValue<int>();
    var newChoices = choices.Select(x => new { x.label, moodId = x.label == "joy" ? (int?)freshId : x.moodId }).ToArray();
    await Call("PUT", "/Models/mapping", new { expectedRevision = revision + 1, labels = newChoices }, 200, eit);
    await Call("DELETE", $"/MoodMapping/{freshId}", null, 204, at);
    var invalidMapping = await Call("GET", "/Models/mapping", null, 200, eit);
    Check(invalidMapping!["labels"]!.AsArray().Any(x => x!["label"]!.GetValue<string>() == "joy" && x["status"]!.GetValue<string>() == "invalid_missing_mood"), "Deleted mood binding must be explicitly invalid, not silently rebound.");
    var missingMoodAnalysis = await Call("POST", "/MoodAnalysis", new { text = "No mapped target remains.", language = "en" }, 200, ut);
    Check(missingMoodAnalysis!["suggestedMoodId"] == null, "Deleted leading mood must not map to another mood.");
    await db.SaveContent(new NoiDungGiaiTri {NoiDungID="feature-movie",TieuDe="Feature fixture",LoaiNoiDung="Movie",Valence=0,Arousal=0,Phim=new Phim {Director="Fixture"}}, create:true);
    var hybrid = await Call("POST", "/Recommendation", new { tamTrangID = mid, limit = 2, mode = "hybrid" }, 200, ut);
    Check(hybrid!["algorithm"]!.GetValue<string>() == "hybrid-feedback-v1", "Hybrid mode not selected.");
    var selectedId = hybrid["items"]![0]!["content"]!["noiDungID"]!.GetValue<string>();
    var sessionId = hybrid["recommendationId"]!.GetValue<string>();
    var eventId = Guid.NewGuid().ToString("N");
    var interaction = new { contentId = selectedId, action = "detail_open", eventId, recommendationId = sessionId };
    await Call("POST", "/Interactions", interaction, 401);
    await Call("POST", "/Interactions", interaction, 400, ot);
    await Call("POST", "/Interactions", interaction, 200, ut);
    await Call("POST", "/Interactions", interaction, 200, ut);
    await Call("POST", "/Interactions", new { contentId=selectedId, action="external_open", eventId, recommendationId=sessionId }, 409, ut);
    var ownActivity=await Call("GET", "/Interactions", null, 200, ut);
    Check(ownActivity!["total"]!.GetValue<int>() == 1, "Duplicate interaction counted twice.");
    Check((await Call("GET", "/Interactions", null, 200, ot))!["total"]!.GetValue<int>()==0, "Activity leaked across accounts.");
    await Call("GET", $"/Users/{user.TaiKhoanID}/activity", null, 403, ut);
    await Call("GET", $"/Users/{user.TaiKhoanID}/activity", null, 200, at);
    await Call("GET", $"/Users/{user.TaiKhoanID}/activity?kind=recommendations", null, 200, at);
    await Call("GET", $"/Users/{user.TaiKhoanID}/activity?kind=ratings", null, 200, at);
    for(var i=0;i<12;i++) await Call("POST", "/Interactions", new {contentId=selectedId, action="detail_open", eventId=Guid.NewGuid().ToString("N")}, 200, ut);
    Check((await Call("GET", "/Interactions", null, 200, ut))!["items"]!.AsArray().Count==12, "Activity pagination exceeds 12.");
    Check((await Call("GET", "/Interactions?page=2", null, 200, ut))!["items"]!.AsArray().Count==1, "Activity page 2 incorrect.");
    await Call("GET", "/Interactions?page=0", null, 400, ut);
    await Call("POST", "/History/ratings", new {noiDungID=selectedId,soSao=4,recommendationId=sessionId},200,ut);
    await Call("POST", "/History/ratings", new {noiDungID=selectedId,soSao=4,recommendationId=sessionId},400,ot);
    await Call("GET", "/Trailers/lookup?imdbId=tt3896198", null,403,ut);
    await Call("GET", "/Trailers/lookup?imdbId=invalid", null,400,at);
    await Call("POST", "/Movies", new {noiDungID="bad-trailer",tieuDe="Bad",trailerUrl="https://evil.test/watch?v=12345678901"},400,at);
    await Call("POST", "/Movies", new {noiDungID="good-trailer",tieuDe="Trailer",valence=0,arousal=0,moodSource="manual",trailerUrl="https://www.youtube.com/watch?v=12345678901"},201,at);
    Check((await Call("GET","/Movies/good-trailer",null,200,ut))!["phim"]!["trailerUrl"]!.GetValue<string>().Contains("12345678901"),"Trailer URL not persisted.");
    Console.WriteLine($"PASS: {checks} HTTP and business assertions on an isolated MongoDB database.");
}
finally
{
    if (server is { HasExited: false }) { server.Kill(entireProcessTree: true); await server.WaitForExitAsync(); }
    server?.Dispose();
    if (database.StartsWith("moodify_test_", StringComparison.Ordinal)) await mongo.DropDatabaseAsync(database);
}
