using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using mood_recommendation.Data;
using mood_recommendation.Models;

/// <summary>Real weights, isolated processes and copied catalog; no quality labels are invented.</summary>
public static class BilingualRealInferenceSmoke
{
    static int Port() { var s=new TcpListener(IPAddress.Loopback,0);s.Start();var p=((IPEndPoint)s.LocalEndpoint).Port;s.Stop();return p; }
    static Process Launch(string executable,string directory,IEnumerable<string> args,Dictionary<string,string> variables,List<string> errors)
    {
        var start=new ProcessStartInfo(executable){WorkingDirectory=directory,UseShellExecute=false,CreateNoWindow=true,
            RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var arg in args)start.ArgumentList.Add(arg);
        foreach(var pair in variables)start.Environment[pair.Key]=pair.Value;
        var p=Process.Start(start)!;
        p.OutputDataReceived+=(_,_)=>{};
        p.ErrorDataReceived+=(_,e)=> {if(e.Data is not null)lock(errors)errors.Add(e.Data);};
        p.BeginOutputReadLine();p.BeginErrorReadLine();return p;
    }
    static async Task Wait(HttpClient http,string endpoint,Process process,int attempts)
    {
        for(var i=0;i<attempts;i++)
        {
            if(process.HasExited)throw new Exception("Isolated smoke process exited.");
            try { using var r=await http.GetAsync(endpoint);if(r.IsSuccessStatusCode)return; } catch(HttpRequestException){} catch(TaskCanceledException){}
            await Task.Delay(500);
        }
        throw new Exception("Readiness did not complete: "+endpoint);
    }

