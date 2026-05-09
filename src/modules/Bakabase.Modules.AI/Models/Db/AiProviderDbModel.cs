using System.ComponentModel.DataAnnotations;
using Bakabase.Modules.AI.Models.Domain;

namespace Bakabase.Modules.AI.Models.Db;

/// <summary>
/// One real-world AI service account/installation. Common credentials live here;
/// per-capability behavior (LLM / AIGC) is gated by enable flags and the kind's
/// declared capabilities.
/// </summary>
public record AiProviderDbModel
{
    [Key]
    public int Id { get; set; }

    public AiProviderKind Kind { get; set; }
    public string Name { get; set; } = string.Empty;

    public string? Endpoint { get; set; }
    public string? ApiKey { get; set; }

    /// <summary>Master switch. When false, neither capability is exposed to consumers.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>Use this provider for LLM features (chat, translation, file processing, etc.).</summary>
    public bool LlmEnabled { get; set; }

    /// <summary>Use this provider for AIGC generators.</summary>
    public bool AigcEnabled { get; set; }

    /// <summary>
    /// Kind-specific configuration JSON for the AIGC capability (workflow templates,
    /// HTTP request templates, etc.). Ignored when <see cref="AigcEnabled"/> is false.
    /// </summary>
    public string? AigcConfigJson { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
