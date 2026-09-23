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
    public void TicketOutlivesTheSourceLeaseWhileInUseAndAdoptsOnlyANewLeaseForTheSameFile()
    {
        var clock = new Clock();
        var sessions = new FederationMediaSessions(clock);
        var film = new FederatedAsset(new string('a', 64), "video", "film.mp4", "video/mp4",
            "root", "films/film.mp4", clock.Now.AddMinutes(10));
        sessions.Remember(Detail(film), null);
        var ticket = sessions.Issue(sessions.GetAsset(new AssetRef(Reference, film.AssetId)), null);

        // A paused player seeks after the source's ten-minute lease: the ticket itself is still live.
        clock.Now = clock.Now.AddMinutes(30);
        Assert.AreSame(ticket, sessions.GetTicket(ticket.Id));

        var renewed = film with { AssetId = new string('b', 64), ExpiresAt = clock.Now.AddMinutes(10) };
        var other = film with { AssetId = new string('c', 64), FileName = "other.mp4",
            RelativePath = "films/other.mp4", ExpiresAt = clock.Now.AddMinutes(10) };
        sessions.Remember(Detail(renewed), null);
        sessions.Remember(Detail(other), null);
        Assert.AreEqual("AssetGone", Assert.ThrowsExactly<FederationQueryException>(() =>
            sessions.Renew(ticket, sessions.GetAsset(new AssetRef(Reference, other.AssetId)))).Code);
        sessions.Renew(ticket, sessions.GetAsset(new AssetRef(Reference, renewed.AssetId)));
        Assert.AreEqual(renewed.AssetId, sessions.GetTicket(ticket.Id).Source.Ref.AssetId);

        // Idle tickets still expire, and so does any ticket at its absolute cap.
        clock.Now = clock.Now + FederationMediaSessions.TicketIdleLifetime;
        Assert.AreEqual("AssetExpired",
            Assert.ThrowsExactly<FederationQueryException>(() => sessions.GetTicket(ticket.Id)).Code);
        sessions.Remember(Detail(film with { ExpiresAt = clock.Now.AddMinutes(10) }), null);
        var busy = sessions.Issue(sessions.GetAsset(new AssetRef(Reference, film.AssetId)), null);
        for (var elapsed = TimeSpan.Zero; elapsed < FederationMediaSessions.TicketMaxLifetime;
             elapsed += TimeSpan.FromHours(1))
        {
            clock.Now = clock.Now.AddHours(1);
            if (elapsed + TimeSpan.FromHours(1) < FederationMediaSessions.TicketMaxLifetime)
                sessions.GetTicket(busy.Id);
        }
        Assert.AreEqual("AssetExpired",
            Assert.ThrowsExactly<FederationQueryException>(() => sessions.GetTicket(busy.Id)).Code);
    }

    [TestMethod]
    public void MacPackagesAreRevealedNeverOpened()
    {
        var root = Path.Combine(Path.GetTempPath(), "federation-open-" + Guid.NewGuid().ToString("N"));
        try
        {
            var plain = Directory.CreateDirectory(Path.Combine(root, "Movies Vol. 1.5")).FullName;
            var named = Directory.CreateDirectory(Path.Combine(root, "Trailer.APP")).FullName;
            var disguised = Directory.CreateDirectory(Path.Combine(root, "Trailer", "Contents")).Parent!.FullName;
            File.WriteAllText(Path.Combine(disguised, "Contents", "Info.plist"), "<plist/>");
            Assert.IsFalse(FederationDirectoryOpener.IsMacPackage(plain));
            Assert.IsTrue(FederationDirectoryOpener.IsMacPackage(named + Path.DirectorySeparatorChar));
            Assert.IsTrue(FederationDirectoryOpener.IsMacPackage(disguised));
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

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
