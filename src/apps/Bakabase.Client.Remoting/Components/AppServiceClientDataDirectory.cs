using Bakabase.Remoting.Abstractions;
using Bakabase.Infrastructures.Components.App;

namespace Bakabase.Client.Remoting.Components;

/// <summary>
/// Puts the client's own state under its application data directory.
/// </summary>
/// <remarks>
/// The split between <see cref="Path"/> and <see cref="Ensure"/> exists because
/// <see cref="AppService.RequestAppDataDirectory"/> creates what it resolves. Reads have
/// to resolve without that, or a client that has never paired would grow an empty folder
/// simply by starting up.
/// </remarks>
public sealed class AppServiceClientDataDirectory(AppService appService) : IClientDataDirectory
{
    public const string DirectoryName = "client";

    /// <summary>
    /// The client's directory under an already-resolved application data root.
    /// </summary>
    /// <remarks>
    /// Static so the one caller that runs before DI — the host choosing its loopback port —
    /// lands in the same place as everything that comes after it, without repeating the
    /// subdirectory name.
    /// </remarks>
    public static string Resolve(string appDataDirectory) =>
        System.IO.Path.Combine(appDataDirectory, DirectoryName);

    public string Path => Resolve(appService.AppDataDirectory);

    public string Ensure() => appService.RequestAppDataDirectory(DirectoryName);
}
