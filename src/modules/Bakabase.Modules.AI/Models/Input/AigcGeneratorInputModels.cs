using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.AI.Models.Domain;

namespace Bakabase.Modules.AI.Models.Input;

public record AigcGeneratorPropertyPresetInputModel
{
    public required PropertyPool Pool { get; init; }
    public required int PropertyId { get; init; }
    /// <summary>Serialized BizValue (PropertySystem.Value.Serialize).</summary>
    public string? SerializedBizValue { get; init; }
}

public record AigcGeneratorAddInputModel
{
    public required string Name { get; init; }
    public required int ProviderId { get; init; }
    public required AigcMediaType MediaType { get; init; }
    public string? PromptTemplate { get; init; }
    public string? NegativePromptTemplate { get; init; }
    public string? ParametersJson { get; init; }
    public string FilenameTemplate { get; init; } = "{run}_{ordinal}_{timestamp}";
    public AigcArtifactResourceMode ResourceMode { get; init; } = AigcArtifactResourceMode.PerArtifact;
    public bool AllowDeletion { get; init; } = true;
    public bool IsEnabled { get; init; } = true;
    public List<AigcGeneratorPropertyPresetInputModel>? PropertyPresets { get; init; }
}

public record AigcGeneratorUpdateInputModel
{
    public string? Name { get; init; }
    public int? ProviderId { get; init; }
    public AigcMediaType? MediaType { get; init; }
    public string? PromptTemplate { get; init; }
    public string? NegativePromptTemplate { get; init; }
    public string? ParametersJson { get; init; }
    public string? FilenameTemplate { get; init; }
    public AigcArtifactResourceMode? ResourceMode { get; init; }
    public bool? AllowDeletion { get; init; }
    public bool? IsEnabled { get; init; }
    /// <summary>If non-null, fully replaces the generator's preset list.</summary>
    public List<AigcGeneratorPropertyPresetInputModel>? PropertyPresets { get; init; }
}

public record AigcGenerationTriggerInputModel
{
    /// <summary>If supplied, overrides the generator's PromptTemplate for this run only.</summary>
    public string? PromptOverride { get; init; }
    public string? NegativePromptOverride { get; init; }
    /// <summary>Optional one-off parameters merged on top of the generator's ParametersJson.</summary>
    public Dictionary<string, object?>? ParameterOverrides { get; init; }
}

public record AigcArtifactImportInputModel
{
    /// <summary>Absolute file paths to import. Files are moved into the generator's directory.</summary>
    public required List<string> SourceFilePaths { get; init; }
}

public record AigcGeneratorComfyUIImportInputModel
{
    /// <summary>Target ComfyUI AI provider. All imported generators will be bound to this provider.</summary>
    public required int ProviderId { get; init; }

    /// <summary>Absolute paths to files or folders. Folders are walked recursively for *.json files.</summary>
    public required List<string> Paths { get; init; }
}

public enum AigcGeneratorComfyUIImportStatus
{
    Imported = 1,
    SkippedDuplicate = 2,
    SkippedInvalidJson = 3,
    SkippedNotComfyUIWorkflow = 4,
    Failed = 5,
}

public record AigcGeneratorComfyUIImportItemResult
{
    public required string Path { get; init; }
    public required AigcGeneratorComfyUIImportStatus Status { get; init; }
    /// <summary>Human-readable reason for non-Imported statuses; the generator id when Imported.</summary>
    public string? Reason { get; init; }
    public int? GeneratorId { get; init; }
}

public record AigcGeneratorComfyUIImportResult
{
    public int ImportedCount { get; init; }
    public int SkippedCount { get; init; }
    public int FailedCount { get; init; }
    public required List<AigcGeneratorComfyUIImportItemResult> Items { get; init; }
}
