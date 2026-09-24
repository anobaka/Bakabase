using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Remoting.Abstractions.Models;
using Bakabase.Remoting.Components.Connection;
using Bakabase.Remoting.Components.Forwarding;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.RemoteAccess;

/// <summary>
/// When a relay asks its server's address who answers there, and what it does with the
/// answer — the rules that keep the check off the hot path without letting a stale answer
/// stand for a server that has been replaced.
/// </summary>
/// <remarks>
/// The console's real relays, with real servers swapping addresses, are in
/// <c>Console/RelayIdentityTests</c>; this pins the timing with the clock and the answers
/// under the test's control.
/// </remarks>
[TestClass]
public class UpstreamIdentityTests
{
    private const string Desk = "server-desk";
    private const string Address = "http://127.0.0.1:47000";

    private static readonly UpstreamIdentityPolicy Policy = new(TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(3),
        TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(2));

    private RelayNavigationTokensTests.ManualClock _clock = null!;
    private MemoryStore _store = null!;
    private ScriptedVerifier _verifier = null!;
    private UpstreamIdentity _identity = null!;

    [TestInitialize]
    public void Setup()
    {
        _clock = new RelayNavigationTokensTests.ManualClock();
        _store = new MemoryStore(Desk, Address);
        _verifier = new ScriptedVerifier(_clock);
        _identity = new UpstreamIdentity(new ActiveConnection(_store), _verifier, Policy, _clock);
    }

    [TestCleanup]
    public void Cleanup() => _identity.Dispose();

    [TestMethod]
    public async Task A_confirmation_is_reused_from_memory_without_asking_again()
    {
        // The hot path: every request forwarded reads the answer; only the first asks.
        for (var i = 0; i < 50; i++)
        {
            Assert.IsTrue((await _identity.EnsureAsync())!.IsConfirmed);
            _clock.Advance(TimeSpan.FromMilliseconds(100));
        }

        Assert.AreEqual(1, _verifier.Asked);
    }

    [TestMethod]
    public async Task Requests_waiting_at_once_share_one_question()
    {
        _verifier.Hold = new TaskCompletionSource();

        var waiting = Enumerable.Range(0, 20).Select(_ => _identity.EnsureAsync().AsTask()).ToList();

        _verifier.Hold.SetResult();
        var answers = await Task.WhenAll(waiting);

        Assert.IsTrue(answers.All(a => a!.IsConfirmed));
        Assert.AreEqual(1, _verifier.Asked);
    }

    [TestMethod]
    public async Task A_page_in_use_is_rechecked_in_the_background_and_waits_only_once_the_answer_is_old()
    {
        await _identity.EnsureAsync();

        // Past half its lifetime: still used, and the next question starts meanwhile.
        _clock.Advance(TimeSpan.FromSeconds(31));
        _verifier.Hold = new TaskCompletionSource();

        var used = await _identity.EnsureAsync();

        Assert.IsTrue(used!.IsConfirmed);
        await _verifier.WaitForAsked(2);

        _verifier.Hold.SetResult();
        await WaitUntil(() => _identity.Latest!.CheckedAt == _clock.GetUtcNow());

        // Past its whole lifetime with nothing in between, a request waits for a new answer
        // — which says someone else is there now.
        _clock.Advance(TimeSpan.FromSeconds(61));
        _verifier.Answer = UpstreamIdentityVerdict.WrongServer;

        var waited = await _identity.EnsureAsync();

        Assert.AreEqual(UpstreamIdentityVerdict.WrongServer, waited!.Verdict);
        Assert.AreEqual(3, _verifier.Asked);
    }

