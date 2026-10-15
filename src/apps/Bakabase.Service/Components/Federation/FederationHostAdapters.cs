using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Modules.Federation.Contracts;
using Bakabase.Modules.Federation.Identity;
using Bakabase.Modules.Federation.Security;
using Bakabase.Modules.RemoteAccess.Abstractions.Services;

namespace Bakabase.Service.Components.Federation;

public sealed class FederationDataDirectory(AppService appService) : IFederationDataDirectory
{
    public string Path => System.IO.Path.Combine(appService.AppDataDirectory, "federation");
    public string Ensure() => appService.RequestAppDataDirectory("federation");
}

public sealed class FederationNodeIdSource(IRemoteAccessService remoteAccess) : INodeIdSource
{
    public async Task<string> GetNodeIdAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await remoteAccess.GetOrCreateServerIdAsync();
    }
}
