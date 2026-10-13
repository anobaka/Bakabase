using Bakabase.Modules.Federation.Security;
using Bakabase.Modules.Federation.Peers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.Federation.Security;

public sealed partial class FederationGateTests
{
    [TestMethod]
    public async Task MappingTreeRequiresNodeAuthenticationAndCannotBecomeAnAdministrativeNodeRoute()
    {
        using var gate = await GateFixture.CreateAsync();
        var export = Context("/federation/v1/export/mapping-tree", "GET");
        await gate.RunAsync(export);
        Assert.AreEqual("NodeAuthenticationRequired", Error(export));
        export = Context("/federation/v1/export/mapping-tree", "GET");
        gate.Sign(export);
        await gate.RunAsync(export);
        Assert.IsTrue(gate.ReachedEndpoint);
        var local = Context("/federation/local/peers/peer/mapping-tree", "GET");
        gate.Sign(local);
        await gate.RunAsync(local);
        Assert.AreEqual("NodeRouteForbidden", Error(local));
        Assert.IsFalse(gate.ReachedEndpoint);
    }

    [TestMethod]
    [DataRow(FederationEndpointKind.Export,"GET","/federation/v1/export/mapping-tree")]
    [DataRow(FederationEndpointKind.Local,"GET","/federation/local/peers/peer/mapping-tree")]
    public void MappingTreeRoutesAreExact(FederationEndpointKind kind,string method,string path)
    {
        foreach (var other in Enum.GetValues<FederationEndpointKind>())
            Assert.AreEqual(other==kind,FederationRoutePolicy.Allows(other,method,path));
        Assert.IsFalse(FederationRoutePolicy.Allows(kind,"POST",path));
        Assert.IsFalse(FederationRoutePolicy.Allows(kind,"GET",path+"/extra"));
        Assert.IsFalse(FederationRoutePolicy.Allows(kind,"GET",path+"-admin"));
        Assert.AreEqual(FederationSharingRequirement.Library,
            FederationRoutePolicy.RequiredSharing(FederationEndpointKind.Export,"GET","/federation/v1/export/mapping-tree"));
        Assert.IsFalse(FederationRoutePolicy.ScopeMatches(FederationSharingRequirement.Library,FederationScopes.DataSyncRead));
    }
}
