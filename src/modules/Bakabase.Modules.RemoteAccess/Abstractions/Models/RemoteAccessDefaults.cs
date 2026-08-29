using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.RemoteAccess.Abstractions.Models;

/// <summary>
/// The mode that applies when the user has not chosen one. Supplied by the
/// application layer, which knows the runtime mode — Docker keeps the wide-open
/// behavior containerized installs have always had, everything else starts closed.
/// </summary>
/// <param name="Mode">Mode to fall back to.</param>
public record RemoteAccessDefaults(RemoteAccessMode Mode);
