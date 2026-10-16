using System.Reflection;
using System.Reflection.Emit;
using Bakabase.Modules.Federation.Identity;
using Bakabase.Modules.Federation.Peers;
using Bakabase.Modules.Federation.Security;
using Bakabase.Modules.Federation.Transport;
using Bakabase.Service.Components.Federation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.Federation;

/// <summary>
/// Definitions sync uses direct scoped grants and serves only this device's feed. The identity controller
/// cannot create definitions access, and the Federation module stays independent from the sync runtime.
/// </summary>
[TestClass]
public sealed class DataSyncGrantBoundaryTests
{
    /// <summary>The services that manage definitions access.</summary>
    private static readonly Type[] AccessServices =
    [
        typeof(FederationPeerService), typeof(NodePairingClient), typeof(FederationPairingFlow),
        typeof(NodeGrantService), typeof(FederationStateStore), typeof(PeerSessionFactory)
    ];

    [TestMethod]
    public void TheIdentityPlaneDoesNotIssueDataSyncGrants()
    {
        var calls = typeof(Bakabase.Service.Controllers.FederationPeerController)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .SelectMany(method => CalledMethods(method).Select(call => (method, call))).ToArray();
        Assert.IsTrue(calls.Any(c => c.call.DeclaringType == typeof(FederationStateStore) &&
                                     c.call.Name == nameof(FederationStateStore.ChangeDisplayNameAsync)),
            "The scan follows async actions into their bodies.");
        foreach (var (method, call) in calls)
            Assert.IsFalse(call.DeclaringType == typeof(FederationPeerService) &&
                           call.Name.Contains("DataSync", StringComparison.Ordinal),
                $"{method.Name} calls {call.Name}");
    }

    /// <summary>
    /// The other direction (§1, N2): the federation module knows nothing of data sync. Grants it issues or obtains
    /// reach the data sync runtime only through the Service's pairing flow, which raises the grant events.
    /// </summary>
    [TestMethod]
    public void TheFederationModuleNeverReferencesDataSync()
    {
        var module = typeof(FederationPeerService).Assembly;
        var referenced = module.GetReferencedAssemblies().Select(a => a.Name!).ToArray();
        Assert.IsTrue(referenced.Contains("Bakabase.Modules.RemoteAccess"), "The scan reads the module's real references.");
        Assert.IsFalse(referenced.Any(name => name.StartsWith("Bakabase.Modules.DataSync", StringComparison.Ordinal)),
            string.Join(", ", referenced));
        // The claim loop hands data sync what it claimed as plain node ids, and the outcomes it raises them from are
        // the federation module's own: a node id and the read-back word.
        Assert.AreEqual(typeof(Task<IReadOnlyList<string>>),
            typeof(NodePairingClient).GetMethod(nameof(NodePairingClient.ClaimPendingDataSyncAsync))!.ReturnType);
        Assert.AreEqual(typeof(Task<IReadOnlyList<NodeDataSyncPairingOutcome>>), typeof(NodePairingClient)
            .GetMethod(nameof(NodePairingClient.ClaimPendingDataSyncOutcomesAsync))!.ReturnType);
        Assert.AreEqual(module, typeof(NodeDataSyncPairingOutcome).Assembly);
    }

    /// <summary>
    /// The hub relay rule on the reading side (§7.7, D03): the feed reader reaches a peer only through a datasync
    /// session for that peer — never an implicit session, a transport overload that chooses a grant, or credentials found any
    /// other way — and names no scope but <c>datasync.read</c>.
    /// </summary>
    [TestMethod]
    public void ThePeerClientReadsOnlyWithADirectDefinitionsGrant()
    {
        var calls = CalledMethods(typeof(FederationDataSyncPeerClient)).Distinct().ToArray();
        bool Calls(Type type, string name, Func<ParameterInfo[], bool> parameters) => calls.Any(m =>
            m.DeclaringType == type && m.Name == name && parameters(m.GetParameters()));

        Assert.IsTrue(Calls(typeof(PeerSessionFactory), nameof(PeerSessionFactory.GetAsync), p => p.Length == 3),
            "The scan sees the scoped session it reads with.");
        Assert.IsTrue(Calls(typeof(INodeTransport), nameof(INodeTransport.SendAsync),
            p => p[0].ParameterType == typeof(PeerSessionSnapshot)));
        Assert.IsFalse(Calls(typeof(PeerSessionFactory), nameof(PeerSessionFactory.GetAsync), p => p.Length != 3),
            "The session must name its definitions scope explicitly.");
        Assert.IsFalse(Calls(typeof(IPeerSessionFactory), nameof(IPeerSessionFactory.GetAsync), _ => true),
            "The session must name its definitions scope explicitly.");
        Assert.IsFalse(Calls(typeof(INodeTransport), nameof(INodeTransport.SendAsync),
            p => p[0].ParameterType == typeof(string)), "The transport must use the verified session rather than choose its own.");
        var reached = calls.Where(m => m.DeclaringType != null && AccessServices.Contains(m.DeclaringType) &&
                                       m.DeclaringType != typeof(PeerSessionFactory))
            .Select(m => $"{m.DeclaringType!.Name}.{m.Name}").Distinct().ToArray();
        Assert.AreEqual(0, reached.Length, string.Join(", ", reached));

        var strings = LoadedStrings(typeof(FederationDataSyncPeerClient)).ToHashSet();
        Assert.IsTrue(strings.Contains(FederationScopes.DataSyncRead));
        Assert.IsFalse(strings.Contains("library.read"));
        Assert.IsFalse(strings.Contains(FederationScopes.Any));
    }

