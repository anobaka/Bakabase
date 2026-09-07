using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Network;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.ThirdParty.Abstractions.Http;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests;

/// <summary>
/// The request gate in <see cref="AbstractThirdPartyHttpMessageHandler{TOptions}"/> is a single
/// permit held for the whole of every request, on a handler that lives as long as the app. Leaking
/// it does not fail a request — it hangs every future request to that source, forever, with no
/// error anywhere. That is what made downloads freeze mid-step ("downloading torrent file") with
/// nothing but an app restart to recover.
///
/// The acquisitions used to sit outside the try/finally that releases them, so any throw in between
/// leaked. These tests pin that down: after a request fails, the next one must still get through.
/// </summary>
[TestClass]
public class ThirdPartyHttpMessageHandlerGateTests
{
    /// <summary>
    /// Long enough that a slow machine will not report a false leak, short enough that a real leak
    /// (which hangs forever) fails the test rather than the run.
    /// </summary>
    private static readonly TimeSpan GateTimeout = TimeSpan.FromSeconds(5);

    private sealed class TestOptions : IThirdPartyHttpClientOptions
    {
        public string? Cookie { get; set; }
        public string? UserAgent { get; set; }
        public string? Referer { get; set; }
        public System.Collections.Generic.Dictionary<string, string>? Headers { get; set; }
        public int MaxConcurrency { get; set; } = 1;
        public int RequestInterval { get; set; }
    }

    private sealed class TestHandler(TestOptions options, Func<Task>? beforeRequesting = null)
        : AbstractThirdPartyHttpMessageHandler<TestOptions>(
            new ThirdPartyHttpRequestLogger(NullLogger<ThirdPartyHttpRequestLogger>.Instance),
            ThirdPartyId.ExHentai,
            new BakabaseWebProxy(new StubOptions()),
            options)
    {
        protected override async Task BeforeRequestingAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (beforeRequesting != null)
            {
                await beforeRequesting();
            }

            await base.BeforeRequestingAsync(request, ct);
        }

        private sealed class StubOptions : IBOptions<NetworkOptions>
        {
            public NetworkOptions Value { get; } = new();
        }
    }

    /// <summary>
    /// Sends a request and reports how it ended, bounded so a leaked gate shows up as a timeout
    /// instead of hanging the test run.
    /// </summary>
    private static async Task<Exception?> SendAsync(HttpMessageInvoker invoker)
    {
        var send = Task.Run(async () =>
        {
            try
            {
                await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://example.invalid/"),
                    CancellationToken.None);
                return null;
            }
            catch (Exception e)
            {
                return (Exception?) e;
            }
        });

        var finished = await Task.WhenAny(send, Task.Delay(GateTimeout));

        Assert.AreSame(send, finished,
            "The request never returned: the handler's gate was leaked by an earlier failure.");

        return await send;
    }

    [TestMethod]
    public async Task RequestPreparationThrowing_DoesNotWedgeLaterRequests()
    {
        // Anything can throw here in production — most plainly a cookie or header the user pasted
        // that HttpRequestHeaders refuses to accept.
        var boom = 0;
        using var handler = new TestHandler(new TestOptions(),
            () =>
            {
                boom++;
                throw new InvalidOperationException("malformed header");
            });
        using var invoker = new HttpMessageInvoker(handler);

        var first = await SendAsync(invoker);

        Assert.IsInstanceOfType<InvalidOperationException>(first);

        // The gate must be free again. Before the fix this second call blocked forever.
        var second = await SendAsync(invoker);

        Assert.IsInstanceOfType<InvalidOperationException>(second);
        Assert.AreEqual(2, boom, "The second request never reached request preparation.");
    }

    [TestMethod]
    public async Task CancellationDuringPreparation_DoesNotWedgeLaterRequests()
    {
        var attempts = 0;
        using var handler = new TestHandler(new TestOptions(),
            () =>
            {
                attempts++;
                // Stopping a download cancels its token, and that cancellation can land anywhere —
                // including after the gate has been taken but before the request is on the wire.
                throw new OperationCanceledException();
            });
        using var invoker = new HttpMessageInvoker(handler);

        Assert.IsInstanceOfType<OperationCanceledException>(await SendAsync(invoker));
        Assert.IsInstanceOfType<OperationCanceledException>(await SendAsync(invoker));
        Assert.AreEqual(2, attempts);
    }

    [TestMethod]
    public async Task ZeroConcurrency_IsTreatedAsOne_RatherThanBlockingForever()
    {
        // A saved configuration with MaxConcurrency 0 built a semaphore nobody could ever enter,
        // so every request to that source hung with no error.
        var reached = 0;
        using var handler = new TestHandler(new TestOptions { MaxConcurrency = 0 },
            () =>
            {
                reached++;
                throw new InvalidOperationException("stop before the network");
            });
        using var invoker = new HttpMessageInvoker(handler);

        Assert.IsInstanceOfType<InvalidOperationException>(await SendAsync(invoker));
        Assert.IsInstanceOfType<InvalidOperationException>(await SendAsync(invoker));
        Assert.AreEqual(2, reached);
    }
}
