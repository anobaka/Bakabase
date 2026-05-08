using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bakabase.Modules.AI.Models.Db;
using Bakabase.Modules.AI.Models.Domain;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.AI.Components.Aigc.Invokers;

/// <summary>
/// Generic HTTP invoker driven entirely by <see cref="AigcProviderConfigDbModel.ConfigJson"/>.
///
/// Schema:
/// {
///   "method": "POST",
///   "urlTemplate": "https://api.example.com/generate",
///   "headers": { "Authorization": "Bearer {apiKey}" },
///   "bodyTemplate": "{\"prompt\":\"{prompt}\",\"seed\":{seed}}",
///   "asyncMode": "none" | "polling",
///   "polling": {
///     "submitIdPath": "$.task_id",
///     "statusUrlTemplate": "https://api.example.com/task/{task_id}",
///     "donePath": "$.status",
///     "doneEquals": "succeeded",
///     "intervalMs": 2000
///   },
///   "outputs": [
///     { "mediaType": "Image",
///       "extractFromPath": "$.images[*]",
///       "encoding": "base64" | "url",
///       "extension": "png" }
///   ]
/// }
///
/// Templates support the following tokens (placed inside <c>{...}</c>):
///   prompt, negativePrompt, apiKey, seed, plus arbitrary keys from generator parameters.
/// </summary>
public class HttpCustomInvoker(IHttpClientFactory httpClientFactory, ILogger<HttpCustomInvoker> logger)
    : IAigcProviderInvoker
{
    public AigcProviderKind Kind => AigcProviderKind.HttpCustom;
    public string DisplayName => "Custom HTTP";
    public AigcMediaType[] SupportedMediaTypes => [AigcMediaType.Image, AigcMediaType.Text, AigcMediaType.Audio, AigcMediaType.Video, AigcMediaType.Other];
    public bool RequiresApiKey => false;
    public bool RequiresEndpoint => false;
    public string? DefaultEndpoint => null;

    public Task<bool> TestConnectionAsync(AigcProviderConfigDbModel config, CancellationToken ct)
    {
        // No standardized health-check for arbitrary endpoints; consider configured if the JSON parses.
        if (string.IsNullOrEmpty(config.ConfigJson)) return Task.FromResult(false);
        var node = AigcInvokerHelpers.ParseLenient(config.ConfigJson);
        return Task.FromResult(node?["urlTemplate"]?.GetValue<string>() is { Length: > 0 });
    }

    public async Task<AigcInvocationResult> InvokeAsync(AigcProviderConfigDbModel config,
        AigcInvocationRequest request, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(config.ConfigJson))
            throw new InvalidOperationException("HttpCustom provider has no ConfigJson.");

        var cfg = AigcInvokerHelpers.ParseLenient(config.ConfigJson)
                  ?? throw new InvalidOperationException("HttpCustom ConfigJson is not valid JSON.");

        var tokens = BuildTokens(config, request);
        var method = cfg["method"]?.GetValue<string>() ?? "POST";
        var urlTemplate = cfg["urlTemplate"]?.GetValue<string>()
                          ?? throw new InvalidOperationException("HttpCustom config missing urlTemplate.");
        var bodyTemplate = cfg["bodyTemplate"]?.GetValue<string>();

        using var client = httpClientFactory.CreateClient("aigc-http-custom");
        client.Timeout = TimeSpan.FromMinutes(15);

        var rawReqBody = bodyTemplate is null ? null : AigcInvokerHelpers.ResolveTemplate(bodyTemplate, tokens);
        var resolvedUrl = AigcInvokerHelpers.ResolveTemplate(urlTemplate, tokens);

        var resp = await SendAsync(client, cfg, method, resolvedUrl, rawReqBody, tokens, ct);
        var rawText = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"HttpCustom returned {(int)resp.StatusCode}: {rawText}");

        var responseNode = AigcInvokerHelpers.ParseLenient(rawText);

        // Optional polling
        var asyncMode = cfg["asyncMode"]?.GetValue<string>();
        if (string.Equals(asyncMode, "polling", StringComparison.OrdinalIgnoreCase) && cfg["polling"] is JsonObject pcfg)
        {
            responseNode = await PollAsync(client, pcfg, responseNode, tokens, request.OnProgress, ct);
        }

        var outputsConfig = cfg["outputs"] as JsonArray ?? [];
        var outputs = new List<AigcGenerationOutput>();
        foreach (var oc in outputsConfig)
        {
            if (oc is null) continue;
            var mediaTypeStr = oc["mediaType"]?.GetValue<string>() ?? "Image";
            var mediaType = Enum.TryParse<AigcMediaType>(mediaTypeStr, true, out var mt) ? mt : AigcMediaType.Other;
            var path = oc["extractFromPath"]?.GetValue<string>() ?? "$";
            var encoding = oc["encoding"]?.GetValue<string>() ?? "base64";
            var extension = oc["extension"]?.GetValue<string>()
                            ?? (mediaType == AigcMediaType.Image ? "png"
                                : mediaType == AigcMediaType.Video ? "mp4"
                                : mediaType == AigcMediaType.Audio ? "mp3"
                                : mediaType == AigcMediaType.Text ? "txt" : "bin");

            var nodes = AigcInvokerHelpers.SelectNodes(responseNode, path);
            foreach (var node in nodes)
            {
                if (node is null) continue;
                var value = node is JsonValue jv ? jv.ToString() : node.ToString();
                byte[]? bytes = null;
                if (string.Equals(encoding, "base64", StringComparison.OrdinalIgnoreCase))
                {
                    bytes = AigcInvokerHelpers.DecodeBase64Loose(value);
                }
                else if (string.Equals(encoding, "url", StringComparison.OrdinalIgnoreCase))
                {
                    if (Uri.TryCreate(value, UriKind.Absolute, out _))
                    {
                        bytes = await client.GetByteArrayAsync(value, ct);
                    }
                }
                else if (string.Equals(encoding, "text", StringComparison.OrdinalIgnoreCase))
                {
                    bytes = Encoding.UTF8.GetBytes(value);
                }
                if (bytes is null) continue;
                outputs.Add(new AigcGenerationOutput
                {
                    MediaType = mediaType,
                    Content = bytes,
                    SuggestedExtension = extension
                });
            }
        }

        return new AigcInvocationResult
        {
            Outputs = outputs,
            RawRequestPayload = rawReqBody,
            RawResponsePayload = rawText.Length > 16 * 1024 ? rawText[..(16 * 1024)] + "...[truncated]" : rawText
        };
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, JsonNode cfg, string method,
        string url, string? body, IReadOnlyDictionary<string, string?> tokens, CancellationToken ct)
    {
        var req = new HttpRequestMessage(new HttpMethod(method.ToUpperInvariant()), url);
        if (cfg["headers"] is JsonObject headers)
        {
            foreach (var (k, v) in headers)
            {
                var resolved = AigcInvokerHelpers.ResolveTemplate(v?.GetValue<string>() ?? string.Empty, tokens);
                req.Headers.TryAddWithoutValidation(k, resolved);
            }
        }
        if (body is not null && method.ToUpperInvariant() != "GET")
        {
            req.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }
        return await client.SendAsync(req, ct);
    }

    private async Task<JsonNode?> PollAsync(HttpClient client, JsonObject pcfg, JsonNode? submitResponse,
        Dictionary<string, string?> tokens,
        Func<int, string?, CancellationToken, Task>? onProgress, CancellationToken ct)
    {
        var idPath = pcfg["submitIdPath"]?.GetValue<string>() ?? "$.id";
        var statusUrlTemplate = pcfg["statusUrlTemplate"]?.GetValue<string>()
                                ?? throw new InvalidOperationException("polling.statusUrlTemplate is required");
        var donePath = pcfg["donePath"]?.GetValue<string>() ?? "$.status";
        var doneEquals = pcfg["doneEquals"]?.GetValue<string>() ?? "succeeded";
        var interval = pcfg["intervalMs"]?.GetValue<int?>() ?? 2000;

        var idNodes = AigcInvokerHelpers.SelectNodes(submitResponse, idPath);
        var taskId = idNodes.FirstOrDefault()?.ToString();
        if (string.IsNullOrEmpty(taskId))
            throw new InvalidOperationException("Polling configured but submit response did not contain id.");
        tokens["task_id"] = taskId;

        var statusUrl = AigcInvokerHelpers.ResolveTemplate(statusUrlTemplate, tokens);

        var attempts = 0;
        while (!ct.IsCancellationRequested)
        {
            attempts++;
            await Task.Delay(interval, ct);
            var resp = await client.GetAsync(statusUrl, ct);
            if (!resp.IsSuccessStatusCode) continue;
            var text = await resp.Content.ReadAsStringAsync(ct);
            var node = AigcInvokerHelpers.ParseLenient(text);
            var status = AigcInvokerHelpers.SelectNodes(node, donePath).FirstOrDefault()?.ToString();
            if (string.Equals(status, doneEquals, StringComparison.OrdinalIgnoreCase))
            {
                return node;
            }
            if (onProgress is not null)
            {
                await onProgress(0, $"Polling ({attempts}, status={status})", ct);
            }
        }
        ct.ThrowIfCancellationRequested();
        return submitResponse;
    }

    private static Dictionary<string, string?> BuildTokens(AigcProviderConfigDbModel config, AigcInvocationRequest request)
    {
        var tokens = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["prompt"] = JsonEscape(request.Prompt ?? string.Empty),
            ["negativePrompt"] = JsonEscape(request.NegativePrompt ?? string.Empty),
            ["apiKey"] = config.ApiKey ?? string.Empty,
            ["endpoint"] = config.Endpoint ?? string.Empty,
            ["seed"] = (request.Parameters.GetParam<long?>("seed") ?? Random.Shared.NextInt64(0, int.MaxValue)).ToString()
        };
        foreach (var (k, v) in request.Parameters)
        {
            if (v is null) continue;
            tokens[k] = v switch
            {
                string s => s,
                JsonElement je => je.ToString(),
                _ => v.ToString()
            };
        }
        return tokens;
    }

    private static string JsonEscape(string s) => JsonEncodedText.Encode(s).Value;
}
