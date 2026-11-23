using Bakabase.Abstractions.Components.Threading;
using Bakabase.Modules.AI.Models.Domain;

namespace Bakabase.Modules.AI.Services;

/// <summary>One application-wide inference slot for file-processing decisions.</summary>
public sealed class FileProcessorLlmExecutionPolicy : ILlmFeatureExecutionPolicy
{
    private readonly AsyncConcurrencyGate _gate = new(() => 1);
    private readonly object _sync = new();
    private int _active;
    private int _waiting;
    public string CapabilityId => "ai.fileProcessor";
    public AiFeature Feature => AiFeature.FileProcessor;
    public int Capacity => 1;

    public LlmFeatureExecutionSnapshot GetSnapshot()
    {
        lock (_sync) return new(CapabilityId, Feature, Capacity, _active, _waiting);
    }

    public async Task<IDisposable> EnterAsync(CancellationToken ct = default)
    {
        var waiting = false;
        IDisposable? capacityLease = null;
        try
        {
            capacityLease = await _gate.EnterAsync(ct, async () =>
            {
                lock (_sync) { _waiting++; waiting = true; }
                await LlmExecutionScope.ReportAsync(new(CapabilityId, Feature, LlmFeatureExecutionStatus.Queued), ct)
                    .ConfigureAwait(false);
            }).ConfigureAwait(false);
            lock (_sync)
            {
                if (waiting) { _waiting--; waiting = false; }
                _active++;
            }
            var lease = new Lease(this, capacityLease);
            capacityLease = null;
            try
            {
                ct.ThrowIfCancellationRequested();
                await LlmExecutionScope.ReportAsync(new(CapabilityId, Feature, LlmFeatureExecutionStatus.Running), ct)
                    .ConfigureAwait(false);
                return lease;
            }
            catch { lease.Dispose(); throw; }
        }
        finally
        {
            if (waiting) lock (_sync) _waiting--;
            capacityLease?.Dispose();
        }
    }

    private sealed class Lease(FileProcessorLlmExecutionPolicy owner, IDisposable capacityLease) : IDisposable
    {
        private IDisposable? _capacityLease = capacityLease;
        public void Dispose()
        {
            var lease = Interlocked.Exchange(ref _capacityLease, null);
            if (lease == null) return;
            lock (owner._sync) owner._active--;
            lease.Dispose();
        }
    }
}
