using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bakabase.Modules.AI.Models.Db;
using Bakabase.Modules.AI.Models.Domain;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.AI.Components.Aigc.Invokers;

/// <summary>
/// ComfyUI: submit a workflow JSON, poll /history/{prompt_id} until present, fetch outputs via /view.
/// The workflow JSON is supplied via <c>generator.ParametersJson["workflow"]</c> or
/// provider-default <c>config.ConfigJson["defaultWorkflow"]</c>. Tokens "{prompt}", "{negativePrompt}",
/// "{seed}" inside the workflow string are substituted before submission.
/// </summary>
public class ComfyUIInvoker(IHttpClientFactory httpClientFactory, ILogger<ComfyUIInvoker> logger)
    : IAigcProviderInvoker
{
    public AigcProviderKind Kind => AigcProviderKind.ComfyUI;
    public string DisplayName => "ComfyUI";
    public AigcMediaType[] SupportedMediaTypes => [AigcMediaType.Image, AigcMediaType.Video];
    public bool RequiresApiKey => false;
    public bool RequiresEndpoint => true;
    public string? DefaultEndpoint => "http://127.0.0.1:8188";

    public async Task<bool> TestConnectionAsync(AigcProviderConfigDbModel config, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(config.Endpoint)) return false;
        try
        {
            using var client = CreateClient(config);
            var resp = await client.GetAsync("/system_stats", ct);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "ComfyUI test connection failed for {Endpoint}", config.Endpoint);
            return false;
        }
    }

    public async Task<AigcInvocationResult> InvokeAsync(AigcProviderConfigDbModel config,
        AigcInvocationRequest request, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(config.Endpoint))
            throw new InvalidOperationException("ComfyUI endpoint is not configured.");

        var workflowText = request.Parameters.GetParam<string?>("workflow");
        if (string.IsNullOrEmpty(workflowText))
        {
            workflowText = ReadDefaultWorkflow(config);
        }
        if (string.IsNullOrEmpty(workflowText))
            throw new InvalidOperationException("ComfyUI requires a workflow JSON either on the generator or as provider default.");

        var seed = request.Parameters.GetParam<long?>("seed") ?? Random.Shared.NextInt64(0, int.MaxValue);
        var resolved = AigcInvokerHelpers.ResolveTemplate(workflowText, new Dictionary<string, string?>
        {
            ["prompt"] = JsonEscape(request.Prompt ?? string.Empty),
            ["negativePrompt"] = JsonEscape(request.NegativePrompt ?? string.Empty),
            ["seed"] = seed.ToString()
        });

        JsonNode? workflowNode;
        try { workflowNode = JsonNode.Parse(resolved); }
        catch (JsonException jex)
        {
            throw new InvalidOperationException("ComfyUI workflow JSON failed to parse after template substitution: " + jex.Message);
        }

        var submitBody = new JsonObject { ["prompt"] = workflowNode };
        var rawReq = submitBody.ToJsonString();

        using var client = CreateClient(config);
        var submit = await client.PostAsJsonAsync("/prompt", submitBody, ct);
        var submitText = await submit.Content.ReadAsStringAsync(ct);
        if (!submit.IsSuccessStatusCode)
            throw new InvalidOperationException($"ComfyUI /prompt returned {(int)submit.StatusCode}: {submitText}");

        var promptId = JsonNode.Parse(submitText)?["prompt_id"]?.GetValue<string>();
        if (string.IsNullOrEmpty(promptId))
            throw new InvalidOperationException("ComfyUI did not return prompt_id: " + submitText);

        // Poll history until our prompt_id appears
        JsonNode? history = null;
        var pollCount = 0;
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(2000, ct);
            pollCount++;
            var hresp = await client.GetAsync($"/history/{promptId}", ct);
            if (!hresp.IsSuccessStatusCode) continue;
            var htext = await hresp.Content.ReadAsStringAsync(ct);
            var hnode = JsonNode.Parse(htext);
            if (hnode is JsonObject hobj && hobj.TryGetPropertyValue(promptId, out var entry) && entry is not null)
            {
                history = entry;
                break;
            }

            if (request.OnProgress is not null)
            {
                await request.OnProgress(0, $"Polling ComfyUI ({pollCount})", ct);
            }
        }

        ct.ThrowIfCancellationRequested();
        if (history is null)
            throw new InvalidOperationException("ComfyUI polling ended without history entry.");

        var outputsNode = history["outputs"] as JsonObject;
        var outputs = new List<AigcGenerationOutput>();
        if (outputsNode is not null)
        {
            foreach (var (_, nodeOutputs) in outputsNode)
            {
                var imgs = nodeOutputs?["images"] as JsonArray;
                if (imgs is null) continue;
                foreach (var img in imgs)
                {
                    var fileName = img?["filename"]?.GetValue<string>();
                    var subfolder = img?["subfolder"]?.GetValue<string>() ?? "";
                    var type = img?["type"]?.GetValue<string>() ?? "output";
                    if (string.IsNullOrEmpty(fileName)) continue;
                    var url = $"/view?filename={Uri.EscapeDataString(fileName)}&subfolder={Uri.EscapeDataString(subfolder)}&type={Uri.EscapeDataString(type)}";
                    var bytes = await client.GetByteArrayAsync(url, ct);
                    var ext = Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();
                    if (string.IsNullOrEmpty(ext)) ext = "png";
                    outputs.Add(new AigcGenerationOutput
                    {
                        MediaType = AigcMediaType.Image,
                        Content = bytes,
                        SuggestedExtension = ext
                    });
                }
            }
        }

        return new AigcInvocationResult
        {
            Outputs = outputs,
            RawRequestPayload = rawReq.Length > 16 * 1024 ? rawReq[..(16 * 1024)] + "...[truncated]" : rawReq,
            RawResponsePayload = history.ToJsonString()
        };
    }

    private static string? ReadDefaultWorkflow(AigcProviderConfigDbModel config)
    {
        if (string.IsNullOrEmpty(config.ConfigJson)) return null;
        try
        {
            var parsed = JsonNode.Parse(config.ConfigJson);
            return parsed?["defaultWorkflow"]?.ToJsonString();
        }
        catch { return null; }
    }

    private static string JsonEscape(string s)
    {
        return JsonEncodedText.Encode(s).Value;
    }

    private HttpClient CreateClient(AigcProviderConfigDbModel config)
    {
        var client = httpClientFactory.CreateClient("aigc-comfyui");
        client.BaseAddress = new Uri(config.Endpoint!);
        client.Timeout = TimeSpan.FromMinutes(15);
        return client;
    }
}
