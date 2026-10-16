using System.Net;
using System.Security.Cryptography;
using System.Text;
using Bakabase.Modules.Federation.Identity;
using Bakabase.Modules.Federation.Security;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Modules.RemoteAccess.Components.Pairing;
using Bakabase.Service.Components.Federation;
using Bakabase.Service.Components.Federation.MetadataReplicas;
using Bakabase.Service.Controllers;
using Bakabase.Tests.RemoteAccess.Service;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;

namespace Bakabase.Tests;

public sealed partial class ResourceSearchIndexIncrementalTests
{
    [TestMethod]
    public async Task MetadataEndpoint_PairedSignatureRequired_AndEpochGuardRejectsRestoredLibrary()
    {
        await Seed("Signed source resource");
        await BuildIndex();
        var feed = Feed();
        await using var host = await ServiceGateHost.StartAsync([typeof(ManagedMetadataController)], services =>
        {
            services.AddSingleton(feed);
            services.AddSingleton<INodeIdentityProvider>(new MetadataTestIdentity());
            services.Configure<MvcOptions>(options => options.Filters.Add<ManagedLibraryEpochFilter>());
        });
        host.Remote.RequirePairing = true;
        var deviceService = host.Services.GetRequiredService<IRemoteDeviceService>();
        var code = await deviceService.IssuePairingCodeAsync();
        var credentials = (await deviceService.PairWithCodeAsync(code.Code, "Manager", RemoteDevicePlatform.Windows)).Credentials!;
        async Task<HttpResponseMessage> Send(HttpMethod method, string path, string? body = null, bool signed = true)
        {
            using var request = host.Request(method, path, json: body);
            request.Headers.Add(ServiceGateHost.RemoteIpHeader, "192.0.2.51");
            if (signed)
            {
                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var nonce = RemoteRequestSignature.NewNonce();
                var uri = request.RequestUri!;
                var digest = body == null ? string.Empty : RemoteRequestSignature.ToBase64Url(SHA256.HashData(Encoding.UTF8.GetBytes(body)));
                var canonical = RemoteRequestSignature.BuildCanonicalString(credentials.DeviceId, method.Method,
                    uri.AbsolutePath, uri.Query.TrimStart('?'), timestamp, nonce, digest);
                request.Headers.Add("Authorization", RemoteRequestSignature.BuildHeader(credentials.DeviceId, timestamp, nonce,
                    RemoteRequestSignature.Sign(RemoteRequestSignature.FromBase64Url(credentials.Key), canonical)));
            }
            return await host.SendAsync(request);
        }
        using var unsigned = await Send(HttpMethod.Get, "/management/library/metadata/head", signed: false);
        Assert.IsFalse(unsigned.IsSuccessStatusCode);
        using var headResponse = await Send(HttpMethod.Get, "/management/library/metadata/head");
        Assert.AreEqual(HttpStatusCode.OK, headResponse.StatusCode, await headResponse.Content.ReadAsStringAsync());
        var manifest = JsonConvert.DeserializeObject<MetadataSourceManifest>(await headResponse.Content.ReadAsStringAsync())!;
        var requestBody = JsonConvert.SerializeObject(new MetadataPageRequest(manifest.Start, 1));
        using var page = await Send(HttpMethod.Post, "/management/library/metadata/page?managedLibraryEpoch=source-epoch", requestBody);
        Assert.AreEqual(HttpStatusCode.OK, page.StatusCode, await page.Content.ReadAsStringAsync());
        var batch = JsonConvert.DeserializeObject<ReplicaBatch>(await page.Content.ReadAsStringAsync())!;
        Assert.AreEqual(manifest.Start, batch.Expected);
        var checkpoint = batch.Next;
        var resourceIds = batch.Upserts.Select(resource => resource.ResourceId).ToHashSet();
        for (var remaining = 300; !checkpoint.Complete && remaining > 0; remaining--)
        {
            using var continuation = await Send(HttpMethod.Post,
                "/management/library/metadata/page?managedLibraryEpoch=source-epoch",
                JsonConvert.SerializeObject(new MetadataPageRequest(checkpoint, 1)));
            var body = await continuation.Content.ReadAsStringAsync();
            Assert.AreEqual(HttpStatusCode.OK, continuation.StatusCode, body);
            batch = JsonConvert.DeserializeObject<ReplicaBatch>(body)!;
            Assert.AreEqual(checkpoint, batch.Expected, "A successful HTTP status must contain the requested metadata page.");
            resourceIds.UnionWith(batch.Upserts.Select(resource => resource.ResourceId));
            checkpoint = batch.Next;
        }
        Assert.IsTrue(checkpoint.Complete, "The signed source must export resources after the catalog pages.");
        CollectionAssert.AreEquivalent(new[] {await ResourceId("Signed source resource")}, resourceIds.ToArray());
        using var wrongEpoch = await Send(HttpMethod.Post, "/management/library/metadata/page?managedLibraryEpoch=replaced-epoch", requestBody);
        Assert.AreEqual(HttpStatusCode.Conflict, wrongEpoch.StatusCode);
        await deviceService.RevokeAsync(credentials.DeviceId);
        using var revoked = await Send(HttpMethod.Get, "/management/library/metadata/head");
        Assert.IsFalse(revoked.IsSuccessStatusCode);
    }

    [TestMethod]
    public void MetadataControl_HasExactLocalManagementRouteAndDoesNotExpandReadonlyExport()
    {
        const string path = "/federation/local/management/source-device/metadata/control";
        Assert.IsTrue(FederationRoutePolicy.Allows(FederationEndpointKind.Local, "POST", path));
        foreach (var method in new[] {"GET", "PUT", "PATCH", "DELETE"})
            Assert.IsFalse(FederationRoutePolicy.Allows(FederationEndpointKind.Local, method, path));
        Assert.IsFalse(FederationRoutePolicy.Allows(FederationEndpointKind.Local, "POST", path + "/extra"));
        Assert.IsFalse(FederationRoutePolicy.Allows(FederationEndpointKind.Local, "POST", "/federation/local/management/../metadata/control"));
        Assert.IsFalse(FederationRoutePolicy.Allows(FederationEndpointKind.Export, "POST", "/federation/v1/export/metadata/control"));
    }
}
