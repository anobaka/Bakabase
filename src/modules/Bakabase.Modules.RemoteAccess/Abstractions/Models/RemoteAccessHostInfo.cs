namespace Bakabase.Modules.RemoteAccess.Abstractions.Models;

/// <summary>
/// Facts about the hosting application that this module cannot know by itself.
/// Supplied by the application layer at registration, like
/// <see cref="RemoteAccessDefaults"/>.
/// </summary>
/// <param name="AppVersion">The application's own version string.</param>
public record RemoteAccessHostInfo(string AppVersion);
