using System.Collections;
using System.Reflection;
using System.Text.Json.Nodes;
using Bakabase.InsideWorld.Business.Components.DataSync.Runtime;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Runtime;
using Bakabase.Modules.DataSync.Services;
using Bakabase.Modules.Federation.Identity;
using Bakabase.Modules.RemoteAccess.Abstractions.Components;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Modules.RemoteAccess.Components.Pairing;
using Bakabase.Remoting.Abstractions;
using Bakabase.Remoting.Abstractions.Models;
using Bakabase.Remoting.Components.Connection;
using Bakabase.Service.Controllers;
using Bakabase.TestKit.DataSync;
using Bakabase.Tests.DataSync.Api;
using Bakabase.Tests.Federation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.DataSync;

/// <summary>
/// §12 (product must-fix 15), the part packages D and E answer for: three devices on real loopback listeners with the
/// Service's gates and federation's real pairing (<see cref="DataSyncNodeHost"/>), each with the real <c>/data-sync</c>
/// facade, runtime and notifier over it (<see cref="DataSyncApiHarness"/>, package C faked). The marker is seeded into
/// every secret a device keeps: the federation <c>state.json</c> (library and datasync grant keys, invitation and
/// reciprocal codes, claim secrets, reciprocal offers), <c>remote-access/devices.json</c> (device keys, an approved
/// request's key, the pairing code) and the desktop app's managed-connection keys (<c>connection.json</c>). Then a
/// two-way pairing by request with its reciprocal read-back, and one by a two-way code, run through the facade. Neither
/// the marker nor any key, code, claim secret or hash the flows wrote shows in any <c>/data-sync</c> answer, any
/// notification or the Information-level log, except the code in its own creator's <c>POST /data-sync/invitations</c>
/// answer; the test folders never show either.
/// </summary>
public partial class SecretCanaryTests
{
    private static readonly string[] AllKinds = [.. DataSyncKindIds.All];

