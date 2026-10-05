using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.Modules.ThirdParty.ThirdParties.SoulPlus;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests;

[TestClass]
public class SoulPlusRequestGateTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [TestMethod]
    public async Task SiteConcurrencyAppliesToBothPostReadsAndPurchasesAcrossClients()
    {
        var options = new Options(new SoulPlusOptions {Cookie = "fake", MaxConcurrency = 1, RequestInterval = 0});
        var gate = new SoulPlusRequestGate(options);
        var release = Signal();
        var readStarted = Signal();
        var purchaseStarted = Signal();
        var reader = new Client(options, gate, async () => { readStarted.SetResult(); await release.Task; });
        var buyer = new Client(options, gate, () => { purchaseStarted.SetResult(); return Task.CompletedTask; });
        var read = reader.ReadAsync("https://www.north-plus.net/read.php?tid=1");
        await readStarted.Task.WaitAsync(Timeout);
        var purchase = buyer.BuyLockedContent("https://www.north-plus.net/job.php?action=buy&pid=1", default);
        Assert.IsFalse(purchaseStarted.Task.IsCompleted);
        release.SetResult();
        await Task.WhenAll(read, purchase).WaitAsync(Timeout);
        Assert.IsTrue(purchaseStarted.Task.IsCompleted);
    }

    [TestMethod]
    public async Task CancellingSentRequestKeepsSlotUntilTransportActuallyFinishes()
    {
        var options = new Options(new SoulPlusOptions {MaxConcurrency = 1, RequestInterval = 0});
        var gate = new SoulPlusRequestGate(options);
        var response = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        var first = gate.ExecuteAsync(() => response.Task, cancellation.Token);
        cancellation.Cancel();
        var nextStarted = false;
        var next = gate.ExecuteAsync(() => { nextStarted = true; return Task.FromResult("next"); });
        await Task.Delay(350);
        Assert.IsFalse(first.IsCompleted);
        Assert.IsFalse(nextStarted);
        response.SetResult("sent");
        await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => first);
        Assert.AreEqual("next", await next.WaitAsync(Timeout));
    }

    [TestMethod]
    public async Task CancellationWhileQueuedNeverSendsAndRequestFailureReleasesCapacity()
    {
        var gate = new SoulPlusRequestGate(new Options(new SoulPlusOptions {RequestInterval = 0}));
        var release = Signal();
        var first = gate.ExecuteAsync<int>(async () => { await release.Task; throw new InvalidOperationException("network"); });
        using var cancellation = new CancellationTokenSource();
        var sent = false;
        var second = gate.ExecuteAsync(() => { sent = true; return Task.FromResult(2); }, cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsExceptionAsync<TaskCanceledException>(() => second);
        Assert.IsFalse(sent);
        release.SetResult();
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => first);
        Assert.AreEqual(3, await gate.ExecuteAsync(() => Task.FromResult(3)).WaitAsync(Timeout));
    }

    [TestMethod]
    public async Task PacingSeparatesStartsWithoutWaitingForPreviousResponse()
    {
        var gate = new SoulPlusRequestGate(new Options(new SoulPlusOptions {MaxConcurrency = 2, RequestInterval = 150}));
        var release = Signal();
        var clock = Stopwatch.StartNew();
        var firstStarted = TimeSpan.Zero;
        var secondStarted = TimeSpan.Zero;
        var first = gate.ExecuteAsync(async () => { firstStarted = clock.Elapsed; await release.Task; return 1; });
        var second = gate.ExecuteAsync(() => { secondStarted = clock.Elapsed; return Task.FromResult(2); });
        await second.WaitAsync(Timeout);
        Assert.IsFalse(first.IsCompleted, "Pacing must not serialize response completion.");
        Assert.IsTrue(secondStarted - firstStarted >= TimeSpan.FromMilliseconds(120));
        release.SetResult();
        await first.WaitAsync(Timeout);
    }

    [TestMethod]
    public async Task LoweringIntervalUnblocksWaitingRequestAndCancellationDuringPacingDoesNotSend()
    {
        var options = new Options(new SoulPlusOptions {MaxConcurrency = 2, RequestInterval = 60000});
        var gate = new SoulPlusRequestGate(options);
        await gate.ExecuteAsync(() => Task.FromResult(1));
        using var cancellation = new CancellationTokenSource();
        var sent = false;
        var cancelled = gate.ExecuteAsync(() => { sent = true; return Task.FromResult(2); }, cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsExceptionAsync<TaskCanceledException>(() => cancelled);
        Assert.IsFalse(sent);
        var next = gate.ExecuteAsync(() => Task.FromResult(3));
        options.Value.RequestInterval = 0;
        Assert.AreEqual(3, await next.WaitAsync(Timeout));
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class Options(SoulPlusOptions value) : IBOptions<ISoulPlusOptions>
    {
        public SoulPlusOptions Value { get; } = value;
        ISoulPlusOptions Microsoft.Extensions.Options.IOptions<ISoulPlusOptions>.Value => Value;
    }

    private sealed class Factory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private sealed class Client(Options options, SoulPlusRequestGate gate, Func<Task> onSend)
        : SoulPlusClient(new Factory(), NullLoggerFactory.Instance, options, gate)
    {
        public Task<string> ReadAsync(string url) => GetHtml(url, default);
        protected override async Task<(int StatusCode, string Text)> SendAsync(string url,
            Dictionary<string, string> headers, string preset)
        {
            await onSend();
            return (200, "readable");
        }
    }
}