    public static async Task Run(string root,string connection)
    {
        var database="moodify_test_bilingual_real_"+Guid.NewGuid().ToString("N");
        var mongo=new MongoClient(connection);
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"Mongo:Database",database}}).Build();
        var db=new MongoStore(mongo,config);
        var state=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Moodify");
        var viMarker=Path.Combine(state,"vi-active-model.txt");
        var enMarker=Path.Combine(state,"va-active-model.txt");
        var viVersion=(await File.ReadAllTextAsync(viMarker)).Trim();
        var enVersion=(await File.ReadAllTextAsync(enMarker)).Trim();
        var isolatedState=Path.Combine(root,".local-build","bilingual-state-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(isolatedState);
        var originalViMarker=viMarker;var originalEnMarker=enMarker;
        viMarker=Path.Combine(isolatedState,"vi.txt");enMarker=Path.Combine(isolatedState,"en.txt");
        await File.WriteAllTextAsync(viMarker,viVersion);await File.WriteAllTextAsync(enMarker,enVersion);
        var output=Path.Combine(root,"code-train/train-KLCN/output");
        Process? ai=null,backend=null;
        var errors=new List<string>();
        var checks=0;
        void Check(bool value,string message){if(!value)throw new Exception(message);checks++;}
        try
        {
            var src=mongo.GetDatabase("mood_recommendation");
            foreach(var name in new[]{"genres","moods","contents","movies","music"})
            {
                var rows=await src.GetCollection<BsonDocument>(name).Find(name=="contents"
                    ? new BsonDocument("deleted",new BsonDocument("$ne",true)) : FilterDefinition<BsonDocument>.Empty).ToListAsync();
                if(rows.Count>0)await db.Database.GetCollection<BsonDocument>(name).InsertManyAsync(rows);
            }
            await db.Initialize(Path.Combine(root,"backend/Data/mongo-schema.json"));
            var account=new TaiKhoan{TenDangNhap="bilingual_real_fixture",Email="bilingual-real@test.invalid",
                VaiTro="AIEngineer",MatKhau=BCrypt.Net.BCrypt.HashPassword("Isolated-smoke-password-1")};
            await db.Insert(account);
            var aiPort=Port();var android=Environment.GetEnvironmentVariable("MOODIFY_SMOKE_ANDROID")=="true";var apiPort=android||!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MOODIFY_SMOKE_WEBROOT"))?44861:Port();
            var coldStartup=Stopwatch.StartNew();
            ai=Launch(Path.Combine(root,"ai-service/.venv/Scripts/python.exe"),Path.Combine(root,"ai-service"),
                ["-m","uvicorn","main:app","--host","127.0.0.1","--port",aiPort.ToString()],
                new(){["AI_MODEL_ENABLED"]="false",["VA_MODEL_ENABLED"]="true",["VI_MODEL_ENABLED"]="true",
                    ["VA_MODELS_ROOT"]=output,["VA_MODEL_DIR"]=Path.Combine(output,enVersion),["VA_ACTIVE_MODEL_FILE"]=enMarker,
                    ["VI_MODELS_ROOT"]=Path.Combine(output,"vietnamese-emotion"),["VI_ACTIVE_MODEL_FILE"]=viMarker,
                    ["TOKENIZERS_PARALLELISM"]="false"},errors);
            using var inference=new HttpClient{BaseAddress=new Uri($"http://127.0.0.1:{aiPort}/"),Timeout=TimeSpan.FromSeconds(3)};
            await Wait(inference,"health/va/ready",ai,180);
            await Wait(inference,"health/vi/ready",ai,20);
            coldStartup.Stop();
            var healthEn=await inference.GetFromJsonAsync<JsonObject>("health/va/ready");
            var healthVi=await inference.GetFromJsonAsync<JsonObject>("health/vi/ready");
            Check(healthEn!["model_version"]!.GetValue<string>()==enVersion,"Wrong English serving version.");
            Check(healthVi!["model_version"]!.GetValue<string>()==viVersion,"Wrong Vietnamese serving version.");
            backend=Launch("dotnet",Path.Combine(root,"backend"),
                [Environment.GetEnvironmentVariable("MOODIFY_BACKEND_DLL")??Path.Combine(root,"backend/bin/Debug/net10.0/mood_recommendation.dll")],
                new(){["ASPNETCORE_ENVIRONMENT"]=string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MOODIFY_SMOKE_WEBROOT"))?"Development":"Production",["ASPNETCORE_WEBROOT"]=Environment.GetEnvironmentVariable("MOODIFY_SMOKE_WEBROOT")??Path.Combine(root,"backend/wwwroot"),["ASPNETCORE_URLS"]=$"http://127.0.0.1:{apiPort}",
                    ["Mongo__ConnectionString"]=connection,["Mongo__Database"]=database,["Jwt__Key"]=Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)),
                    ["AI__Enabled"]="true",["AI__ServiceUrl"]=$"http://127.0.0.1:{aiPort}/",
                    ["Email__Host"]="",["Email__Password"]="",["Email__Username"]="",
                    ["Firebase__ProjectId"]="",["Firebase__ServiceAccountPath"]="",["Spotify__ClientId"]="",
                    ["Spotify__ClientSecret"]="",["Tmdb__AccessToken"]=""},errors);
            using var api=new HttpClient{BaseAddress=new Uri($"http://127.0.0.1:{apiPort}/"),Timeout=TimeSpan.FromSeconds(20)};
            await Wait(api,"api/Genres",backend,40);
            async Task<JsonNode> Post(string path,object body)
            {
                using var r=await api.PostAsJsonAsync("api/"+path,body);
                var json=await r.Content.ReadFromJsonAsync<JsonNode>();
                Check(r.IsSuccessStatusCode,"Real bilingual API failed: "+path+" "+r.StatusCode);
                return json!;
            }
            var login=await Post("Auth/login",new{identifier=account.Email,matKhau="Isolated-smoke-password-1"});
            api.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",login["token"]!.GetValue<string>());
            var samples=Regex.Matches(await File.ReadAllTextAsync(Path.Combine(root,"docs/AI_MODEL_TEST_CASES.md")),
                @"^\| ((?:VI|EN)-\d\d) \| ([^|]+) \|",RegexOptions.Multiline)
                .Select(m=>(Id:m.Groups[1].Value,Text:m.Groups[2].Value.Trim())).ToList();
            Check(samples.Count==20,"Expected 20 existing, public smoke descriptions.");
            samples.AddRange(new[]{
                ("VI-calm","Tôi thấy bình yên và thư giãn sau một ngày nhẹ nhàng."),
                ("VI-focus","Tôi đang tập trung yên tĩnh làm bài, đầu óc rất sáng suốt."),
                ("VI-nostalgia","Tôi nhớ những ngày thơ bé, vừa ấm áp vừa man mác buồn."),
                ("VI-no-accents","hom nay toi thay binh yen va thu gian"),
                ("VI-mixed","Tôi feel happy but hơi nervous hôm nay"),
                ("unsupported","今天很开心")});
            var results=new List<object>();
            foreach(var (id,text) in samples)
            {
                var timer=Stopwatch.StartNew();
                var result=await Post("MoodAnalysis/valence-arousal",new{text,language="auto"});
                timer.Stop();
                if(id!="unsupported")
                    Check(result["language"]!.GetValue<string>()==(id.StartsWith("VI")?"vi":"en"),"Backend routing mismatch: "+id);
                var matched=result["status"]!.GetValue<string>()=="matched";
                JsonNode? recommendation=null;
                if(matched)
                {
                    var body=new {source="ai_va",analysisId=result["analysisId"]!.GetValue<string>(),
                        tamTrangID=result["mood"]!["tamTrangID"]!.GetValue<int>(),mode="hybrid",limit=6,balanced=true};
                    recommendation=await Post("Recommendation",body);
                    Check(Math.Abs(recommendation["valence"]!.GetValue<double>()-result["recommendationValence"]!.GetValue<double>())<1e-12,"Server target VA lost in recommender.");
                    var replay=await Post("Recommendation",body);
                    Check(replay["recommendationId"]!.GetValue<string>()==recommendation["recommendationId"]!.GetValue<string>(),"Real model replay duplicated recommendation.");
                    Check(recommendation["items"]!.AsArray().Count>0,"Copied catalog produced no recommendations.");
                    var manual=await Post("Recommendation",new{source="manual",tamTrangID=body.tamTrangID,
                        valence=result["recommendationValence"]!.GetValue<double>(),arousal=result["recommendationArousal"]!.GetValue<double>(),mode="hybrid",limit=6,balanced=true});
                    Check(manual["items"]!.AsArray().Select(x=>x!["content"]!["noiDungID"]!.GetValue<string>()).SequenceEqual(
                        recommendation["items"]!.AsArray().Select(x=>x!["content"]!["noiDungID"]!.GetValue<string>())),
                        "Same VA/profile must rank identically for manual and AI.");
                }
                else
                    Check(result["analysisId"] is null && result["mood"] is null && !result["accepted"]!.GetValue<bool>(),"Real abstention issued a usable receipt.");
                results.Add(new {id,text,latencyMs=timer.Elapsed.TotalMilliseconds,result,
                    recommendationItems=recommendation?["items"]?.AsArray().Count??0});
            }
            // Small bounded overload check, not a capacity/load-test claim.
            var burst=await Task.WhenAll(Enumerable.Range(0,4).Select(async _=>{
                var timer=Stopwatch.StartNew();
                using var response=await api.PostAsJsonAsync("api/MoodAnalysis/valence-arousal",
                    new{text="I feel happy and excited today.",language="auto"});
                timer.Stop();
                return new{status=(int)response.StatusCode,latencyMs=timer.Elapsed.TotalMilliseconds};
            }));
            Check(burst.All(x=>x.status is 200 or 503)&&burst.Any(x=>x.status==200),"Concurrent inference did not fail closed with bounded busy handling.");
            if(!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MOODIFY_SMOKE_WEBROOT"))) {
                var browserUser=new TaiKhoan{TenDangNhap="flutter_real_fixture",Email="flutter-real@test.invalid",
                    MatKhau=BCrypt.Net.BCrypt.HashPassword("Flutter-smoke-password-1")};
                await db.Insert(browserUser);
                var browserStart=new ProcessStartInfo("node"){WorkingDirectory=Path.Combine(root,"admin-web"),UseShellExecute=false,
                    CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
                browserStart.ArgumentList.Add("tests/bilingual-flutter-real.mjs");
                browserStart.Environment["SMOKE_ORIGIN"]=$"http://127.0.0.1:{apiPort}";
                browserStart.Environment["SMOKE_USER"]=browserUser.Email;
                browserStart.Environment["SMOKE_PASSWORD"]="Flutter-smoke-password-1";
                using var browser=Process.Start(browserStart)!;
                var stdout=browser.StandardOutput.ReadToEndAsync();var stderr=browser.StandardError.ReadToEndAsync();
                await browser.WaitForExitAsync();var browserErrors=await stderr;var browserOutput=await stdout;
                Check(browser.ExitCode==0,"Flutter browser integration failed: "+browserOutput+" "+browserErrors);
                Console.WriteLine(browserOutput.Trim());
            }
            if(android) {
                var privatePath=Path.Combine(root,".local-build/android-smoke-private.json");
                var credentials=JsonNode.Parse(await File.ReadAllTextAsync(privatePath))!;
                var email=credentials["SMOKE_USER"]!.GetValue<string>();
                var originalUser=await src.GetCollection<TaiKhoan>("accounts").Find(x=>x.Email==email).FirstOrDefaultAsync();
                Check(originalUser is not null&&BCrypt.Net.BCrypt.Verify(credentials["SMOKE_PASSWORD"]!.GetValue<string>(),originalUser.MatKhau),
                    "Supplied Android account credentials did not match the read-only source account.");
                await db.Insert(new TaiKhoan{TenDangNhap=originalUser!.TenDangNhap,Email=email,MatKhau=originalUser.MatKhau});
                var start=new ProcessStartInfo("cmd.exe"){WorkingDirectory=Path.Combine(root,"mobile-app"),UseShellExecute=false,
                    CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
                foreach(var argument in new[]{"/d","/c",@"D:\android\Flutter\flutter\bin\flutter.bat","test","integration_test/bilingual_android_test.dart",
                    "-d","emulator-5554","--no-pub","--dart-define=MOODIFY_INTEGRATION=true","--dart-define-from-file="+privatePath})start.ArgumentList.Add(argument);
                start.Environment["PUB_CACHE"]=Path.Combine(Directory.GetParent(root)!.FullName,"moodify-pub-cache");
                start.Environment["ORG_GRADLE_PROJECT_moodifyIntegration"]="true";
                using var mobile=Process.Start(start)!;
                var liveInfo=await inference.GetFromJsonAsync<JsonObject>("health/live");
                using var inferenceWorker=Process.GetProcessById(liveInfo!["process_id"]!.GetValue<int>());
                using var monitorStop=new CancellationTokenSource();
                var resourceSamples=new List<object>();
                var monitor=Task.Run(async()=>{
                    async Task<string> ReadTool(string executable,params string[] args){
                        var info=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
                        foreach(var arg in args)info.ArgumentList.Add(arg);
                        using var process=Process.Start(info)!;
                        var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
                        await process.WaitForExitAsync();var text=await stdout;await stderr;return text;
                    }
                    while(!monitorStop.IsCancellationRequested){
                        try{
                            var gpu=(await ReadTool("nvidia-smi","--query-gpu=memory.used,memory.total,utilization.gpu","--format=csv,noheader,nounits"))
                                .Split(',').Select(v=>double.Parse(v.Trim(),System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                            var memory=await ReadTool(@"D:\android\SDK\platform-tools\adb.exe","-s","emulator-5554","shell","dumpsys","meminfo","com.example.moodify_application.smoke");
                            var pss=Regex.Match(memory,@"TOTAL PSS:\s*(\d+)");
                            inferenceWorker.Refresh();backend?.Refresh();
                            resourceSamples.Add(new{at=DateTimeOffset.UtcNow,gpuUsedMiB=gpu[0],gpuTotalMiB=gpu[1],gpuUtilization=gpu[2],
                                androidPssMiB=pss.Success?double.Parse(pss.Groups[1].Value)/1024:(double?)null,
                                inferenceWorkingSetMiB=!inferenceWorker.HasExited?inferenceWorker.WorkingSet64/1048576d:(double?)null,
                                backendWorkingSetMiB=backend is {HasExited:false}?backend.WorkingSet64/1048576d:(double?)null});
                        }catch(Exception e)when(e is not OutOfMemoryException){}
                        try{await Task.Delay(3000,monitorStop.Token);}catch(OperationCanceledException){break;}
                    }
                });

                var stdout=mobile.StandardOutput.ReadToEndAsync();var stderr=mobile.StandardError.ReadToEndAsync();
                await mobile.WaitForExitAsync();monitorStop.Cancel();await monitor;
                var outputText=await stdout;var errorText=await stderr;
                await File.WriteAllTextAsync(Path.Combine(root,".local-build/phase3-android-resources.json"),
                    JsonSerializer.Serialize(new{scope="3-second samples during debug Android test/build; GPU totals include OS/emulator/other processes, PSS is app process only.",samples=resourceSamples},new JsonSerializerOptions(JsonSerializerDefaults.Web){WriteIndented=true}));
                await File.WriteAllTextAsync(Path.Combine(root,".local-build/phase3-android.log"),outputText+"\n"+errorText);
                Check(mobile.ExitCode==0,"Android integration failed. Inspect .local-build/phase3-android.log");
                var match=Regex.Match(outputText,@"MOODIFY_ANDROID_RESULT=(\{[^\r\n]+\})");
                Check(match.Success,"Android result evidence missing.");
                await File.WriteAllTextAsync(Path.Combine(root,".local-build/phase3-android-result.json"),match.Groups[1].Value);
                Console.WriteLine("PASS: Android actual-model bilingual UI and feedback; isolated account/database.");
            }
            var history=await db.LichSuTamTrangs.Where(x=>x.TaiKhoanID==account.TaiKhoanID && x.Source=="ai_va").ToListAsync();
            Check(history.Count>0 && history.All(x=>x.AnalysisLanguage is "vi" or "en" && x.AnalysisId is not null && x.VanBanDauVao is null),"Real provenance incomplete.");
            Check((await File.ReadAllTextAsync(viMarker)).Trim()==viVersion && (await File.ReadAllTextAsync(enMarker)).Trim()==enVersion,"Smoke altered active markers.");
            Check((await File.ReadAllTextAsync(originalViMarker)).Trim()==viVersion&&(await File.ReadAllTextAsync(originalEnMarker)).Trim()==enVersion,"Production markers changed.");
            var report=new {checkedAt=DateTimeOffset.UtcNow,coldStartupMs=coldStartup.Elapsed.TotalMilliseconds,qualityStatus="unlabelled_smoke_not_independent_quality_evaluation",
                description="Existing public demo descriptions; no ground-truth VA or independently reviewed emotion labels.",
                english=healthEn,vietnamese=healthVi,cases=results.Count,assertions=checks,burst,results};
            var destination=Path.Combine(root,android?".local-build/ai-unification-phase3-bilingual-smoke.json":".local-build/ai-unification-phase2-bilingual-smoke.json");
            await File.WriteAllTextAsync(destination,JsonSerializer.Serialize(report,new JsonSerializerOptions(JsonSerializerDefaults.Web){WriteIndented=true}));
            Console.WriteLine($"PASS: {checks} real-model assertions, {results.Count} descriptions; {enVersion} + {viVersion}. Catalog copied to temporary DB. Quality metrics not claimed.");
            Console.WriteLine("Evidence: "+Path.GetRelativePath(root,destination));
        }
        catch
        {
            // Only local model loader errors, never tokens/credentials or application request bodies.
            lock(errors)foreach(var line in errors.Where(l=>l.Contains("Error")||l.Contains("Exception")).TakeLast(5))Console.Error.WriteLine(line);
            throw;
        }
        finally
        {
            foreach(var p in new[]{backend,ai})
            {
                if(p is {HasExited:false}) {p.Kill(entireProcessTree:true);await p.WaitForExitAsync();}
                p?.Dispose();
            }
            if(database.StartsWith("moodify_test_bilingual_real_",StringComparison.Ordinal))await mongo.DropDatabaseAsync(database);
        }
    }
}
