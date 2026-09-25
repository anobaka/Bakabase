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
/// Data sync reaches federation only through <see cref="FederationDataSyncGrants"/>, and that bridge never touches
/// library access (§7.1.5, G29): every member of the federation services it calls is a definitions member or one
/// that belongs to neither kind of access. Read from the compiled code, lambdas and async bodies included, so a
/// later edit cannot quietly add a path from <c>/data-sync</c> to library grants. The federation module itself
/// never references data sync.
/// </summary>
[TestClass]
public sealed class DataSyncGrantBoundaryTests
{
    /// <summary>The services that manage access of both kinds.</summary>
    private static readonly Type[] AccessServices =
    [
        typeof(FederationPeerService), typeof(NodePairingClient), typeof(FederationPairingFlow),
        typeof(NodeGrantService), typeof(FederationStateStore), typeof(PeerSessionFactory)
    ];

    /// <summary>Members of those services that belong to neither kind of access.</summary>
    private static readonly HashSet<string> Neutral =
    [
        nameof(FederationPeerService.GetPeerNameAsync), nameof(FederationPairingFlow.GetShareBackAddresses),
        nameof(FederationPairingFlow.RaiseOutboundGranted), nameof(FederationPairingFlow.RaiseInboundGranted),
    ];

    [TestMethod]
    public void TheDataSyncBridgeCallsNoLibraryScopeMember()
    {
        var calls = CalledMethods(typeof(FederationDataSyncGrants)).Distinct().ToArray();
        var reached = calls.Where(m => m.DeclaringType != null && AccessServices.Contains(m.DeclaringType)).ToArray();

        // The scan sees what the bridge is known to call, so an empty result would not pass for a clean one.
        foreach (var expected in new[]
                 {
                     nameof(FederationPeerService.ApproveDataSyncAsync), nameof(FederationPeerService.SetDataSyncSharingAsync),
                     nameof(FederationPeerService.IssueDataSyncInvitationAsync), nameof(FederationPeerService.RevokeDataSyncAsync),
                     nameof(NodePairingClient.ConnectDataSyncAsync), nameof(FederationPairingFlow.ReadBackDataSyncAsync)
                 })
            Assert.IsTrue(reached.Any(m => m.Name == expected), expected);

        var library = reached.Where(m => !m.Name.Contains("DataSync", StringComparison.Ordinal) &&
                                         !Neutral.Contains(m.Name) &&
                                         !(m.DeclaringType == typeof(PeerSessionFactory) &&
                                           m.Name == nameof(PeerSessionFactory.GetConnectionState) &&
                                           m.GetParameters().Length == 2))
            .Select(m => $"{m.DeclaringType!.Name}.{m.Name}").Distinct().ToArray();
        Assert.AreEqual(0, library.Length, string.Join(", ", library));
    }

    [TestMethod]
    public void TheLibraryPlaneHasNoDataSyncRoute()
    {
        // /federation/local manages library access only; definitions access is managed through /data-sync.
        var calls = typeof(Bakabase.Service.Controllers.FederationPeerController)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .SelectMany(method => CalledMethods(method).Select(call => (method, call))).ToArray();
        Assert.IsTrue(calls.Any(c => c.call.DeclaringType == typeof(FederationPeerService) &&
                                     c.call.Name == nameof(FederationPeerService.ApproveAsync)),
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
        // The claim loop hands data sync what it claimed as plain node ids.
        Assert.AreEqual(typeof(Task<IReadOnlyList<string>>),
            typeof(NodePairingClient).GetMethod(nameof(NodePairingClient.ClaimPendingDataSyncAsync))!.ReturnType);
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