    [TestMethod]
    public async Task A_new_connection_needs_an_answer_from_moments_ago()
    {
        await _identity.EnsureAsync();

        // Requests on connections already open go on using it.
        _clock.Advance(TimeSpan.FromSeconds(5));
        await _identity.EnsureAsync();
        Assert.AreEqual(1, _verifier.Asked);

        // A new connection is the moment the process at the other end can be another one.
        _verifier.Answer = UpstreamIdentityVerdict.WrongServer;
        var forConnection = await _identity.EnsureForConnectionAsync();

        Assert.AreEqual(UpstreamIdentityVerdict.WrongServer, forConnection!.Verdict);
        Assert.AreEqual(2, _verifier.Asked);

        // And from then on the requests see it too.
        Assert.AreEqual(UpstreamIdentityVerdict.WrongServer, (await _identity.EnsureAsync())!.Verdict);
    }

    [TestMethod]
    public async Task Connections_opened_together_share_one_question()
    {
        await _identity.EnsureAsync();
        _clock.Advance(TimeSpan.FromSeconds(5));
        _verifier.Hold = new TaskCompletionSource();

        var opening = Enumerable.Range(0, 6).Select(_ => _identity.EnsureForConnectionAsync().AsTask()).ToList();

        _verifier.Hold.SetResult();
        await Task.WhenAll(opening);

        Assert.AreEqual(2, _verifier.Asked);

        // Within the window, the next connection opens on that answer.
        _clock.Advance(TimeSpan.FromSeconds(1));
        await _identity.EnsureForConnectionAsync();
        Assert.AreEqual(2, _verifier.Asked);
    }

    [TestMethod]
    public async Task Someone_else_at_the_address_is_asked_again_only_after_the_retry_interval()
    {
        _verifier.Answer = UpstreamIdentityVerdict.WrongServer;
        Assert.AreEqual(UpstreamIdentityVerdict.WrongServer, (await _identity.EnsureAsync())!.Verdict);

        // A page reloaded meanwhile does not ask on every request.
        _clock.Advance(TimeSpan.FromSeconds(2));
        Assert.AreEqual(UpstreamIdentityVerdict.WrongServer, (await _identity.EnsureAsync())!.Verdict);
        Assert.AreEqual(1, _verifier.Asked);

        // The server back at its address is noticed on the next question.
        _clock.Advance(TimeSpan.FromSeconds(2));
        _verifier.Answer = UpstreamIdentityVerdict.Confirmed;

        Assert.IsTrue((await _identity.EnsureAsync())!.IsConfirmed);
        Assert.AreEqual(2, _verifier.Asked);
    }

    [TestMethod]
    public async Task A_server_seen_failing_is_asked_again_before_the_next_request()
    {
        await _identity.EnsureAsync();

        // Gone mid-exchange: the fresh confirmation no longer stands.
        _identity.Suspect();
        _verifier.Answer = UpstreamIdentityVerdict.WrongServer;

        Assert.AreEqual(UpstreamIdentityVerdict.WrongServer, (await _identity.EnsureAsync())!.Verdict);
        Assert.AreEqual(2, _verifier.Asked);
    }

    [TestMethod]
    public async Task A_question_asked_before_the_failure_does_not_answer_for_after_it()
    {
        await _identity.EnsureAsync();
        _clock.Advance(TimeSpan.FromMinutes(2));

        // A question goes out, then the server is seen failing before it is answered.
        _verifier.Hold = new TaskCompletionSource();
        var early = _identity.EnsureAsync().AsTask();
        await _verifier.WaitForAsked(2);

        _clock.Advance(TimeSpan.FromMilliseconds(10));
        _identity.Suspect();

        var late = _identity.EnsureAsync().AsTask();
        await _verifier.WaitForAsked(3);

        _verifier.Hold.SetResult();
        await Task.WhenAll(early, late);

        // The late request did not settle for the question that predates the failure.
        Assert.AreEqual(3, _verifier.Asked);
    }

    [TestMethod]
    public async Task An_answer_about_the_old_address_never_stands_for_a_new_one()
    {
        await _identity.EnsureAsync();

        _store.SetAddress("http://127.0.0.1:47001");

        Assert.IsNull(_identity.Latest, "an answer about another address was reported as current");

        var check = await _identity.EnsureAsync();

        Assert.AreEqual("http://127.0.0.1:47001", check!.Address);
        Assert.AreEqual(2, _verifier.Asked);
        CollectionAssert.AreEqual(new[] {Address, "http://127.0.0.1:47001"}, _verifier.Addresses.ToArray());
    }

