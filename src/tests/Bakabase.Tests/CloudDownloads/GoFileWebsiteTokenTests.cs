using System.Net;
using Bakabase.Service.Components.SourceAvailability.Probes;

namespace Bakabase.Tests.CloudDownloads;

[TestClass]
public sealed class GoFileWebsiteTokenTests
{
    private const string Signature = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Good = "function generateWT(token) { return '" + Signature + "'; }";

    [TestMethod]
    public void SignatureRunsWithoutHostObjectsAndUsesOnlyNavigatorPrimitives()
    {
        const string script = """
            function generateWT(token) {
              if (typeof System !== 'undefined' || typeof importNamespace !== 'undefined' ||
                  typeof require !== 'undefined' || typeof fetch !== 'undefined' ||
                  typeof process !== 'undefined' || typeof XMLHttpRequest !== 'undefined') throw 'host exposed';
              if (!Object.isFrozen(navigator) || typeof navigator.userAgent !== 'string' || navigator.language !== 'en-US') throw 'navigator';
              return 'a'.repeat(64);
            }
            """;
        Assert.AreEqual(Signature, GoFileWebsiteToken.Evaluate(script, "private-token", default));
    }

    [DataTestMethod]
    [DataRow("while(true) {}")]
    [DataRow("function generateWT(t) { return new Array(100001).join('a'); }")]
    [DataRow("function generateWT(t) { return eval(\"'a'.repeat(64)\"); }")]
    [DataRow("function generateWT(t) { return (function(){}).constructor(\"return 'a'.repeat(64)\")(); }")]
    [DataRow("function generateWT(t) { return {toString() { while(true){} }}; }")]
    [DataRow("function generateWT(t) { return Promise.resolve('a'.repeat(64)); }")]
    [DataRow("function generateWT(t) { return t; }")]
    [DataRow("function generateWT(t) { return generateWT(t); }")]
    public void ChangedOrUnboundedScriptsFailClosedWithoutLeakingCredential(string script)
    {
        var error = Assert.ThrowsException<GoFileProbeProtocolException>(() => GoFileWebsiteToken.Evaluate(script, "private-token", default));
        Assert.AreEqual("SOURCE_PROBE_PROTOCOL_CHANGED", error.Message);
    }

    [TestMethod]
    public void CancelledAndOversizedScriptsNeverExecute()
    {
        using var cts = new CancellationTokenSource(); cts.Cancel();
        Assert.ThrowsException<OperationCanceledException>(() => GoFileWebsiteToken.Evaluate(Good, "private", cts.Token));
        Assert.ThrowsException<GoFileProbeProtocolException>(() => GoFileWebsiteToken.Evaluate(new(' ', 65537), "private", default));
    }

    [TestMethod]
    public async Task ScriptCacheExpiresAndNeverFallsBackToExpiredGeneratorOnRefreshFailure()
    {
        var clock = new Clock(); var calls = 0;
        var factory = new Factory(request =>
        {
            Assert.AreEqual(HttpMethod.Get, request.Method);
            Assert.AreEqual("https://gofile.io/js/wt.obf.js", request.RequestUri!.AbsoluteUri);
            return ++calls == 1 ? new(HttpStatusCode.OK) {Content = new StringContent(Good)} : new(HttpStatusCode.ServiceUnavailable);
        });
        var signer = new GoFileWebsiteToken(factory, clock);
        Assert.AreEqual(Signature, await signer.GenerateAsync("credential", default));
        Assert.AreEqual(Signature, await signer.GenerateAsync("credential", default));
        Assert.AreEqual(1, calls);
        clock.Now = clock.Now.AddHours(1);
        await Assert.ThrowsExceptionAsync<GoFileProbeProtocolException>(() => signer.GenerateAsync("credential", default));
        Assert.AreEqual(2, calls);
    }

    private sealed class Clock : TimeProvider { public DateTimeOffset Now = DateTimeOffset.UtcNow; public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Factory(Func<HttpRequestMessage, HttpResponseMessage> send) : IHttpClientFactory
    { public HttpClient CreateClient(string name) => new(new Handler(send)); }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(send(request)); }
}
