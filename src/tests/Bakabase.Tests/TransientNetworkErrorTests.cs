using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Network;

namespace Bakabase.Tests;

/// <summary>
/// The classifier decides what a download retries by itself instead of failing, so it has to be
/// right in both directions: every way a connection can drop must count, and nothing that repeating
/// cannot fix — or that repeating makes worse — may.
/// </summary>
[TestClass]
public sealed class TransientNetworkErrorTests
{
    /// <summary>The failure from the original report: an image server cut the TLS handshake short.</summary>
    private static HttpRequestException SslHandshakeCutShort() => new(HttpRequestError.SecureConnectionError,
        "The SSL connection could not be established, see inner exception.",
        new IOException("Received an unexpected EOF or 0 bytes from the transport stream."));

    [TestMethod]
    public void TheReportedSslEofIsTransient()
    {
        Assert.IsTrue(TransientNetworkError.IsTransient(SslHandshakeCutShort()));
    }

    [TestMethod]
    public void ARejectedCertificateIsNot()
    {
        // A wrong clock or an intercepting proxy fails the certificate check the same way every time.
        Assert.IsFalse(TransientNetworkError.IsTransient(new HttpRequestException(
            HttpRequestError.SecureConnectionError,
            "The SSL connection could not be established, see inner exception.",
            new AuthenticationException(
                "The remote certificate is invalid according to the validation procedure: RemoteCertificateNameMismatch"))));
    }

    [DataTestMethod]
    [DataRow(HttpRequestError.NameResolutionError)]
    [DataRow(HttpRequestError.ConnectionError)]
    [DataRow(HttpRequestError.SecureConnectionError)]
    [DataRow(HttpRequestError.ResponseEnded)]
    [DataRow(HttpRequestError.ProxyTunnelError)]
    public void ConnectionLevelRequestErrorsAreTransient(HttpRequestError error)
    {
        Assert.IsTrue(TransientNetworkError.IsTransient(new HttpRequestException(error)));
    }

    [DataTestMethod]
    [DataRow(HttpRequestError.InvalidResponse)]
    [DataRow(HttpRequestError.UserAuthenticationError)]
    [DataRow(HttpRequestError.ConfigurationLimitExceeded)]
    [DataRow(HttpRequestError.Unknown)]
    public void RequestErrorsThatRepeatingCannotFixAreNot(HttpRequestError error)
    {
        Assert.IsFalse(TransientNetworkError.IsTransient(new HttpRequestException(error)));
    }

    [TestMethod]
    public void AnUnclassifiedRequestFailureCountsWhenItWrapsTransportIo()
    {
        Assert.IsTrue(TransientNetworkError.IsTransient(
            new HttpRequestException("Error while copying content", new IOException("Connection reset"))));
        Assert.IsTrue(TransientNetworkError.IsTransient(
            new HttpRequestException("Error", new SocketException((int) SocketError.ConnectionReset))));
    }

    [DataTestMethod]
    [DataRow(HttpStatusCode.RequestTimeout)]
    [DataRow(HttpStatusCode.TooManyRequests)]
    [DataRow(HttpStatusCode.InternalServerError)]
    [DataRow(HttpStatusCode.BadGateway)]
    [DataRow(HttpStatusCode.ServiceUnavailable)]
    [DataRow(HttpStatusCode.GatewayTimeout)]
    public void ServerSaysNotRightNow(HttpStatusCode status)
    {
        Assert.IsTrue(TransientNetworkError.IsTransientStatusCode(status));
        Assert.IsTrue(TransientNetworkError.IsTransient(new HttpRequestException("status", null, status)));
    }

    [DataTestMethod]
    [DataRow(HttpStatusCode.BadRequest)]
    [DataRow(HttpStatusCode.Unauthorized)]
    [DataRow(HttpStatusCode.Forbidden)]
    [DataRow(HttpStatusCode.NotFound)]
    [DataRow(HttpStatusCode.Gone)]
    public void ServerSaysNo(HttpStatusCode status)
    {
        Assert.IsFalse(TransientNetworkError.IsTransientStatusCode(status));
        // A status overrides the wrapped cause: the server answered, so the connection was fine.
        Assert.IsFalse(TransientNetworkError.IsTransient(
            new HttpRequestException("status", new IOException("irrelevant"), status)));
    }

    [TestMethod]
    public void ABodyCutOffMidResponseIsTransient()
    {
        Assert.IsTrue(TransientNetworkError.IsTransient(new HttpIOException(HttpRequestError.ResponseEnded)));
        Assert.IsFalse(TransientNetworkError.IsTransient(new HttpIOException(HttpRequestError.InvalidResponse)));
    }

