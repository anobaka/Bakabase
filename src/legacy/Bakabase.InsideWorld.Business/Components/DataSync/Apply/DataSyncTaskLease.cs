using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.InsideWorld.Business.Components.DataSync.Persistence;
using Bakabase.Modules.DataSync.Runtime;

namespace Bakabase.InsideWorld.Business.Components.DataSync.Apply;

/// <summary>
/// A task body's hold on the <see cref="DataSyncGate"/> (§8.10.1) that it may give back while its task is paused, so
/// a paused task never keeps heads, manifests, entity settings, retention or any other data sync work waiting. While
/// given back, <see cref="IsHeld"/> is false; Refresh and the actor check refuse it until the body takes the gate again.
/// </summary>
internal sealed class DataSyncTaskLease : DataSyncGateLease
{
    private readonly DataSyncGate _gate;
    private DataSyncGateLease? _held;

    private DataSyncTaskLease(DataSyncGate gate, DataSyncGateLease held)
    {
        _gate = gate;
        _held = held;
    }

    /// <summary>Waits for the gate without a limit, as a BTask body does.</summary>
    public static async Task<DataSyncTaskLease> EnterAsync(DataSyncGate gate, CancellationToken ct) =>
        new(gate, await gate.EnterAsync(null, ct));

    public override bool IsHeld => _held?.IsHeld == true;

    /// <summary>
    /// Honours a pause of the task: when it is paused, gives the gate back, waits until the task resumes and takes the
    /// gate again. Call it only with no transaction open. Returns whether the gate was given back meanwhile — anything
    /// may have run then, so the caller checks the attempt and the actor again. A stop while paused throws with the gate
    /// given back.
    /// </summary>
    public async Task<bool> WaitWhilePausedAsync(BTaskArgs args)
    {
        var ct = args.CancellationToken;
        var paused = args.PauseToken.WaitWhilePausedAsync(ct);
        if (paused.IsCompleted)
        {
            await paused;
            return false;
        }

        _held?.Dispose();
        _held = null;
        await paused;
        _held = await _gate.EnterAsync(null, ct);
        return true;
    }

    public override void Dispose()
    {
        _held?.Dispose();
        _held = null;
    }
}