    [TestMethod]
    public async Task The_marker_and_every_pairing_secret_never_leave_through_the_data_sync_api()
    {
        var logs = new CapturingLoggerProvider();
        var root = Path.Combine(Path.GetTempPath(), "canary-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var desk = await CanaryDevice.StartAsync("node-desk", "Desk", root, logs);
            await using var nas = await CanaryDevice.StartAsync("node-nas", "NAS", root, logs);
            await using var pc = await CanaryDevice.StartAsync("node-pc", "PC-2", root, logs);
            CanaryDevice[] devices = [desk, nas, pc];
            logs.Clear();

            var answers = new List<(string What, object? Answer)>();
            async Task<T> Call<T>(CanaryDevice device, string what, Func<DataSyncController, Task<T>> call)
            {
                var answer = await device.Harness.CallAsync(Callers.Loopback, call);
                answers.Add(($"{device.Name}: {what}", answer));
                return answer;
            }

            foreach (var device in devices)
            {
                var sharing = await Call(device, "sharing on",
                    c => c.SetSharing(new DataSyncSharingInput(true, true, null), default));
                Assert.IsNull(sharing.Data, $"{device.Name}: {sharing.Data?.Code}");
            }

            // By request, two-way: the desk asks the NAS, the NAS approves and reads the desk back through the desk's
            // reciprocal code, and the desk collects its grant.
            var asked = (await Call(desk, "create link by request", c => c.CreateLink(
                new DataSyncLinkCreateInput(null, nas.Node.Address, null, DataSyncLinkMode.TwoWay, AllKinds),
                default))).Data!;
            Assert.IsNull(asked.Problem, asked.Problem?.Detail);
            Assert.IsNotNull(asked.RequestId);
            var request = (await Call(nas, "requests", c => c.GetRequests(default))).Data!
                .Single(r => r.NodeId == "node-desk");
            var approved = (await Call(nas, "approve", c => c.ApproveRequest(request.RequestId,
                new DataSyncApproveInput(true, AllKinds), default))).Data!;
            Assert.IsNull(approved.Problem, approved.Problem?.Detail);
            Assert.IsTrue(approved.ReadBackGranted, "the NAS reads the desk back");
            await desk.Node.Flow.ClaimPendingAsync(default);
            Assert.IsTrue(await desk.Node.Grants.HasOutboundGrantAsync("node-nas", default));
            Assert.IsTrue(await nas.Node.Grants.HasOutboundGrantAsync("node-desk", default));

            // By a code made with two-way consent: the PC redeems the NAS's code, and the NAS reads the PC back.
            var invitation = await Call(nas, "invitation", c => c.CreateInvitation(
                new DataSyncInvitationInput(true), default));
            var code = invitation.Data!.Invitation!.Code;
            var redeemed = (await Call(pc, "create link by code", c => c.CreateLink(
                new DataSyncLinkCreateInput(null, nas.Node.Address, code, DataSyncLinkMode.TwoWay, AllKinds),
                default))).Data!;
            Assert.IsNull(redeemed.Problem, redeemed.Problem?.Detail);
            Assert.IsTrue(await pc.Node.Grants.HasOutboundGrantAsync("node-nas", default));
            await nas.Events.WaitForAsync("outbound node-pc");

            foreach (var device in devices)
            {
                await device.Harness.Provider.GetRequiredService<DataSyncGrantEventsHandler>().DrainAsync(default);
                foreach (var (what, read) in Reads()) await Call(device, what, read);
            }

            Assert.IsTrue((await nas.Node.Grants.GetGrantsAsync(default)).Count >= 3,
                "the NAS lets the desk, the PC and the seeded device read it");

            // Every secret the devices keep, read from their own files after the flows.
            var secrets = devices.SelectMany(d => d.Secrets()).Distinct().ToList();
            Assert.IsTrue(secrets.Count(s => !s.Contains(Marker, StringComparison.Ordinal)) >= 10,
                $"the flows wrote keys, codes and claim secrets: {secrets.Count}");
            secrets.Add(code);

            var exposed = new List<(string What, string Text)>();
            foreach (var (what, answer) in answers) exposed.Add((what, string.Join("\n", Strings(answer))));
            foreach (var device in devices)
            {
                exposed.Add(($"{device.Name}: notifications of pairing",
                    string.Join("\n", device.Node.Notifications.Created.SelectMany(n => Strings(n)))));
                exposed.Add(($"{device.Name}: notifications of data sync",
                    string.Join("\n", device.Harness.Notifications.Records.SelectMany(n => Strings(n)))));
            }

            foreach (var device in devices)
            {
                device.Node.Services.GetRequiredService<ILogger<SecretCanaryTests>>().LogInformation("node probe");
                device.Harness.Provider.GetRequiredService<ILogger<SecretCanaryTests>>().LogInformation("api probe");
            }

            StringAssert.Contains(logs.Text, "node probe", "the nodes' Information-level output is captured");
            StringAssert.Contains(logs.Text, "api probe", "the facades' Information-level output is captured");
            exposed.Add(("log", logs.Text));
            Assert.IsTrue(exposed.Any(e => e.What.EndsWith("notifications of pairing") && e.Text.Length > 0),
                "a request told a person");

            var invitationText = exposed.Single(e => e.What == "NAS: invitation").Text;
            StringAssert.Contains(invitationText, code, "the creator's own answer carries the code it made");
            foreach (var (what, text) in exposed)
            {
                Assert.IsFalse(text.Contains(Marker, StringComparison.OrdinalIgnoreCase), $"the marker is in {what}");
                foreach (var secret in secrets)
                {
                    if (what == "NAS: invitation" && secret == code) continue;
                    Assert.IsFalse(text.Contains(secret, StringComparison.Ordinal), $"a secret is in {what}");
                }

                foreach (var path in new[] { root, root.Replace('\\', '/'), Path.GetTempPath().TrimEnd('/', '\\') })
                    Assert.IsFalse(text.Contains(path, StringComparison.OrdinalIgnoreCase), $"a test folder is in {what}");
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    /// <summary>Every read of §10.1 a device answers without an id.</summary>
    private static IEnumerable<(string What, Func<DataSyncController, Task<object?>> Read)> Reads() =>
    [
        ("overview", async c => (await c.GetOverview(default)).Data),
        ("map", async c => (await c.GetMap(default)).Data),
        ("peers", async c => (await c.GetPeers(false, default)).Data),
        ("links", async c => (await c.GetLinks(default)).Data),
        ("requests", async c => (await c.GetRequests(default)).Data),
        ("readers", async c => (await c.GetReaders(default)).Data),
        ("inbox", async c => (await c.GetInbox(false, null, null, 0, 500, null, default)).Data),
        ("custom properties", async c => (await c.GetEntities(DataSyncKindIds.CustomProperty, default)).Data),
        ("extension groups", async c => (await c.GetEntities(DataSyncKindIds.ExtensionGroup, default)).Data),
        ("history", async c => (await c.GetHistory(default)).Data),
        ("restore", async c => (await c.GetRestore(default)).Data),
    ];

    /// <summary>Every string reachable from a value: what any serializer would write of it.</summary>
    private static IEnumerable<string> Strings(object? value, int depth = 0)
    {
        if (value is null || depth > 16) yield break;
        switch (value)
        {
            case string text:
                yield return text;
                yield break;
            case JsonNode node:
                yield return node.ToJsonString();
                yield break;
            case Enum or DateTime or DateTimeOffset or TimeSpan or Guid:
                yield break;
            case IDictionary dictionary:
            {
                foreach (DictionaryEntry entry in dictionary)
                {
                    foreach (var found in Strings(entry.Key, depth + 1)) yield return found;
                    foreach (var found in Strings(entry.Value, depth + 1)) yield return found;
                }

                yield break;
            }
            case IEnumerable items:
            {
                foreach (var item in items)
                    foreach (var found in Strings(item, depth + 1))
                        yield return found;
                yield break;
            }
        }

        var type = value.GetType();
        if (type.IsPrimitive || type == typeof(decimal)) yield break;
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length > 0) continue;
            foreach (var found in Strings(property.GetValue(value), depth + 1)) yield return found;
        }
    }

    /// <summary>One device: its node, its <c>/data-sync</c> facade over the node's grants, and its seeded folders.</summary>
    private sealed class CanaryDevice : IAsyncDisposable
    {
        private CanaryDevice(string name, CanaryFolder folder, DataSyncNodeHost node, DataSyncApiHarness harness,
            ForwardingGrantEvents events)
        {
            Name = name;
            Folder = folder;
            Node = node;
            Harness = harness;
            Events = events;
        }

        public string Name { get; }
        public CanaryFolder Folder { get; }
        public DataSyncNodeHost Node { get; }
        public DataSyncApiHarness Harness { get; }
        public ForwardingGrantEvents Events { get; }

        public static async Task<CanaryDevice> StartAsync(string nodeId, string name, string root,
            CapturingLoggerProvider logs)
        {
            var folder = new CanaryFolder(Path.Combine(root, nodeId));
            await SeedAsync(folder, nodeId);
            var events = new ForwardingGrantEvents();
            var node = await DataSyncNodeHost.StartAsync(nodeId, name, configure: s =>
            {
                s.AddSingleton<IFederationDataDirectory>(folder.Federation);
                s.AddSingleton<IRemoteAccessDataDirectory>(folder.RemoteAccess);
                s.AddSingleton<IDataSyncGrantEvents>(events);
                s.AddSingleton<ILoggerProvider>(logs);
            });
            var harness = await DataSyncApiHarness.CreateAsync(s =>
            {
                s.AddSingleton<IDataSyncGrantService>(node.Grants);
                s.AddSingleton<IDataSyncDeviceIdentity>(
                    new TestDataSyncDeviceIdentity(new DataSyncDevice(nodeId, "epoch-" + nodeId, name)));
                s.AddSingleton<ILoggerProvider>(logs);
                s.AddLogging(b => b.SetMinimumLevel(LogLevel.Information));
            }, registerFetchTask: false);
            events.Target = harness.Provider.GetRequiredService<DataSyncGrantEventsHandler>();
            return new CanaryDevice(name, folder, node, harness, events);
        }

        /// <summary>
        /// The marker in every secret the device keeps, written before it starts, the way each store writes its file.
        /// </summary>
        private static async Task SeedAsync(CanaryFolder folder, string nodeId)
        {
            var epoch = Guid.NewGuid().ToString("N");
            var expires = DateTimeOffset.UtcNow.AddDays(1);
            JsonObject Credentials(string grantId, string subject, string audience, string grantEpoch, string key) =>
                new()
                {
                    ["grantId"] = grantId, ["subjectNodeId"] = subject, ["audienceNodeId"] = audience,
                    ["libraryEpoch"] = grantEpoch, ["key"] = $"{Marker}-{key}", ["revision"] = 1,
                };
            JsonObject Offer(string what) => new()
                { ["addresses"] = new JsonArray("http://127.0.0.1:9"), ["code"] = $"{Marker}-{what}" };
            JsonObject Incoming(string requestId, string intent) => new()
            {
                ["requestId"] = requestId, ["transactionId"] = requestId, ["nodeId"] = "node-old-tablet",
                ["nodeName"] = "Old tablet", ["claimSecretHash"] = $"{Marker}-{requestId}-claim-hash",
                ["status"] = "awaitingApproval", ["expiresAt"] = expires, ["remoteAddress"] = "127.0.0.1",
                ["reciprocal"] = Offer(requestId + "-offer"), ["intent"] = intent,
            };
            JsonObject Outgoing(string requestId, string intent) => new()
            {
                ["requestId"] = requestId, ["address"] = "http://127.0.0.1:9", ["nodeId"] = "node-old",
                ["nodeName"] = "Old laptop", ["libraryEpoch"] = "old-epoch", ["claimSecret"] = $"{Marker}-{requestId}",
                ["status"] = "awaitingApproval", ["expiresAt"] = expires, ["intent"] = intent,
            };
            JsonObject Grant(string grantId, string key) => new()
            {
                ["credentials"] = Credentials(grantId, "node-old", nodeId, epoch, key), ["revoked"] = false,
                ["createdAt"] = DateTimeOffset.UtcNow,
            };
            JsonObject Invitation(string what) => new()
                { ["codeHash"] = $"{Marker}-{what}", ["expiresAt"] = expires, ["failedAttempts"] = 0, ["allowTwoWay"] = true };
            JsonArray Reciprocal(string what) => new(new JsonObject
                { ["codeHash"] = $"{Marker}-{what}", ["audienceNodeId"] = "node-old", ["expiresAt"] = expires });

            var state = new JsonObject
            {
                ["schemaVersion"] = 1, ["nodeId"] = nodeId, ["libraryEpoch"] = epoch, ["sharingEnabled"] = false,
                ["browsingEnabled"] = false,
                ["peers"] = new JsonObject
                {
                    ["node-old"] = new JsonObject
                    {
                        ["nodeId"] = "node-old", ["label"] = "Old laptop", ["address"] = "http://127.0.0.1:9",
                        ["libraryEpoch"] = "old-epoch", ["enabled"] = true, ["pathMappings"] = new JsonArray(),
                    },
                },
                ["inboundGrants"] = new JsonObject { ["library-in"] = Grant("library-in", "library-in-key") },
                ["outboundGrants"] = new JsonObject
                    { ["node-old"] = Credentials("library-out", nodeId, "node-old", "old-epoch", "library-out-key") },
                ["incomingRequests"] = new JsonArray(Incoming("library-request", "follow")),
                ["outgoingRequests"] = new JsonArray(Outgoing("library-outgoing", "follow")),
                ["invitation"] = Invitation("library-code-hash"),
                ["reciprocalInvitations"] = Reciprocal("library-reciprocal-hash"),
                ["dataSyncSharingEnabled"] = false,
                ["inboundDataSyncGrants"] = new JsonObject { ["datasync-in"] = Grant("datasync-in", "datasync-in-key") },
                ["outboundDataSyncGrants"] = new JsonObject
                    { ["node-old"] = Credentials("datasync-out", nodeId, "node-old", "old-epoch", "datasync-out-key") },
                ["incomingDataSyncRequests"] = new JsonArray(Incoming("datasync-request", "twoWay")),
                ["outgoingDataSyncRequests"] = new JsonArray(Outgoing("datasync-outgoing", "follow")),
                ["dataSyncInvitation"] = Invitation("datasync-code-hash"),
                ["dataSyncReciprocalInvitations"] = Reciprocal("datasync-reciprocal-hash"),
            };
            await File.WriteAllTextAsync(Path.Combine(folder.Federation.Ensure(), FederationStateStore.FileName),
                state.ToJsonString());

            await new RemoteDeviceStore(folder.RemoteAccess).MutateAsync(d =>
            {
                d.Devices.Add(new RemoteDevice
                {
                    Id = "device-phone", Name = "Phone", Platform = RemoteDevicePlatform.Android,
                    Key = $"{Marker}-device-key", CreatedAt = DateTime.UtcNow,
                });
                d.PendingRequests.Add(new PendingPairingRequest
                {
                    Id = "pairing-tablet", DeviceName = "Tablet", RequestedAt = DateTime.UtcNow,
                    ExpiresAt = DateTime.UtcNow.AddMinutes(10), ApprovedDeviceId = "device-tablet",
                    ApprovedKey = $"{Marker}-approved-key", ApprovedByDeviceId = "device-phone",
                });
                d.PairingCode = new PairingCodeState
                    { CodeHash = $"{Marker}-pairing-code-hash", ExpiresAt = DateTime.UtcNow.AddMinutes(10) };
            });
            await new ClientConnectionStore(folder.Managed).MutateAsync(d => d.Servers.Add(new ClientServerConnection
            {
                ServerId = "server-old", ServerName = "Old NAS", BaseAddress = "http://127.0.0.1:9",
                DeviceId = "device-desk", DeviceKey = $"{Marker}-managed-key", PairedAt = DateTime.UtcNow,
            }));
        }

        /// <summary>Every key, code, claim secret and hash the device's files hold now.</summary>
        public IEnumerable<string> Secrets()
        {
            string[] names = ["key", "claimSecret", "claimSecretHash", "codeHash", "code", "approvedKey", "deviceKey"];
            var files = new[]
            {
                Path.Combine(Folder.Federation.Path, FederationStateStore.FileName),
                Path.Combine(Folder.RemoteAccess.Path, RemoteDeviceStore.FileName),
                Path.Combine(Folder.Managed.Path, ClientConnectionStore.FileName),
            };
            foreach (var file in files)
            {
                Assert.IsTrue(File.Exists(file), file);
                foreach (var secret in Find(JsonNode.Parse(File.ReadAllText(file))))
                    yield return secret;
            }

            IEnumerable<string> Find(JsonNode? node)
            {
                switch (node)
                {
                    case JsonObject obj:
                        foreach (var (name, value) in obj)
                        {
                            if (value is JsonValue v && v.TryGetValue<string>(out var text) && text.Length >= 8 &&
                                names.Contains(name, StringComparer.OrdinalIgnoreCase))
                                yield return text;
                            foreach (var found in Find(value)) yield return found;
                        }

                        break;
                    case JsonArray array:
                        foreach (var item in array)
                            foreach (var found in Find(item))
                                yield return found;
                        break;
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            await Harness.DisposeAsync();
            await Node.DisposeAsync();
        }
    }

    /// <summary>A device's data folders: federation, remote access and the desktop app's managed connections.</summary>
    private sealed class CanaryFolder(string root)
    {
        public Folder Federation { get; } = new(Path.Combine(root, "federation"));
        public Folder RemoteAccess { get; } = new(Path.Combine(root, "remote-access"));
        public Folder Managed { get; } = new(Path.Combine(root, "remote-access", "managed"));
    }

    private sealed class Folder(string path) : IFederationDataDirectory, IRemoteAccessDataDirectory, IClientDataDirectory
    {
        public string Path => path;
        public string Ensure() => Directory.CreateDirectory(path).FullName;
    }

    /// <summary>What pairing tells data sync, recorded and handed on to the device's runtime.</summary>
    private sealed class ForwardingGrantEvents : IDataSyncGrantEvents
    {
        private readonly List<string> _raised = [];
        public IDataSyncGrantEvents? Target { get; set; }

        public void OutboundGranted(string peerNodeId)
        {
            Record("outbound " + peerNodeId);
            Target?.OutboundGranted(peerNodeId);
        }

        public void InboundGranted(string peerNodeId, DataSyncRequestIntent intent, bool readBackStarted)
        {
            Record($"inbound {peerNodeId} {intent} {readBackStarted}");
            Target?.InboundGranted(peerNodeId, intent, readBackStarted);
        }

        public void ReadBackFailed(string peerNodeId, string errorCode)
        {
            Record($"readBackFailed {peerNodeId} {errorCode}");
            Target?.ReadBackFailed(peerNodeId, errorCode);
        }

        private void Record(string what)
        {
            lock (_raised) _raised.Add(what);
        }

        /// <summary>Waits for an event raised in the background (the read-back a code starts), at most ten seconds.</summary>
        public async Task WaitForAsync(string raised)
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (true)
            {
                lock (_raised)
                {
                    if (_raised.Contains(raised)) return;
                }

                if (DateTime.UtcNow > deadline) Assert.Fail($"'{raised}' was not raised.");
                await Task.Delay(20);
            }
        }
    }
}