    [TestMethod]
    public async Task A_question_that_fails_or_runs_out_of_time_identifies_nobody()
    {
        _verifier.Throw = new InvalidOperationException("boom");
        Assert.AreEqual(UpstreamIdentityVerdict.Unconfirmed, (await _identity.EnsureAsync())!.Verdict);

        _clock.Advance(TimeSpan.FromSeconds(4));
        _verifier.Throw = null;
        _verifier.Hang = true;

        using var identity = new UpstreamIdentity(new ActiveConnection(_store), _verifier,
            Policy with {Timeout = TimeSpan.FromMilliseconds(100)}, _clock);

        var check = await identity.EnsureAsync();

        Assert.AreEqual(UpstreamIdentityVerdict.Unconfirmed, check!.Verdict);
        StringAssert.Contains(check.Describe("Desk"), "Desk is not answering at 127.0.0.1:47000");
    }

    [TestMethod]
    public async Task An_older_answer_heard_elsewhere_does_not_replace_a_newer_one()
    {
        var asked = await _identity.EnsureAsync();

        _identity.Record(asked! with
        {
            Verdict = UpstreamIdentityVerdict.WrongServer,
            CheckedAt = asked.CheckedAt - TimeSpan.FromSeconds(1)
        });
        Assert.IsTrue(_identity.Latest!.IsConfirmed);

        // A newer one does, at once: a probe that found someone else stops the relay.
        _identity.Record(asked with {Verdict = UpstreamIdentityVerdict.WrongServer, AnsweredById = "server-nas"});
        Assert.AreEqual(UpstreamIdentityVerdict.WrongServer, (await _identity.EnsureAsync())!.Verdict);
        Assert.AreEqual(1, _verifier.Asked);
    }

    [TestMethod]
    public async Task No_server_is_no_answer()
    {
        _store.Forget();

        Assert.IsNull(await _identity.EnsureAsync());
        Assert.IsNull(await _identity.EnsureForConnectionAsync());
        Assert.AreEqual(0, _verifier.Asked);
    }

    [TestMethod]
    public async Task The_connect_step_opens_nothing_for_someone_else_or_for_another_address()
    {
        _verifier.Answer = UpstreamIdentityVerdict.WrongServer;

        var refused = await Assert.ThrowsExceptionAsync<UpstreamIdentityRefusedException>(() =>
            UpstreamConnections.ConnectAsync(_identity, new DnsEndPoint("127.0.0.1", 47000), default).AsTask());
        Assert.AreEqual(UpstreamIdentityVerdict.WrongServer, refused.Check!.Verdict);

        // Confirmed, but for 47000: a dial to anywhere else is not covered by it.
        _clock.Advance(TimeSpan.FromSeconds(4));
        _verifier.Answer = UpstreamIdentityVerdict.Confirmed;

        await Assert.ThrowsExceptionAsync<UpstreamIdentityRefusedException>(() =>
            UpstreamConnections.ConnectAsync(_identity, new DnsEndPoint("127.0.0.1", 47001), default).AsTask());
    }

    [TestMethod]
    [DataRow(UpstreamIdentityVerdict.WrongServer, "127.0.0.1:47000 now answers as another server (NAS), not Desk")]
    [DataRow(UpstreamIdentityVerdict.ThisDevice, "127.0.0.1:47000 now reaches this computer itself, not Desk")]
    [DataRow(UpstreamIdentityVerdict.Unconfirmed, "Desk is not answering at 127.0.0.1:47000 (nothing answers there)")]
    public void What_the_user_is_told_names_the_address_and_who_answers(UpstreamIdentityVerdict verdict,
        string expected)
    {
        var check = new UpstreamIdentityCheck(Desk, Address, verdict, "server-nas", "NAS",
            verdict == UpstreamIdentityVerdict.Unconfirmed ? "nothing answers there" : null, DateTimeOffset.UtcNow);
        var message = check.Describe("Desk");

        StringAssert.StartsWith(message, expected);
        Assert.IsFalse(message.Contains("client", StringComparison.OrdinalIgnoreCase), message);
    }

