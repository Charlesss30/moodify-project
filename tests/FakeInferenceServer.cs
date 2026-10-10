using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;

public sealed class FakeInferenceServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly string[] _labels;
    private readonly double[] _thresholds;
    private readonly double _selectiveThreshold;
    private readonly string _configHash;
    private CancellationTokenSource? _stop;
    private Task? _loop;

    public FakeInferenceServer(string repositoryRoot)
    {
        var modelDirectory = Path.Combine(repositoryRoot,
            "code-train", "train-KLCN", "output", "roberta_large_asl_en_01", "best_model");
        using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(modelDirectory, "inference_config.json")));
        var root = config.RootElement;
        _labels = root.GetProperty("labels").EnumerateArray().Select(x => x.GetString()!).ToArray();
        _thresholds = root.GetProperty("thresholds").EnumerateArray().Select(x => x.GetDouble()).ToArray();
        _selectiveThreshold = root.GetProperty("selective_policy").GetProperty("threshold").GetDouble();
        _configHash = Convert.ToHexString(SHA256.HashData(
            File.ReadAllBytes(Path.Combine(modelDirectory, "inference_config.json")))).ToLowerInvariant();

        var socket = new TcpListener(IPAddress.Loopback, 0);
        socket.Start();
        var port = ((IPEndPoint)socket.LocalEndpoint).Port;
        socket.Stop();
        BaseAddress = new Uri($"http://127.0.0.1:{port}/");
        _listener.Prefixes.Add(BaseAddress.AbsoluteUri);
    }

    public Uri BaseAddress { get; }
    public string Scenario { get; set; } = "matched";
    public int StatusCode { get; set; } = 200;
    public int DelayMilliseconds { get; set; }

    public void Start()
    {
        _stop = new CancellationTokenSource();
        _listener.Start();
        _loop = Task.Run(() => AcceptAsync(_stop.Token));
    }

    private async Task AcceptAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext context;
            try { context = await _listener.GetContextAsync(); }
            catch (HttpListenerException) when (cancellationToken.IsCancellationRequested) { break; }
            _ = Task.Run(() => HandleAsync(context, cancellationToken), cancellationToken);
        }
    }

    private async Task HandleAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        try
        {
            if (context.Request.Url?.AbsolutePath == "/health/ready")
            {
                await WriteAsync(context.Response, new
                {
                    status = "ready",
                    model_version = "roberta_large_asl_en_01",
                    artifact_hash = "EA865317D9EB7CF1B39008062276BAE441801A7D719D33177A3DDD1851AF7B09".ToLowerInvariant(),
                    config_hash = _configHash,
                    language = "en",
                    score_type = "uncalibrated_sigmoid_not_accuracy",
                    inference_device = "cpu",
                    device_name = "Fake test CPU"
                }, cancellationToken);
                return;
            }
            if (context.Request.Url?.AbsolutePath != "/predict")
            {
                context.Response.StatusCode = 404;
                context.Response.Close();
                return;
            }
            if (DelayMilliseconds > 0)
                await Task.Delay(DelayMilliseconds, cancellationToken);
            if (StatusCode != 200)
            {
                context.Response.StatusCode = StatusCode;
                context.Response.Close();
                return;
            }
            if (Scenario == "malformed")
            {
                await WriteAsync(context.Response, new { status = "accepted" }, cancellationToken);
                return;
            }

            var scores = Enumerable.Repeat(0.05, _labels.Length).ToArray();
            switch (Scenario)
            {
                case "low":
                    scores[Array.IndexOf(_labels, "joy")] = 0.9;
                    break;
                case "ambiguous":
                    scores[Array.IndexOf(_labels, "joy")] = 0.98;
                    scores[Array.IndexOf(_labels, "grief")] = 0.8;
                    break;
                case "unmapped-leading":
                    scores[Array.IndexOf(_labels, "neutral")] = 0.99;
                    scores[Array.IndexOf(_labels, "joy")] = 0.8;
                    break;
                default:
                    scores[Array.IndexOf(_labels, "joy")] = 0.98;
                    scores[Array.IndexOf(_labels, "amusement")] = 0.9;
                    break;
            }

            var order = Enumerable.Range(0, scores.Length)
                .OrderByDescending(index => scores[index]).ToArray();
            var decision = order.Where(index => scores[index] >= _thresholds[index]).ToArray();
            var eligible = scores[order[0]] >= _selectiveThreshold;
            await WriteAsync(context.Response, new
            {
                model_version = "roberta_large_asl_en_01",
                language = "en",
                status = eligible ? "accepted" : "needs_confirmation",
                suggested_emotion = _labels[order[0]],
                score = scores[order[0]],
                score_type = "uncalibrated_sigmoid_not_accuracy",
                requires_confirmation = true,
                decision_eligible = eligible,
                decision_candidates = decision.Select(index => new { label = _labels[index], score = scores[index] }),
                predicted_labels = decision.Select(index => _labels[index]),
                candidates = order.Take(5).Select(index => new { label = _labels[index], score = scores[index] }),
                emotion_scores = _labels.Select((label, index) => (label, index))
                    .ToDictionary(item => item.label, item => scores[item.index])
            }, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (HttpListenerException) { }
    }

    private static async Task WriteAsync(HttpListenerResponse response, object body, CancellationToken cancellationToken)
    {
        response.ContentType = "application/json";
        await JsonSerializer.SerializeAsync(response.OutputStream, body, cancellationToken: cancellationToken);
        response.Close();
    }

    public void Dispose()
    {
        _stop?.Cancel();
        if (_listener.IsListening) _listener.Stop();
        try { _loop?.GetAwaiter().GetResult(); } catch (HttpListenerException) { }
        _stop?.Dispose();
        _listener.Close();
    }
}
