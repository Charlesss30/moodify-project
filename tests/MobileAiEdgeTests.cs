using System.Text.Json;
using mood_recommendation.Services;

public static class MobileAiEdgeTests
{
    public static int Run(string root)
    {
        using var document=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"tests/ai-cases/mobile-ai-edge-cases.json")));
        var results=new List<object>();var failures=new List<string>();var checks=0;
        foreach(var item in document.RootElement.EnumerateArray()) {
            var id=item.GetProperty("id").GetString()!;
            var text=item.GetProperty("text").GetString()!;
            var language=item.GetProperty("language").GetString()!;
            var expected=item.GetProperty("contextMood").GetString();
            var context=SemanticMoodMapper.ReadContext(text,language);
            var input=SemanticMoodMapper.PrepareModelInput(text,language);
            var ok=context.MoodKey==expected;
            if(!ok)failures.Add(id);
            checks++;
            results.Add(new{id,text,expected,context,input,ok});
        }
        using(var routes=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"tests/ai-cases/language-routing-cases.json")))) {
            foreach(var item in routes.RootElement.EnumerateArray()) {
                var text=item.GetProperty("text").GetString()!;
                var expected=item.GetProperty("language").GetString();
                if(MoodLanguageRouter.Detect(text)!=expected)failures.Add("route: "+text);
                checks++;
            }
        }
        Directory.CreateDirectory(Path.Combine(root,".local-build"));
        File.WriteAllText(Path.Combine(root,".local-build/mobile-ai-edge-context.json"),
            JsonSerializer.Serialize(new{checks,failures,results},new JsonSerializerOptions(JsonSerializerDefaults.Web){WriteIndented=true}));
        Console.WriteLine($"Context edges: {checks} checked, {failures.Count} mismatches: {string.Join(", ",failures)}");
        if(failures.Count>0)throw new Exception("Inspect .local-build/mobile-ai-edge-context.json.");
        return checks;
    }
}
