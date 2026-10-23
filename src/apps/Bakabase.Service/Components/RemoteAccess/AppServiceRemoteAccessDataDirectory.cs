using System.IO;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Modules.RemoteAccess.Abstractions.Components;

namespace Bakabase.Service.Components.RemoteAccess;

/// <summary>
/// Puts remote-access state under the application's data directory.
/// </summary>
/// <remarks>
/// The split between <see cref="Path"/> and <see cref="Ensure"/> exists because
/// <see cref="AppService.RequestAppDataDirectory"/> creates what it resolves. Reads
/// have to resolve without that, or the all-in-one — which ships with remote access
/// off — would grow an empty <c>remote-access</c> folder simply by starting up.
/// </remarks>
public sealed class AppServiceRemoteAccessDataDirectory(AppService appService) : IRemoteAccessDataDirectory
{
    public const string DirectoryName = "remote-access";

    public string Path => AppDataStorage.RemoteAccess.GetPath(appService.AppDataDirectory);

    public string Ensure() => appService.RequestAppDataDirectory(AppDataStorage.RemoteAccess.RelativePath);
}
