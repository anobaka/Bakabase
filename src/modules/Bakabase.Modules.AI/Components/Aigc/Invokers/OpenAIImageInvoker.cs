using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bakabase.Modules.AI.Models.Db;
using Bakabase.Modules.AI.Models.Domain;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.AI.Components.Aigc.Invokers;

/// <summary>
/// OpenAI Images API: POST {endpoint}/v1/images/generations.
/// Defaults to <c>dall-e-3</c>; override via parameter <c>model</c>. Returns base64-encoded images.
/// </summary>
public class OpenAIImageInvoker(IHttpClientFactory httpClientFactory, ILogger<OpenAIImageInvoker> logger)
    : IAigcProviderInvoker
{
    public AiProviderKind Kind => AiProviderKind.OpenAI;
    public AigcMediaType[] SupportedMediaTypes => [AigcMediaType.Image];

    private const string DefaultEndpoint = "https://api.openai.com";

    public async Task<bool> TestConnectionAsync(AiProviderDbModel config, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(config.ApiKey)) return false;
        try
        {
            using var client = CreateClient(config);
            var resp = await client.GetAsync("/v1/models", ct);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "OpenAI Image test connection failed");
            return false;
        }
    }

    public async Task<AigcInvocationResult> InvokeAsync(AiProviderDbModel config,
        AigcInvocationRequest request, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(config.ApiKey))
            throw new InvalidOperationException("OpenAI Image API key is not configured.");

        var model = request.Parameters.GetParam<string?>("model") ?? "dall-e-3";
        var size = request.Parameters.GetParam<string?>("size") ?? "1024x1024";
        var quality = request.Parameters.GetParam<string?>("quality");
        var n = request.Parameters.GetParam<int?>("n") ?? 1;

        var body = new JsonObject
        {
            ["model"] = model,
            ["prompt"] = request.Prompt ?? string.Empty,
            ["n"] = n,
            ["size"] = size,
            ["response_format"] = "b64_json"
        };
        if (!string.IsNullOrEmpty(quality)) body["quality"] = quality;

        var rawReq = body.ToJsonString();

        using var client = CreateClient(config);
        var resp = await client.PostAsJsonAsync("/v1/images/generations", body, ct);
        var rawResp = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"OpenAI Image returned {(int)resp.StatusCode}: {rawResp}");
        }

        var json = JsonNode.Parse(rawResp);
        var data = json?["data"] as JsonArray ?? [];
        var outputs = new List<AigcGenerationOutput>();
        foreach (var item in data)
        {
            var b64 = item?["b64_json"]?.GetValue<string>();
            var bytes = AigcInvokerHelpers.DecodeBase64Loose(b64);
            if (bytes == null) continue;
            outputs.Add(new AigcGenerationOutput
            {
                MediaType = AigcMediaType.Image,
                Content = bytes,
                SuggestedExtension = "png",
                Metadata = item?["revised_prompt"]?.GetValue<string>() is { } rp
                    ? new Dictionary<string, string> { ["revised_prompt"] = rp }
                    : null
            });
        }

        return new AigcInvocationResult
        {
            Outputs = outputs,
            RawRequestPayload = rawReq,
            RawResponsePayload = rawResp.Length > 8 * 1024 ? rawResp[..(8 * 1024)] + "...[truncated]" : rawResp
        };
    }

    private HttpClient CreateClient(AiProviderDbModel config)
    {
        var client = httpClientFactory.CreateClient("aigc-openai-image");
        client.BaseAddress = new Uri(string.IsNullOrEmpty(config.Endpoint) ? DefaultEndpoint : config.Endpoint);
        client.Timeout = TimeSpan.FromMinutes(5);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);
        return client;
    }
}
