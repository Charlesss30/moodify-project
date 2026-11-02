using System.IO.Compression;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

// Synthetic archive bytes exercise storage and promotion boundaries only; no model is loaded.
public static class ModelPackageTests {
    public static async Task Run(string root,HttpClient client,Action<bool,string> check,string engineer,string admin,FakeInferenceServer inference) {
        var output=Path.GetFullPath(Path.Combine(root,"code-train/train-KLCN/output"));
        var nonce=Guid.NewGuid().ToString("N");
        var activations=inference.ModelActivations;
        var created=new List<string>();
        try {
            foreach(var language in new[]{"en","vi"}) {
                var version="six_upload_fixture_"+language+"_"+nonce;
                var folder=Path.GetFullPath(Path.Combine(output,language=="vi"?"vietnamese-emotion":"",version));
                if(!folder.StartsWith(output+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||Directory.Exists(folder))throw new Exception("Unsafe upload fixture path.");
                created.Add(folder);
                var model=language=="en"?"best_model":"model";
                var files=new Dictionary<string,string>{
                    [model+"/config.json"]="{}",[model+"/model.safetensors"]="SOFTWARE FIXTURE NOT A MODEL",
                    [model+"/tokenizer_config.json"]="{}",[model+"/tokenizer.json"]="{}",
                    [model+"/vocab.txt"]="fixture",[model+"/bpe.codes"]="fixture",
                    ["label_config.json"]="{}",["preprocessing.json"]="{\"emoji_dict\":{},\"teencode_dict\":{}}",
                    ["confidence_threshold.json"]="{\"confidence_threshold\":0.8}",
                    ["manifest.json"]=JsonSerializer.Serialize(new{model_version=version,language})
                };
                async Task<HttpResponseMessage> Upload(string token,string requestedVersion) {
                    using var zip=new MemoryStream();
                    using(var archive=new ZipArchive(zip,ZipArchiveMode.Create,true))
                        foreach(var file in files) {using var writer=new StreamWriter(archive.CreateEntry("export/"+file.Key).Open(),new UTF8Encoding(false));writer.Write(file.Value);}
                    using var form=new MultipartFormDataContent();
                    form.Add(new StringContent(requestedVersion),"version");
                    form.Add(new ByteArrayContent(zip.ToArray()),"package","fixture.zip");
                    using var message=new HttpRequestMessage(HttpMethod.Post,"/api/Models/"+(language=="en"?"va":"vi")+"/upload"){Content=form};
                    message.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
                    return await client.SendAsync(message);
                }
                using(var denied=await Upload(admin,version))check((int)denied.StatusCode==403,"Admin uploaded an engineer-only candidate.");
                using(var mismatch=await Upload(engineer,version+"_wrong"))check((int)mismatch.StatusCode==400,"Archive manifest and requested version mismatch was accepted.");
                using var response=await Upload(engineer,version);
                check((int)response.StatusCode==202,"Six-class "+language+" archive did not store as pending quality: "+response.StatusCode+" "+await response.Content.ReadAsStringAsync());
                var result=await response.Content.ReadFromJsonAsync<JsonNode>();
                check(result!["status"]!.GetValue<string>()=="stored_pending_quality","Upload bypassed quality review.");
                foreach(var file in files.Where(f=>f.Key!="preprocessing.json"||language=="vi"))
                    check(File.Exists(Path.Combine(folder,file.Key)),"Archive lost a serving asset: "+file.Key);
                var threshold=JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(folder,"confidence_threshold.json")));
                check(threshold!["confidence_threshold"]!.GetValue<double>()==.8,"Upload lost classifier calibration configuration.");
                using(var duplicate=await Upload(engineer,version))check((int)duplicate.StatusCode==409,"Upload overwrote an existing version.");
            }
            check(inference.ModelActivations==activations,"Upload attempted automatic activation without quality evidence.");
        }
        finally {
            foreach(var folder in created)
                if(Path.GetFullPath(folder).StartsWith(output+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)
                    &&Path.GetFileName(folder).EndsWith(nonce,StringComparison.Ordinal)&&Directory.Exists(folder))Directory.Delete(folder,true);
        }
    }
}