    [TestMethod]
    public void A_server_with_remote_access_off_is_to_be_switched_on_not_looked_for()
    {
        var check = new UpstreamIdentityCheck(Desk, Address, UpstreamIdentityVerdict.Unconfirmed, null, null,
            "remote access is turned off there", DateTimeOffset.UtcNow) {RemoteAccessDisabled = true};
        var message = check.Describe("Desk");

        StringAssert.StartsWith(message, "Remote access is turned off at 127.0.0.1:47000");
        StringAssert.Contains(message, "under “Let other devices manage this device”");
        Assert.IsFalse(message.Contains("running", StringComparison.OrdinalIgnoreCase), message);

        // Carried with the answer when the relay stamps it as its own.
        Assert.IsTrue((check with {CheckedAt = DateTimeOffset.UnixEpoch}).RemoteAccessDisabled);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(10);
        }

        Assert.IsTrue(condition());
    }

    /// <summary>Answers as told, counting what it was asked.</summary>
    private sealed class ScriptedVerifier(TimeProvider clock) : IUpstreamIdentityVerifier
    {
        private int _asked;

        public UpstreamIdentityVerdict Answer { get; set; } = UpstreamIdentityVerdict.Confirmed;
        public TaskCompletionSource? Hold { get; set; }
        public Exception? Throw { get; set; }
        public bool Hang { get; set; }
        public ConcurrentQueue<string> Addresses { get; } = new();

        public int Asked => Volatile.Read(ref _asked);

        public async Task<UpstreamIdentityCheck> VerifyAsync(string serverId, string address, CancellationToken ct)
        {
            Interlocked.Increment(ref _asked);
            Addresses.Enqueue(address);

            // Read before waiting: what the answer is was decided when the question went out.
            var answer = Answer;

            if (Hold is { } hold)
            {
                await hold.Task.WaitAsync(ct);
            }

            if (Hang)
            {
                await Task.Delay(Timeout.Infinite, ct);
            }

            if (Throw is { } e)
            {
                throw e;
            }

            return new UpstreamIdentityCheck(serverId, address, answer,
                answer == UpstreamIdentityVerdict.Confirmed ? serverId : "server-nas", "NAS", null,
                clock.GetUtcNow());
        }

        public async Task WaitForAsked(int count)
        {
            for (var i = 0; i < 200 && Asked < count; i++)
            {
                await Task.Delay(10);
            }

            Assert.AreEqual(count, Asked);
        }
    }

    /// <summary>One managed server, as a relay's store view shows it.</summary>
    private sealed class MemoryStore : IClientConnectionStore
    {
        private ClientConnectionData _data;

        public MemoryStore(string serverId, string address)
        {
            _data = Snapshot(new ClientServerConnection
            {
                ServerId = serverId,
                ServerName = "Desk",
                BaseAddress = address,
                DeviceId = "device",
                DeviceKey = "key"
            });
        }

        public ClientConnectionData Read() => _data;

        public Task<T> MutateAsync<T>(Func<ClientConnectionData, T> mutate, CancellationToken ct = default) =>
            Task.FromResult(mutate(_data));

        public Task MutateAsync(Action<ClientConnectionData> mutate, CancellationToken ct = default)
        {
            mutate(_data);
            return Task.CompletedTask;
        }

        public void SetAddress(string address)
        {
            var server = _data.Servers.Single();

            _data = Snapshot(new ClientServerConnection
            {
                ServerId = server.ServerId,
                ServerName = server.ServerName,
                BaseAddress = address,
                DeviceId = server.DeviceId,
                DeviceKey = server.DeviceKey
            });
        }

        public void Forget() => _data = new ClientConnectionData();

        private static ClientConnectionData Snapshot(ClientServerConnection server) => new()
        {
            Servers = new List<ClientServerConnection> {server},
            ActiveServerId = server.ServerId
        };
    }
}
