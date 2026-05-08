using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bakabase.Modules.AI.Models.Db;
using Bakabase.Modules.AI.Models.Domain;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.AI.Components.Aigc.Invokers;

/// <summary>
/// Google Gemini image generation a.k.a. "Nano Banana".
/// POST {endpoint}/v1beta/models/{model}:generateContent?key={apiKey}
/// </summary>
public class GeminiImageInvoker(IHttpClientFactory httpClientFactory, ILogger<GeminiImageInvoker> logger)
    : IAigcProviderInvoker
{
    public AigcProviderKind Kind => AigcProviderKind.GeminiImage;
    public string DisplayName => "Gemini Image (Nano Banana)";
    public AigcMediaType[] SupportedMediaTypes => [AigcMediaType.Image];
    public bool RequiresApiKey => true;
    public bool RequiresEndpoint => false;
    public string? DefaultEndpoint => "https://generativelanguage.googleapis.com";

    public async Task<bool> TestConnectionAsync(AigcProviderConfigDbModel config, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(config.ApiKey)) return false;
        try
        {
            using var client = CreateClient(config);
            var resp = await client.GetAsync($"/v1beta/models?key={config.ApiKey}", ct);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Gemini Image test connection failed");
            return false;
        }
    }

    public async Task<AigcInvocationResult> InvokeAsync(AigcProviderConfigDbModel config,
        AigcInvocationRequest request, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(config.ApiKey))
            throw new InvalidOperationException("Gemini Image API key is not configured.");

        var model = request.Parameters.GetParam<string?>("model") ?? "gemini-2.5-flash-image-preview";
        var promptText = request.Prompt ?? string.Empty;
        if (!string.IsNullOrEmpty(request.NegativePrompt))
            promptText += "\n\nNegative: " + request.NegativePrompt;

        var body = new JsonObject
        {
            ["contents"] = new JsonArray
            {
                new JsonObject
                {
                    ["parts"] = new JsonArray { new JsonObject { ["text"] = promptText } }
                }
            },
            ["generationConfig"] = new JsonObject
            {
                ["responseModalities"] = new JsonArray { "IMAGE", "TEXT" }
            }
        };

        var rawReq = body.ToJsonString();

        using var client = CreateClient(config);
        var url = $"/v1beta/models/{Uri.EscapeDataString(model)}:generateContent?key={Uri.EscapeDataString(config.ApiKey)}";
        var resp = await client.PostAsJsonAsync(url, body, ct);
        var rawResp = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Gemini Image returned {(int)resp.StatusCode}: {rawResp}");
        }

        var json = JsonNode.Parse(rawResp);
        var outputs = new List<AigcGenerationOutput>();

        var candidates = json?["candidates"] as JsonArray ?? [];
        foreach (var candidate in candidates)
        {
            var parts = candidate?["content"]?["parts"] as JsonArray;
            if (parts is null) continue;
            foreach (var part in parts)
            {
                var inlineData = part?["inlineData"];
                var data = inlineData?["data"]?.GetValue<string>();
                var mime = inlineData?["mimeType"]?.GetValue<string>();
                var bytes = AigcInvokerHelpers.DecodeBase64Loose(data);
                if (bytes is null) continue;
                outputs.Add(new AigcGenerationOutput
                {
                    MediaType = AigcMediaType.Image,
                    Content = bytes,
                    SuggestedExtension = AigcInvokerHelpers.GuessExtensionFromMime(mime, "png")
                });
            }
        }

        return new AigcInvocationResult
        {
            Outputs = outputs,
            RawRequestPayload = rawReq,
            RawResponsePayload = rawResp.Length > 8 * 1024 ? rawResp[..(8 * 1024)] + "...[truncated]" : rawResp
        };
    }

    private HttpClient CreateClient(AigcProviderConfigDbModel config)
    {
        var client = httpClientFactory.CreateClient("aigc-gemini-image");
        client.BaseAddress = new Uri(string.IsNullOrEmpty(config.Endpoint) ? DefaultEndpoint! : config.Endpoint);
        client.Timeout = TimeSpan.FromMinutes(5);
        return client;
    }
}
