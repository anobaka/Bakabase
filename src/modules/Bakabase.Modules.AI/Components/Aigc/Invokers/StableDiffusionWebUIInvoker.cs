using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bakabase.Modules.AI.Models.Db;
using Bakabase.Modules.AI.Models.Domain;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.AI.Components.Aigc.Invokers;

/// <summary>
/// Auto1111 / SD WebUI compatible. POST {endpoint}/sdapi/v1/txt2img.
/// </summary>
public class StableDiffusionWebUIInvoker(IHttpClientFactory httpClientFactory, ILogger<StableDiffusionWebUIInvoker> logger)
    : IAigcProviderInvoker
{
    public AiProviderKind Kind => AiProviderKind.StableDiffusionWebUI;
    public AigcMediaType[] SupportedMediaTypes => [AigcMediaType.Image];

    public async Task<bool> TestConnectionAsync(AiProviderDbModel config, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(config.Endpoint)) return false;
        try
        {
            using var client = CreateClient(config);
            var resp = await client.GetAsync("/sdapi/v1/sd-models", ct);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "SD WebUI test connection failed for {Endpoint}", config.Endpoint);
            return false;
        }
    }

    public async Task<AigcInvocationResult> InvokeAsync(AiProviderDbModel config,
        AigcInvocationRequest request, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(config.Endpoint))
            throw new InvalidOperationException("SD WebUI endpoint is not configured.");

        var body = new JsonObject
        {
            ["prompt"] = request.Prompt ?? string.Empty,
            ["negative_prompt"] = request.NegativePrompt ?? string.Empty,
            ["steps"] = request.Parameters.GetParam<int?>("steps") ?? 20,
            ["cfg_scale"] = request.Parameters.GetParam<double?>("cfg_scale") ?? 7.0,
            ["width"] = request.Parameters.GetParam<int?>("width") ?? 512,
            ["height"] = request.Parameters.GetParam<int?>("height") ?? 512,
            ["batch_size"] = request.Parameters.GetParam<int?>("batch_size") ?? 1,
            ["n_iter"] = request.Parameters.GetParam<int?>("n_iter") ?? 1
        };

        var sampler = request.Parameters.GetParam<string?>("sampler_name");
        if (!string.IsNullOrEmpty(sampler)) body["sampler_name"] = sampler;
        var seed = request.Parameters.GetParam<long?>("seed");
        if (seed.HasValue) body["seed"] = seed.Value;
        var modelName = request.Parameters.GetParam<string?>("model");
        if (!string.IsNullOrEmpty(modelName))
        {
            body["override_settings"] = new JsonObject { ["sd_model_checkpoint"] = modelName };
        }

        var rawReq = body.ToJsonString();

        using var client = CreateClient(config);
        var resp = await client.PostAsJsonAsync("/sdapi/v1/txt2img", body, ct);
        var rawResp = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"SD WebUI returned {(int)resp.StatusCode}: {rawResp}");
        }

        var json = JsonNode.Parse(rawResp);
        var images = json?["images"] as JsonArray ?? [];
        var outputs = new List<AigcGenerationOutput>();
        foreach (var img in images)
        {
            var b64 = img?.GetValue<string>();
            var bytes = AigcInvokerHelpers.DecodeBase64Loose(b64);
            if (bytes == null) continue;
            outputs.Add(new AigcGenerationOutput
            {
                MediaType = AigcMediaType.Image,
                Content = bytes,
                SuggestedExtension = "png"
            });
        }

        return new AigcInvocationResult
        {
            Outputs = outputs,
            RawRequestPayload = rawReq,
            RawResponsePayload = TruncatePayload(rawResp)
        };
    }

    private HttpClient CreateClient(AiProviderDbModel config)
    {
        var client = httpClientFactory.CreateClient("aigc-sd-webui");
        client.BaseAddress = new Uri(config.Endpoint!);
        client.Timeout = TimeSpan.FromMinutes(10);
        if (!string.IsNullOrEmpty(config.ApiKey))
        {
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", config.ApiKey);
        }
        return client;
    }

    /// <summary>SD WebUI responses include base64 images that bloat the DB; trim them for storage.</summary>
    private static string TruncatePayload(string raw)
    {
        if (raw.Length <= 8 * 1024) return raw;
        return raw[..(8 * 1024)] + "...[truncated]";
    }
}
