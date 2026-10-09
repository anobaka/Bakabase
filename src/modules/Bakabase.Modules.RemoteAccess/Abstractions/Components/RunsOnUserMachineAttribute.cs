using System;

namespace Bakabase.Modules.RemoteAccess.Abstractions.Components;

/// <summary>
/// Marks an action whose effect lands on the machine that executes it, so it only
/// makes sense to run on the machine the user is sitting at.
/// </summary>
/// <remarks>
/// <para>
/// Launching a player, opening a folder, showing a native window: doing any of
/// these on the server puts something on a screen nobody is watching and reports
/// success. Today that is exactly what a remote browser gets in the container
/// build, because <c>Unrestricted</c> makes the authorization filter return early.
/// </para>
/// <para>
/// This is orthogonal to <c>[RemoteAccessible]</c>, which answers "may a remote
/// caller reach this at all". This one answers "where does it have to run", and it
/// is read by two sides that never talk to each other at runtime:
/// </para>
/// <list type="bullet">
/// <item>the server refuses these for any non-loopback caller, paired or not,
/// unless the action explicitly provides a browser fallback;</item>
/// <item>the client's local forwarding layer keeps them instead of forwarding.</item>
/// </list>
/// <para>
/// Each side compiles its own copy of the set, so a version mismatch degrades
/// predictably: the worst case is a client forwarding something the server then
/// refuses with a reason. Nothing runs silently in the wrong place.
/// </para>
/// <para>
/// <b>The marked set is not the set the forwarder intercepts.</b> Some actions the
/// server must keep answering (the userscript, the remote-access context, the
/// per-machine half of the app options) are still handled locally by the client
/// through an explicit override table. See the execution plan §5.10.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RunsOnUserMachineAttribute : Attribute
{
    /// <summary>
    /// Short, human-readable reason shown to the caller when the server refuses.
    /// Written for a user reading an error, not for a developer reading a log.
    /// </summary>
    public string? Reason { get; init; }

    /// <summary>
    /// The action can answer in the caller's browser instead of launching a native
    /// process. Normal remote authorization still applies; the action must only
    /// launch natively for a loopback caller on an explicitly identified desktop.
    /// </summary>
    public bool HasBrowserFallback { get; init; }
}
