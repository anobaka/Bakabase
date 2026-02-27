using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.ResourceResolver.Abstractions;

/// <summary>
/// A resource discovered by a Resolver.
/// </summary>
public record ResolvedResource
{
    /// <summary>
    /// Unique identifier within the source (e.g., AppId for Steam, WorkId for DLsite).
    /// </summary>
    public string SourceKey { get; set; } = null!;

    /// <summary>
    /// Human-readable name for display.
    /// </summary>
    public string DisplayName { get; set; } = null!;

    /// <summary>
    /// Local file path, if the resource has a local presence (e.g., installed game directory).
    /// Null for resources without local files (e.g., uninstalled Steam games).
    /// </summary>
    public string? Path { get; set; }

    /// <summary>
    /// The source this resource was discovered from.
    /// </summary>
    public ResourceSource Source { get; set; }
}

/// <summary>
/// Cover image data retrieved by a Resolver.
/// </summary>
public record ResolvedCover
{
    /// <summary>
    /// Already-downloaded image bytes (preferred if available).
    /// </summary>
    public byte[]? ImageData { get; set; }

    /// <summary>
    /// URL to the cover image (used if ImageData is null).
    /// </summary>
    public string? ImageUrl { get; set; }
}

/// <summary>
/// A FileSystem resource identified as a candidate for migration to another source.
/// </summary>
public record MigrationCandidate
{
    /// <summary>
    /// The original FileSystem resource.
    /// </summary>
    public Resource OriginalResource { get; set; } = null!;

    /// <summary>
    /// The SourceKey that will be assigned after migration.
    /// </summary>
    public string SourceKey { get; set; } = null!;

    /// <summary>
    /// Confidence score (0.0 - 1.0) for the match.
    /// </summary>
    public float Confidence { get; set; }
}

/// <summary>
/// Schema describing the configuration options for a Resolver.
/// Used by the frontend to render a dynamic settings form.
/// </summary>
public record ResolverConfigurationSchema
{
    public List<ResolverConfigField> Fields { get; set; } = [];
}

/// <summary>
/// A single configuration field in a Resolver's settings.
/// </summary>
public record ResolverConfigField
{
    public string Key { get; set; } = null!;
    public string Label { get; set; } = null!;
    public string? Description { get; set; }
    public ResolverConfigFieldType Type { get; set; }
    public bool Required { get; set; }
}

public enum ResolverConfigFieldType
{
    String = 1,
    Password = 2,
    StringList = 3,
    DirectoryList = 4,
}

/// <summary>
/// Default player configuration for a source.
/// </summary>
public record ResolverPlayerConfig
{
    /// <summary>
    /// URI template for launching the resource (e.g., "steam://rungameid/{SourceKey}").
    /// </summary>
    public string? UriTemplate { get; set; }

    /// <summary>
    /// Whether to use Locale Emulator when launching executables.
    /// </summary>
    public bool UseLocaleEmulator { get; set; }
}

/// <summary>
/// Interface for selecting playable files from a resource.
/// </summary>
public interface IPlayableFileSelector
{
    /// <summary>
    /// Selects the playable files from a resource's local directory.
    /// </summary>
    Task<List<string>> SelectPlayableFiles(string resourcePath, CancellationToken ct);
}
