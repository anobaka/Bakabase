namespace Bakabase.Abstractions.Models.Domain;

/// <summary>
/// Abstract base for playable targets.
/// Supports both file-based and URI-based playback.
/// </summary>
public abstract record PlayableTarget;

/// <summary>
/// Traditional file-based playable target (FileSystem, DLsite local files).
/// </summary>
public record FilePlayableTarget(string[] FilePaths) : PlayableTarget;

/// <summary>
/// URI-based playable target (Steam steam:// protocol, web URLs).
/// </summary>
public record UriPlayableTarget(string Uri) : PlayableTarget;
