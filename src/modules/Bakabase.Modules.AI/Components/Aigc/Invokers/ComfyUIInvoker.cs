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
/// provider-default <c>config.AigcConfigJson["defaultWorkflow"]</c>. Tokens "{prompt}", "{negativePrompt}",
/// "{seed}" inside the workflow string are substituted before submission.
/// </summary>
public class ComfyUIInvoker(IHttpClientFactory httpClientFactory, ILogger<ComfyUIInvoker> logger)
    : IAigcProviderInvoker
{
    public AiProviderKind Kind => AiProviderKind.ComfyUI;
    public AigcMediaType[] SupportedMediaTypes => [AigcMediaType.Image, AigcMediaType.Video];

    public async Task<bool> TestConnectionAsync(AiProviderDbModel config, CancellationToken ct)
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

    public async Task<AigcInvocationResult> InvokeAsync(AiProviderDbModel config,
        AigcInvocationRequest request, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(config.Endpoint))
            throw new InvalidOperationException("ComfyUI endpoint is not configured.");

        var workflowText = ReadGeneratorWorkflow(request);
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

        var submitParsed = JsonNode.Parse(submitText);
        var promptId = submitParsed?["prompt_id"]?.GetValue<string>();
        if (string.IsNullOrEmpty(promptId))
            throw new InvalidOperationException("ComfyUI did not return prompt_id: " + submitText);

        // ComfyUI returns 200 + prompt_id even when the workflow has validation errors —
        // the prompt is accepted into the queue but never executed. Detect that here so
        // we don't end up polling history forever for a prompt that won't run.
        if (submitParsed?["node_errors"] is JsonObject nodeErrors && nodeErrors.Count > 0)
        {
            throw new InvalidOperationException(
                $"ComfyUI rejected the workflow with node_errors (prompt_id={promptId}): {nodeErrors.ToJsonString()}");
        }

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

        // ComfyUI history entry has a "status" object that says whether the run actually
        // succeeded (status_str = "success" / "error"). When it errored, "messages" carries
        // the per-node failures — surface them.
        var statusObj = history["status"] as JsonObject;
        var statusStr = statusObj?["status_str"]?.GetValue<string>();
        if (statusStr == "error")
        {
            throw new InvalidOperationException(
                $"ComfyUI execution failed (prompt_id={promptId}). Status: {statusObj?.ToJsonString() ?? "<none>"}");
        }

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

        if (outputs.Count == 0)
        {
            // Status said success but we got nothing. The most common cause is ComfyUI
            // caching the entire output chain when the workflow is byte-identical to a
            // previous submission — surfaced via the "execution_cached" status message.
            var cachedNodes = ExtractCachedNodes(statusObj);
            if (cachedNodes.Count > 0)
            {
                logger.LogWarning(
                    "ComfyUI cached all output nodes ({Nodes}). prompt_id={PromptId}, history entry: {Entry}",
                    string.Join(",", cachedNodes), promptId, history.ToJsonString());
                throw new InvalidOperationException(
                    $"ComfyUI cached all output-producing nodes ([{string.Join(",", cachedNodes)}]) so no new files were saved. " +
                    $"This happens when the submitted workflow is byte-identical to a previous one. " +
                    $"Fix: add a {{seed}} token to some node input (e.g. SaveImage's filename_prefix: \"ComfyUI_{{seed}}\") " +
                    $"so each run differs and ComfyUI re-executes the chain.");
            }

            logger.LogWarning("ComfyUI returned status={Status} but zero image outputs. " +
                              "prompt_id={PromptId}, full history entry: {Entry}",
                statusStr ?? "<missing>", promptId, history.ToJsonString());
            throw new InvalidOperationException(
                $"ComfyUI completed but produced no image outputs (prompt_id={promptId}, status={statusStr ?? "<missing>"}). " +
                $"Full history entry: {history.ToJsonString()}");
        }

        return new AigcInvocationResult
        {
            Outputs = outputs,
            RawRequestPayload = rawReq.Length > 16 * 1024 ? rawReq[..(16 * 1024)] + "...[truncated]" : rawReq,
            RawResponsePayload = history.ToJsonString()
        };
    }

    /// <summary>
    /// Read the per-generator workflow from request.Parameters. Accepts either a string
    /// (legacy / template-with-tokens) or a JsonElement object (current import flow stores
    /// the workflow as a structured object).
    /// </summary>
    private static string? ReadGeneratorWorkflow(AigcInvocationRequest request)
    {
        if (!request.Parameters.TryGetValue("workflow", out var raw) || raw is null) return null;
        return raw switch
        {
            string s => s,
            JsonElement je => je.ValueKind switch
            {
                JsonValueKind.String => je.GetString(),
                JsonValueKind.Object or JsonValueKind.Array => je.GetRawText(),
                _ => null
            },
            JsonNode jn => jn.ToJsonString(),
            _ => raw.ToString()
        };
    }

    /// <summary>
    /// Pull cached-node ids out of a ComfyUI history-entry status object. The shape is:
    ///   { "messages": [ ["execution_cached", { "nodes": ["1","2"], ... }], ... ] }
    /// </summary>
    private static List<string> ExtractCachedNodes(JsonObject? statusObj)
    {
        var result = new List<string>();
        if (statusObj?["messages"] is not JsonArray messages) return result;

        foreach (var msg in messages)
        {
            if (msg is not JsonArray arr || arr.Count < 2) continue;
            if (arr[0]?.GetValue<string>() != "execution_cached") continue;
            if (arr[1] is not JsonObject info) continue;
            if (info["nodes"] is not JsonArray nodes) continue;

            foreach (var n in nodes)
            {
                var s = n?.GetValue<string>();
                if (!string.IsNullOrEmpty(s)) result.Add(s);
            }
        }
        return result;
    }

    private static string? ReadDefaultWorkflow(AiProviderDbModel config)
    {
        if (string.IsNullOrEmpty(config.AigcConfigJson)) return null;
        var parsed = AigcInvokerHelpers.ParseLenient(config.AigcConfigJson);
        if (parsed is null) return null;

        // Wrapped form: { "defaultWorkflow": { ... } }
        if (parsed["defaultWorkflow"] is { } wrapped) return wrapped.ToJsonString();

        // Bare form: user pasted the Export (API) JSON directly.
        // A ComfyUI API-format workflow is an object whose values carry "class_type".
        if (parsed is JsonObject obj &&
            obj.Any(kv => kv.Value is JsonObject n && n.ContainsKey("class_type")))
        {
            return obj.ToJsonString();
        }

        return null;
    }

    private static string JsonEscape(string s)
    {
        return JsonEncodedText.Encode(s).Value;
    }

    private HttpClient CreateClient(AiProviderDbModel config)
    {
        var client = httpClientFactory.CreateClient("aigc-comfyui");
        client.BaseAddress = new Uri(config.Endpoint!);
        client.Timeout = TimeSpan.FromMinutes(15);
        return client;
    }
}
