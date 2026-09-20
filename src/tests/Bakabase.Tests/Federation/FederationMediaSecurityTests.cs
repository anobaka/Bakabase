using Bakabase.Modules.Federation.Contracts;
using Bakabase.Modules.Federation.Media;
using Bakabase.Service.Components.Federation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.Federation;

[TestClass]
public sealed class FederationMediaSecurityTests
{
    private static readonly ResourceRef Reference = new("source", "epoch", 1);

    [TestMethod]
    public void MaliciousPeerCannotChooseHtmlMimeOnTheLocalApplicationOrigin()
    {
        var sessions = new FederationMediaSessions();
        var asset = new FederatedAsset(new string('a', 64), "image", "cover.jpg", "text/html",
            null, null, DateTimeOffset.UtcNow.AddMinutes(5));
        sessions.Remember(Detail(asset), null);
        var trusted = sessions.GetAsset(new AssetRef(Reference, asset.AssetId));
        Assert.AreEqual("image/jpeg", trusted.Asset.ContentType);
        var activeDocument = asset with { AssetId = new string('b', 64), FileName = "cover.svg" };
        Assert.ThrowsException<FederationQueryException>(() => sessions.Remember(Detail(activeDocument), null));
        Assert.ThrowsException<FederationQueryException>(() => sessions.Remember(Detail(asset with { Kind = "video" }), null));
        Assert.ThrowsException<FederationQueryException>(() => sessions.Remember(Detail(asset with
            { SourceRootId = "root", RelativePath = new string('x', 4097) }), null));
        Assert.ThrowsException<FederationQueryException>(() => sessions.Remember(Detail(asset with
            { FileName = null! }), null));
    }

    private static FederatedResourceDetail Detail(FederatedAsset asset) => new(Reference, "Source", "Resource",
        "resource.jpg", "HasFile", [], [], [], [], [asset], null);

    [TestMethod]
    public async Task IdleMediaReadIsCancelledButAnActiveStreamCanExceedOneIdleWindow()
    {
        using var output = new MemoryStream();
        await Assert.ThrowsExceptionAsync<TaskCanceledException>(() => FederationMediaStream.CopyAsync(
            new DelayedStream(Timeout.InfiniteTimeSpan, 1), output, TimeSpan.FromMilliseconds(30), CancellationToken.None));
        using var active = new DelayedStream(TimeSpan.FromMilliseconds(50), 10);
        await FederationMediaStream.CopyAsync(active, output, TimeSpan.FromMilliseconds(300), CancellationToken.None);
        Assert.AreEqual(10L, output.Length);
    }

    private sealed class DelayedStream(TimeSpan delay, int count) : MemoryStream
    {
        private int _remaining = count;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_remaining-- <= 0) return 0;
            await Task.Delay(delay, cancellationToken);
            buffer.Span[0] = 42;
            return 1;
        }
    }
}