    /// <summary>
    /// The hub relay rule on the serving side (§7.7, D03): a node answers a feed read from its own feed and never
    /// forwards it. Nothing the feed endpoint calls or resolves can reach another node.
    /// </summary>
    [TestMethod]
    public void TheFeedEndpointNeverReachesAnotherNode()
    {
        Type[] outbound =
        [
            typeof(INodeTransport), typeof(NodeTransport), typeof(IPeerSessionFactory), typeof(PeerSessionFactory),
            typeof(FederationHttpClient), typeof(NodePairingClient), typeof(INodePeerDiscovery),
            typeof(Bakabase.Modules.DataSync.Runtime.IDataSyncPeerClient), typeof(FederationDataSyncPeerClient),
            typeof(FederationDataSyncGrants), typeof(FederationPairingFlow), typeof(HttpClient),
            typeof(HttpMessageInvoker)
        ];
        var controller = typeof(Bakabase.Service.Controllers.DataSyncNodeController);
        var calls = CalledMethods(controller).Distinct().ToArray();

        Assert.IsTrue(calls.Any(m => m.DeclaringType == typeof(Bakabase.Modules.DataSync.Runtime.IDataSyncFeedSource) &&
                                     m.Name == nameof(Bakabase.Modules.DataSync.Runtime.IDataSyncFeedSource.GetPageAsync)),
            "The scan follows the actions into the feed source.");
        var reaching = calls.Where(m => outbound.Any(t => t.IsAssignableFrom(m.DeclaringType)) ||
                                        m.IsGenericMethod && m.GetGenericArguments().Any(a => outbound.Contains(a)))
            .Select(m => $"{m.DeclaringType!.Name}.{m.Name}").Distinct().ToArray();
        Assert.AreEqual(0, reaching.Length, string.Join(", ", reaching));
        CollectionAssert.AreEqual(new[] { typeof(FederationPeerService), typeof(Bakabase.Modules.DataSync.Runtime.IDataSyncFeedSource) },
            controller.GetConstructors().Single().GetParameters().Select(p => p.ParameterType).ToArray());
    }

    /// <summary>Every string constant <paramref name="type"/>'s code loads, nested compiler-made types included.</summary>
    private static IEnumerable<string> LoadedStrings(Type type)
    {
        foreach (var nested in Nested(type))
        foreach (var method in nested.GetMethods(All).Cast<MethodBase>().Concat(nested.GetConstructors(All)))
        {
            var il = method.GetMethodBody()?.GetILAsByteArray();
            if (il == null) continue;
            for (var i = 0; i < il.Length;)
            {
                var code = il[i] == 0xFE ? OpCodesByValue[(ushort)(0xFE00 | il[i + 1])] : OpCodesByValue[il[i]];
                i += code.Size;
                if (code.OperandType == OperandType.InlineString)
                    yield return method.Module.ResolveString(BitConverter.ToInt32(il, i));
                i += OperandSize(code.OperandType, il, i);
            }
        }
    }

    /// <summary>Every method called by <paramref name="type"/>'s code, including its nested compiler-made types.</summary>
    private static IEnumerable<MethodBase> CalledMethods(Type type)
    {
        foreach (var nested in Nested(type))
        foreach (var method in nested.GetMethods(All).Cast<MethodBase>().Concat(nested.GetConstructors(All)))
        foreach (var call in CalledMethods(method))
            yield return call;
    }

    private static IEnumerable<MethodBase> CalledMethods(MethodBase method)
    {
        var il = method.GetMethodBody()?.GetILAsByteArray();
        if (il == null) yield break;
        var owner = method.DeclaringType!;
        // An async method's body is its state machine; follow it.
        if (method.GetCustomAttribute<System.Runtime.CompilerServices.AsyncStateMachineAttribute>() is { } machine)
            foreach (var call in CalledMethods(machine.StateMachineType))
                yield return call;
        for (var i = 0; i < il.Length;)
        {
            var code = il[i] == 0xFE ? OpCodesByValue[(ushort)(0xFE00 | il[i + 1])] : OpCodesByValue[il[i]];
            i += code.Size;
            if (code.OperandType == OperandType.InlineMethod)
            {
                MethodBase? called = null;
                try
                {
                    called = method.Module.ResolveMethod(BitConverter.ToInt32(il, i),
                        owner.IsGenericType ? owner.GetGenericArguments() : null,
                        method.IsGenericMethod ? method.GetGenericArguments() : null);
                }
                catch (ArgumentException)
                {
                }
                if (called != null) yield return called;
            }
            i += OperandSize(code.OperandType, il, i);
        }
    }

    private static IEnumerable<Type> Nested(Type type)
    {
        yield return type;
        foreach (var nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
        foreach (var inner in Nested(nested))
            yield return inner;
    }

    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                                     BindingFlags.Static | BindingFlags.DeclaredOnly;

    private static readonly Dictionary<ushort, OpCode> OpCodesByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(f => (OpCode)f.GetValue(null)!)
        .ToDictionary(o => (ushort)o.Value);

    private static int OperandSize(OperandType type, byte[] il, int at) => type switch
    {
        OperandType.InlineNone => 0,
        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
        OperandType.InlineVar => 2,
        OperandType.InlineI8 or OperandType.InlineR => 8,
        OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, at),
        _ => 4
    };
}
