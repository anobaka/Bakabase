namespace Bakabase.Abstractions.Models.Domain.Options;

/// <summary>
/// Per-enhancer translation configuration.
/// When enabled, enhancement result values will be translated using the Translation AI feature config.
/// </summary>
public record EnhancerTranslationOptions
{
    public bool Enabled { get; set; }
    public string TargetLanguage { get; set; } = "en-US";
}
