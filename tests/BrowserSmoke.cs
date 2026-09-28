using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using MongoDB.Driver;
using Microsoft.Extensions.Configuration;
using mood_recommendation.Data;
using mood_recommendation.Models;


public static class BrowserSmoke
{
    private static int FreePort()
    {
        var socket = new TcpListener(IPAddress.Loopback, 0);
        socket.Start(); var port = ((IPEndPoint)socket.LocalEndpoint).Port; socket.Stop(); return port;
    }
    private static Process Start(string command, string directory, IEnumerable<string> args, Dictionary<string,string> env)
    {
        var options = new ProcessStartInfo(command) { WorkingDirectory = directory, UseShellExecute = false,
            CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) options.ArgumentList.Add(arg);
        foreach (var pair in env) options.Environment[pair.Key] = pair.Value;
        var process = Process.Start(options)!;
        // Drain service output; the browser test reports its own concise result.
        process.OutputDataReceived += (_, _) => { };
        process.ErrorDataReceived += (_, _) => { };
        process.BeginOutputReadLine(); process.BeginErrorReadLine();
        return process;
    }
    public static async Task Run(string root, string connection)
    {
        var database="moodify_browser_"+Guid.NewGuid().ToString("N");
        var mongo=new MongoClient(connection);
        var db=new MongoStore(mongo,new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"Mongo:Database",database}}).Build());
        Process? backend = null, frontend = null;
        try
        {
            await db.Initialize(Path.Combine(root,"backend/Data/mongo-schema.json"));
            foreach(var name in new[]{"Action","Adventure","Sci-Fi"}) await db.Insert(new TheLoai {TenTheLoai=name,ContentType="Movie",DefaultValence=.2m,DefaultArousal=.6m});
            await db.SaveContent(new NoiDungGiaiTri {
                NoiDungID = "SPOTIFY_" + SpotifyTests.TrackId, TieuDe = "Spotify fixture song", LoaiNoiDung = "Song",
                Valence = .75m, Arousal = .65m, Nhac = new Nhac {
                    TenNgheSi = "Fixture artist", Album = "Fixture album", Duration = 180,
                    Source = "Spotify", ExternalId = SpotifyTests.TrackId,
                    SourceUrl = "https://open.spotify.com/track/" + SpotifyTests.TrackId, ReleaseDate = "2020-01", MoodSource = "admin-selected"
                }
            }, create:true);
            await db.Insert(new TamTrang { TenTamTrang = "Happy", MinValence = .5m, MaxValence = 1, MinArousal = .4m, MaxArousal = .9m });
            for(var i=0;i<25;i++) await db.Insert(new TheLoai {TenTheLoai=$"Test genre {i:00}",ContentType="Song"});
            var password = "Browser-" + Guid.NewGuid().ToString("N");
            await db.Insert(
                new TaiKhoan { TenDangNhap = "browser_admin", Email = "browser_admin@test.invalid", VaiTro = "Admin", MatKhau = BCrypt.Net.BCrypt.HashPassword(password) });
            await db.Insert(
                new TaiKhoan { TenDangNhap = "browser_user", Email = "browser_user@test.invalid", MatKhau = BCrypt.Net.BCrypt.HashPassword(password) });

            var apiPort = FreePort(); var adminPort = FreePort();
            var api = $"http://127.0.0.1:{apiPort}";
            var admin = $"http://127.0.0.1:{adminPort}";
            backend = Start("dotnet", Path.Combine(root, "backend"),
                [(Environment.GetEnvironmentVariable("MOODIFY_BACKEND_DLL") ?? Path.Combine(root, "backend/bin/Debug/net10.0/mood_recommendation.dll"))],
                new() { ["ASPNETCORE_ENVIRONMENT"] = "Development", ["ASPNETCORE_URLS"] = api, ["Mongo__ConnectionString"] = connection, ["Mongo__Database"] = database });
            frontend = Start("node", Path.Combine(root, "admin-web"),
                ["node_modules/vite/bin/vite.js", "--host", "127.0.0.1", "--port", adminPort.ToString()],
                new() { ["MOODIFY_API_TARGET"] = api });
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            var ready = false;
            for (var i = 0; i < 60; i++)
            {
                try { if ((await http.GetAsync(api + "/api/Genres")).IsSuccessStatusCode && (await http.GetAsync(admin)).IsSuccessStatusCode) { ready = true; break; } }
                catch (HttpRequestException) { }
                await Task.Delay(250);
            }
            if (!ready) throw new Exception("Smoke test services did not start.");
            var options = new ProcessStartInfo("node") { WorkingDirectory = Path.Combine(root, "admin-web"), UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            options.ArgumentList.Add("tests/browser-smoke.mjs");
            options.Environment["SMOKE_API"] = api; options.Environment["SMOKE_ADMIN"] = admin; options.Environment["SMOKE_PASSWORD"] = password;
            using var browser = Process.Start(options)!;
            var browserOut = browser.StandardOutput.ReadToEndAsync();
            var browserErr = browser.StandardError.ReadToEndAsync();
            await browser.WaitForExitAsync();
            Console.WriteLine(await browserOut);
            Console.WriteLine(await browserErr);
            if (browser.ExitCode != 0) throw new Exception("Browser smoke test failed.");
            Console.WriteLine("PASS: Spotify admin browser smoke test (isolated database and provider fixtures).");
        }
        finally
        {
            foreach (var process in new[] { frontend, backend })
            {
                if (process is { HasExited: false }) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
                process?.Dispose();
            }
            if (database.StartsWith("moodify_browser_", StringComparison.Ordinal)) await mongo.DropDatabaseAsync(database);
        }
    }
}
