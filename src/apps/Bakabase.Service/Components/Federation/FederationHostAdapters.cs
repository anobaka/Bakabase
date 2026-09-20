using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Modules.Federation.Contracts;
using Bakabase.Modules.Federation.Identity;
using Bakabase.Modules.Federation.Queries;
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

public sealed class FederationQueryAccess(INodeIdentityProvider identity, INodeGrantService grants,
    IRemoteAccessService remoteAccess, GrantLeaseRegistry leases) : IFederationQueryAccess
{
    // Never accepted from the wire: export controllers take their grant only from the authenticated principal.
    public const string LocalGrantId = "local-library";

    public async Task<QueryAccess> ValidateAsync(string grantId, string expectedLibraryEpoch,
        CancellationToken cancellationToken)
    {
        var node = await identity.GetAsync(cancellationToken);
        if (node.LibraryEpoch != expectedLibraryEpoch)
            throw new FederationQueryException("LibraryEpochChanged", 409);
        if (grantId == LocalGrantId)
            return new QueryAccess(node.NodeId, node.LibraryEpoch, grantId, 0);
        if (remoteAccess.GetEffectiveMode() == RemoteAccessMode.Disabled)
            throw new FederationQueryException("SharingDisabled", 403);
        var principal = await grants.ValidateAsync(grantId, expectedLibraryEpoch, cancellationToken);
        return new QueryAccess(node.NodeId, node.LibraryEpoch, grantId, principal.Revision);
    }

    public CancellationToken GetCancellationToken(string grantId) => grantId == LocalGrantId
        ? CancellationToken.None : leases.GetCancellationToken(grantId);
}