    [TestMethod]
    public void SocketFailuresAreTransientEvenWhenWrappedInAnIoException()
    {
        var reset = new SocketException((int) SocketError.ConnectionReset);
        Assert.IsTrue(TransientNetworkError.IsTransient(reset));
        Assert.IsTrue(TransientNetworkError.IsTransient(
            new IOException("Unable to read data from the transport connection", reset)));
    }

    [TestMethod]
    public void AFileSystemIoExceptionIsNot()
    {
        // Disk full, path too long, file in use: all IOExceptions, none of them network.
        Assert.IsFalse(TransientNetworkError.IsTransient(new IOException("There is not enough space on the disk.")));
        Assert.IsFalse(TransientNetworkError.IsTransient(new PathTooLongException()));
    }

    [TestMethod]
    public void AClientTimeoutIsTransientUnlessTheCallerCancelled()
    {
        var timeout = new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout",
            new TimeoutException());
        Assert.IsTrue(TransientNetworkError.IsTransient(timeout));

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.IsFalse(TransientNetworkError.IsTransient(timeout, cts.Token));
    }

    [TestMethod]
    public void TheCallersOwnCancellationIsNeverTransient()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.IsFalse(TransientNetworkError.IsTransient(new TaskCanceledException()));
        Assert.IsFalse(TransientNetworkError.IsTransient(new OperationCanceledException(cts.Token)));
        Assert.IsFalse(TransientNetworkError.IsTransient(SslHandshakeCutShort(), cts.Token),
            "Once the caller has asked to stop, nothing may be tried again.");
    }

    [TestMethod]
    public void WrappedAndAggregatedCausesAreFound()
    {
        // ExHentai gives up on a gallery by rethrowing a finished task's AggregateException.
        Assert.IsTrue(TransientNetworkError.IsTransient(
            new AggregateException(new InvalidOperationException("unrelated"), SslHandshakeCutShort())));
        Assert.IsTrue(TransientNetworkError.IsTransient(
            new InvalidOperationException("wrapper", new AggregateException(SslHandshakeCutShort()))));
        Assert.IsFalse(TransientNetworkError.IsTransient(
            new AggregateException(new InvalidOperationException("a"), new FormatException("b"))));
    }

    [TestMethod]
    public void BansAndParseFailuresAreNot()
    {
        // Retrying a ban only extends it.
        Assert.IsFalse(TransientNetworkError.IsTransient(new Exception("ExHentai banned us: Your IP address has been temporarily banned")));
        Assert.IsFalse(TransientNetworkError.IsTransient(new FormatException("Failed to parse the gallery")));
        Assert.IsFalse(TransientNetworkError.IsTransient(new InvalidOperationException("An invalid request URI was provided.")));
    }

    /// <summary>A service that answered "not right now" in-band (HTTP 200 with a risk-control code).</summary>
    private sealed class InBandNotRightNow() : Exception("busy"), ITransientServiceError;

    [TestMethod]
    public void AnInBandNotRightNowIsTransientAlsoWhenWrapped()
    {
        Assert.IsTrue(TransientNetworkError.IsTransient(new InBandNotRightNow()));
        Assert.IsTrue(TransientNetworkError.IsTransient(new InvalidOperationException("wrapper", new InBandNotRightNow())));
        Assert.IsTrue(TransientNetworkError.IsTransient(new AggregateException(new FormatException("a"), new InBandNotRightNow())));
    }

    [TestMethod]
    public void AnInBandNotRightNowIsNotTransientOnceTheCallerCancelled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.IsFalse(TransientNetworkError.IsTransient(new InBandNotRightNow(), cts.Token));
    }

    [TestMethod]
    public void BackoffDoublesWithJitterAndNeverExceedsTheCap()
    {
        var initial = TimeSpan.FromSeconds(1);
        var max = TimeSpan.FromSeconds(10);

        for (var i = 0; i < 200; i++)
        {
            AssertWithin(TransientNetworkError.GetBackoffDelay(0, initial, max), 0.8, 1.2);
            AssertWithin(TransientNetworkError.GetBackoffDelay(2, initial, max), 3.2, 4.8);
            AssertWithin(TransientNetworkError.GetBackoffDelay(10, initial, max), 8, 10);
            AssertWithin(TransientNetworkError.GetBackoffDelay(int.MaxValue, initial, max), 8, 10);
        }

        static void AssertWithin(TimeSpan actual, double minSeconds, double maxSeconds) =>
            Assert.IsTrue(actual.TotalSeconds >= minSeconds && actual.TotalSeconds <= maxSeconds,
                $"{actual.TotalSeconds}s is outside [{minSeconds}, {maxSeconds}]");
    }
}
