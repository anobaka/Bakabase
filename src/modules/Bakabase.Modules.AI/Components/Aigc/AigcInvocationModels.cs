using Bakabase.Modules.AI.Models.Domain;

namespace Bakabase.Modules.AI.Components.Aigc;

/// <summary>
/// One generation request, after the generator's templates have been resolved.
/// </summary>
public record AigcInvocationRequest
{
    public string? Prompt { get; init; }
    public string? NegativePrompt { get; init; }
    public AigcMediaType MediaType { get; init; }

    /// <summary>
    /// Free-form parameters merged from the generator's ParametersJson.
    /// Common keys: width, height, steps, sampler, seed, cfg_scale, model, batch_size.
    /// Unknown keys are ignored by invokers that don't understand them.
    /// </summary>
    public Dictionary<string, object?> Parameters { get; init; } = new();

    /// <summary>
    /// Optional progress callback for long-running operations (ComfyUI, polling-based providers).
    /// </summary>
    public Func<int, string?, CancellationToken, Task>? OnProgress { get; init; }
}

public record AigcInvocationResult
{
    public required IReadOnlyList<AigcGenerationOutput> Outputs { get; init; }
    public string? RawRequestPayload { get; init; }
    public string? RawResponsePayload { get; init; }
}

public record AigcGenerationOutput
{
    public required AigcMediaType MediaType { get; init; }
    /// <summary>Output bytes. Caller is responsible for writing to disk.</summary>
    public required byte[] Content { get; init; }
    /// <summary>Suggested file extension without leading dot (e.g. "png", "mp4", "txt").</summary>
    public required string SuggestedExtension { get; init; }
    /// <summary>Echoed metadata (seed, model, etc.) for diagnostic purposes.</summary>
    public Dictionary<string, string>? Metadata { get; init; }
}
