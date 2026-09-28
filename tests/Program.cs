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
if (args.Contains("--live-read")) { await MongoReadCheck.Run(original);return; }
if (args.Contains("--browser-smoke")) { await BrowserSmoke.Run(root,original);return; }
if (args.Length > 0) throw new ArgumentException("Supported options: --browser-smoke, --live-read. Mongo migration: node scripts/mongo-migrate.mjs --apply");
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
    checks += await SpotifyTests.Run(db);
    checks += await ExternalCatalogTests.Run(db);
    var admin = new TaiKhoan { TenDangNhap = "test_admin", Email = "admin@test.invalid", VaiTro = "Admin", MatKhau = BCrypt.Net.BCrypt.HashPassword("Test-password-1") };
    var user = new TaiKhoan { TenDangNhap = "test_user", Email = "user@test.invalid", MatKhau = BCrypt.Net.BCrypt.HashPassword("Test-password-2") };
    var other = new TaiKhoan { TenDangNhap = "test_other", Email = "other@test.invalid", MatKhau = BCrypt.Net.BCrypt.HashPassword("Test-password-3") };
    await db.Insert(admin);await db.Insert(user);await db.Insert(other);
    var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
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
    start.Environment["Omdb__ApiKey"] = "";
    start.Environment["Spotify__ClientSecret"] = "";
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
    await Call("GET", "/Omdb/lookup?query=tt3896198", null, 401);
    await Call("GET", "/Omdb/lookup?query=tt3896198", null, 403, ut);
    await Call("GET", "/Omdb/lookup?query=tt3896198", null, 503, at);
    await Call("GET", "/Omdb/lookup?query=movie&year=1", null, 400, at);
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
    var movie = new { noiDungID = "test-movie", tieuDe = "Test Movie", theLoai = "Drama 2", valence = 0m, arousal = 0m, diemDanhGiaTB = 10, hinhAnh = "https://example.com/poster.jpg" };
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
    var recommendation = await Call("POST", "/Recommendation", new { tamTrangID = mid, limit = 2 }, 200, ut);
    Check(recommendation!["items"]![0]!["content"]!["noiDungID"]!.GetValue<string>() == "test-movie", "Wrong nearest content.");
    Check(recommendation["items"]![0]!["match"]!.GetValue<double>() == 100, "Exact mood match must be 100%.");
    Check((await Call("GET", "/History", null, 200, ut))!["total"]!.GetValue<int>() == 1, "History not saved.");
    Check((await Call("GET", "/History", null, 200, ot))!["total"]!.GetValue<int>() == 0, "History leaked across accounts.");
    await Call("DELETE", $"/MoodMapping/{mid}", null, 409, at);
    await Call("POST", "/History/ratings", new { noiDungID = "test-movie", soSao = 6 }, 400, ut);
    var rating = await Call("POST", "/History/ratings", new { noiDungID = "test-movie", soSao = 5, nhanXet = "Good" }, 200, ut);
    var rid = rating!["danhGiaID"]!.GetValue<long>();
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
    Console.WriteLine($"PASS: {checks} HTTP and business assertions on an isolated MongoDB database.");
}
finally
{
    if (server is { HasExited: false }) { server.Kill(entireProcessTree: true); await server.WaitForExitAsync(); }
    server?.Dispose();
    if (database.StartsWith("moodify_test_", StringComparison.Ordinal)) await mongo.DropDatabaseAsync(database);
}
