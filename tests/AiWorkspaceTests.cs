using System.Text.Json.Nodes;

public static class AiWorkspaceTests
{
    public static async Task Run(Func<string, string, object?, int, string?, Task<JsonNode?>> call,
        Action<bool, string> check, string admin, string engineer, string user)
    {
        await call("GET", "/AiWorkspace/datasets", null, 401, null);
        foreach (var path in new[] { "datasets", "jobs", "feedback" })
        {
            await call("GET", "/AiWorkspace/" + path, null, 403, user);
            await call("GET", "/AiWorkspace/" + path, null, 200, engineer);
            await call("GET", "/AiWorkspace/" + path, null, 403, admin);
        }
        foreach (var path in new[] { "mapping", "coverage" }) await call("GET", "/Models/" + path, null, 403, admin);
        await call("POST", "/Models/test", new { text = "I feel happy", language = "en" }, 403, admin);
        await call("PUT", "/Models/mapping", new { }, 403, admin);
        await call("POST", "/AiWorkspace/jobs", new { datasetId = "fixture", modelVersion = "fixture" }, 403, admin);
        object Sample(string text, string split) => new { text, split, labels = new[] { "joy" } };
        var small = new[] { Sample("A happy train sentence", "train"), Sample("A happy validation sentence", "validation"), Sample("A happy test sentence", "test") };
        await call("POST", "/AiWorkspace/datasets", new { name = "Private data", source = "Reviewed fixture", rows = small }, 403, user);
        await call("POST", "/AiWorkspace/datasets", new { name = "Duplicates", source = "Fixture", rows = new[] { Sample("same text", "train"), Sample(" SAME  TEXT ", "validation"), Sample("Other text", "test") } }, 400, engineer);
        var mini = await call("POST", "/AiWorkspace/datasets", new { name = "Tiny fixture", source = "Reviewed fixture", rows = small }, 200, engineer);
        await call("POST", "/AiWorkspace/jobs", new { datasetId = mini!["id"]!.GetValue<string>(), modelVersion = "too_small" }, 400, engineer);
        var samples = Enumerable.Range(0, 402).Select(i => Sample("Reviewed English fixture sample number " + i,
            i == 0 ? "train" : i == 401 ? "test" : "validation")).ToArray();
        var data = await call("POST", "/AiWorkspace/datasets", new { name = "Workspace fixture", source = "Synthetic test only", rows = samples }, 200, engineer);
        var datasetId = data!["id"]!.GetValue<string>();
        check(data["count"]!.GetValue<int>() == 402, "Dataset count changed.");
        await call("GET", "/AiWorkspace/datasets/" + datasetId, null, 403, user);
        await call("GET", "/AiWorkspace/datasets/" + datasetId, null, 200, engineer);
        await call("POST", "/AiWorkspace/jobs", new { datasetId, modelVersion = "../invalid" }, 400, engineer);
        await call("POST", "/AiWorkspace/jobs", new { datasetId, modelVersion = "invalid_rate", learningRate = 1 }, 400, engineer);
        var job = await call("POST", "/AiWorkspace/jobs", new { datasetId, modelVersion = "test_asl_02" }, 200, engineer);
        var id = job!["id"]!.GetValue<string>();
        check(job["status"]!.GetValue<string>() == "prepared", "Creating a job must not claim training started.");
        var bundle = await call("GET", $"/AiWorkspace/jobs/{id}/bundle", null, 200, engineer);
        check(bundle!["datasetHash"]!.GetValue<string>() == data["hash"]!.GetValue<string>(), "Bundle lost dataset provenance.");
        var notebook = await call("GET", $"/AiWorkspace/jobs/{id}/notebook", null, 200, engineer);
        check(notebook!["cells"]!.AsArray().Count == 13, "Notebook must retain all original cells plus report bridge.");
        var testRoot = Environment.GetEnvironmentVariable("MOODIFY_TEST_ROOT");
        if (testRoot is not null) {
            var generated = Path.Combine(testRoot, ".local-build/ai-workspace/generated-notebook.test.ipynb");
            Directory.CreateDirectory(Path.GetDirectoryName(generated)!);
            await File.WriteAllTextAsync(generated, notebook.ToJsonString());
        }
        var source = notebook["cells"]![1]!["source"]![0]!.GetValue<string>();
        check(source.Contains("LOSS_KIND = 'asl'") && source.Contains("WORKSPACE") && source.Contains("workspace-jobs"), "Notebook lost ASL or job isolation.");
        check(notebook["cells"]![2]!.ToJsonString().Contains("gamma_neg"), "Original ASL source was removed.");
        await call("POST", $"/AiWorkspace/jobs/{id}/state", new { state = "running", revision = 99 }, 409, engineer);
        await call("POST", $"/AiWorkspace/jobs/{id}/state", new { state = "running", revision = 0 }, 200, engineer);
        var report = new { revision = 1, jobId = id, modelVersion = "test_asl_02", datasetHash = bundle["datasetHash"]!.GetValue<string>(),
            configHash = bundle["configHash"]!.GetValue<string>(), artifactHash = new string('a', 64), microF1 = .7, macroF1 = .5, testSamples = 1 };
        await call("POST", $"/AiWorkspace/jobs/{id}/report", report, 403, user);
        var reported = await call("POST", $"/AiWorkspace/jobs/{id}/report", report, 200, engineer);
        check(reported!["status"]!.GetValue<string>() == "reported", "An imported report must not imply verified deployment.");
        check(reported["events"]!.AsArray().Count == 3, "Job state audit trail missing.");
        await call("POST", $"/AiWorkspace/jobs/{id}/report", report, 409, engineer);
        await call("POST", $"/AiWorkspace/jobs/{id}/state", new { state = "cancelled", revision = 2 }, 409, engineer);
        var cancel = await call("POST", "/AiWorkspace/jobs", new { datasetId, modelVersion = "cancel_fixture" }, 200, engineer);
        await call("POST", $"/AiWorkspace/jobs/{cancel!["id"]!.GetValue<string>()}/state", new { state = "cancelled", revision = 0 }, 200, engineer);
        await call("POST", "/AiWorkspace/feedback/export", new { }, 403, user);
        var feedback = await call("POST", "/AiWorkspace/feedback/export", new { }, 200, engineer);
        check(!feedback!.ToJsonString().Contains("accountId") && !feedback.ToJsonString().Contains("comment" + "\":"), "Feedback export exposed identities/comments.");
        await call("GET", "/AiWorkspace/worker", null, 403, user);
        await call("GET", "/AiWorkspace/worker", null, 403, admin);
        await call("POST", $"/AiWorkspace/jobs/{id}/start", new { revision = 0 }, 403, user);
        await call("GET", $"/AiWorkspace/jobs/{id}/progress", null, 403, user);
        var local=await call("POST", "/AiWorkspace/jobs", new {datasetId,modelVersion="local_fixture",epochs=1,batchSize=8},200,engineer);
        var localId=local!["id"]!.GetValue<string>();
        if(Environment.GetEnvironmentVariable("MOODIFY_TRAINING_TEST_MODEL") is null) {
            await call("POST",$"/AiWorkspace/jobs/{localId}/start",new {revision=0},503,engineer);
        } else {
            await call("POST",$"/AiWorkspace/jobs/{localId}/start",new {revision=99},409,engineer);
            await call("POST",$"/AiWorkspace/jobs/{localId}/start",new {revision=0},200,engineer);
            await call("POST",$"/AiWorkspace/jobs/{localId}/start",new {revision=0},409,engineer);
            await call("POST",$"/AiWorkspace/jobs/{localId}/state",new {revision=1,state="failed"},409,engineer);
            JsonNode? progress=null;
            for(var attempt=0;attempt<180;attempt++){
                progress=await call("GET",$"/AiWorkspace/jobs/{localId}/progress",null,200,engineer);
                if(progress!["job"]!["status"]!.GetValue<string>() is "completed" or "failed")break;
                await Task.Delay(1000);
            }
            check(progress!["job"]!["status"]!.GetValue<string>()=="completed", "Real tiny-model worker failed: "+progress.ToJsonString());
            check(progress["artifactAvailable"]!.GetValue<bool>(),"Local artifact was not verified.");
            check(progress["job"]!["report"]!["artifactHash"]!.GetValue<string>() != progress["job"]!["baseArtifactHash"]!.GetValue<string>(),"Training did not change the tiny model weights.");
            await call("GET",$"/AiWorkspace/jobs/{localId}/artifact",null,200,engineer);
            var stopped=await call("POST","/AiWorkspace/jobs",new {datasetId,modelVersion="stop_fixture",epochs=10,batchSize=1},200,engineer);
            var stopId=stopped!["id"]!.GetValue<string>();
            await call("POST",$"/AiWorkspace/jobs/{stopId}/start",new {revision=0},200,engineer);
            for(var attempt=0;attempt<30;attempt++){
                progress=await call("GET",$"/AiWorkspace/jobs/{stopId}/progress",null,200,engineer);
                if(progress!["job"]!["status"]!.GetValue<string>()=="running" && progress["job"]!["workerPid"] is not null)break;
                await Task.Delay(200);
            }
            await call("POST",$"/AiWorkspace/jobs/{stopId}/cancel",new {revision=progress!["job"]!["revision"]!.GetValue<int>()},200,engineer);
            for(var attempt=0;attempt<30;attempt++){
                progress=await call("GET",$"/AiWorkspace/jobs/{stopId}/progress",null,200,engineer);
                if(progress!["job"]!["status"]!.GetValue<string>()=="cancelled")break;
                await Task.Delay(200);
            }
            check(progress!["job"]!["status"]!.GetValue<string>()=="cancelled","Cancel did not stop local job.");
            var pid=progress["job"]!["workerPid"]!.GetValue<int>();
            var exited=false;
            try { using var child=System.Diagnostics.Process.GetProcessById(pid); exited=child.HasExited; } catch(ArgumentException) { exited=true; }
            check(exited,"Cancelled training process is still running.");
        }
        await call("GET", "/Users", null, 403, engineer);
    }
}
