using System.ComponentModel.DataAnnotations;
using Bakabase.Modules.AI.Models.Domain;

namespace Bakabase.Modules.AI.Models.Db;

public record AigcGeneratorDbModel
{
    [Key]
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
    public int ProviderId { get; set; }
    public AigcMediaType MediaType { get; set; }

    public string? PromptTemplate { get; set; }
    public string? NegativePromptTemplate { get; set; }

    /// <summary>
    /// Serialized parameter dictionary merged into invocation requests.
    /// Format: JSON object of arbitrary keys/values understood by the provider invoker.
    /// </summary>
    public string? ParametersJson { get; set; }

    /// <summary>
    /// Filename template for produced artifacts. Supports tokens:
    ///   {run} {ordinal} {timestamp} {ext}
    /// Examples: "{run}_{ordinal}_{timestamp}", "frame_{ordinal}".
    /// Extension is appended automatically.
    /// </summary>
    public string FilenameTemplate { get; set; } = "{run}_{ordinal}_{timestamp}";

    public AigcArtifactResourceMode ResourceMode { get; set; } = AigcArtifactResourceMode.PerArtifact;

    public bool AllowDeletion { get; set; } = true;
    public bool IsEnabled { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
