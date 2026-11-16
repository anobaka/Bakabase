using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Acquisition.Components;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Service.Components.CloudDownloads;

namespace Bakabase.Service.Components.Acquisition.Planning;

public sealed partial class AcquisitionStorageOverrideService
{
    private async Task<AcquisitionStorageOverrideResult> ContinueDownloadAsync(int id,
        AcquisitionStorageOverrideApplyInput input, AcquisitionStorageOverridePreviewStore.Entry entry,
        CancellationToken ct, Func<string?, Task>? guardDirectory)
    {
        if (resumer == null || input.UseEmergencySpaceReserve == null ||
            string.IsNullOrWhiteSpace(input.SignalJson) || input.SignalJson.Length > 1024 * 1024)
            throw new ArgumentException("STORAGE_OVERRIDE_SELECTION_INVALID");
        CloudSelectionSignal signal;
        try
        {
            using var document = JsonDocument.Parse(input.SignalJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                document.RootElement.EnumerateObject().Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() !=
                document.RootElement.EnumerateObject().Count()) throw new JsonException();
            signal = document.RootElement.Deserialize<CloudSelectionSignal>(WorkflowJson.Options) ?? throw new JsonException();
            if (signal.SourceIds == null || signal.SourceMethods == null || signal.SourceAccounts == null ||
                signal.SourceCredentialCorrections == null) throw new JsonException();
        }
        catch (JsonException) {throw new ArgumentException("STORAGE_OVERRIDE_SELECTION_INVALID");}
        // The preview captures specific original targets. A multi-target retry must never
        // degrade to the old unscoped "{}" signal, including after the preview becomes stale.
        if (entry.Preview.DownloadTargets is {} targets)
        {
            var selected = signal.ResumeTargetIds;
            var canonical = new CloudSelectionSignal {ObserveExistingOnly = true, ResumeTargetIds = selected};
            if (JsonSerializer.Serialize(signal, WorkflowJson.Options) != JsonSerializer.Serialize(canonical, WorkflowJson.Options) ||
                (selected != null && (selected.Count == 0 || selected.Distinct(StringComparer.Ordinal).Count() != selected.Count ||
                    selected.Any(key => !targets.Any(t => t.TargetId == key && t.CanRetry)))))
                throw new ArgumentException("STORAGE_OVERRIDE_SELECTION_INVALID");
        }
        var gate = AcquisitionTaskGate.For(id); await gate.WaitAsync(ct);
        var granted = false;
        try
        {
            var (task, run) = await CurrentAsync(id, ct);
            await ValidateAsync(task, run, ct, "download");
            if (entry.Preview.ExpiresAt <= DateTimeOffset.UtcNow) throw new ArgumentException("STORAGE_OVERRIDE_PREVIEW_EXPIRED");
            if (AcquisitionRecoveryService.Version(task!, run) != entry.Version ||
                options.Value.MinimumFreeSpaceBytes != entry.Preview.MinimumFreeBytes ||
                options.Value.UseEmergencySpaceReserve != entry.Preview.UseEmergencySpaceReserve)
                throw new ArgumentException("ACQUISITION_TASK_CHANGED");
            if (guardDirectory != null)
            {
                if (continuations == null) throw new ArgumentException("STORAGE_OVERRIDE_REASON_UNSUPPORTED");
                foreach (var path in await continuations.CapturePaths(task!, run, "{}", ct)) await guardDirectory(path);
                await guardDirectory(signal.OutputDirectory);
            }
            // Preserve the exact node answer. The existing activity still decides whether to
            // observe, revalidate an original download, correct a credential or select a source.
            // The reserve permission alone never supplies any of those capabilities.
            await resumer.ContinueConditionallyAsync(run!.Id, true, input.SignalJson, async token =>
            {
                var (current, currentRun) = await CurrentAsync(id, token);
                await ValidateAsync(current, currentRun, token, "download");
                if (entry.Preview.ExpiresAt <= DateTimeOffset.UtcNow ||
                    AcquisitionRecoveryService.Version(current!, currentRun) != entry.Version ||
                    options.Value.MinimumFreeSpaceBytes != entry.Preview.MinimumFreeBytes ||
                    options.Value.UseEmergencySpaceReserve != entry.Preview.UseEmergencySpaceReserve)
                    throw new ArgumentException("ACQUISITION_TASK_CHANGED");
                current = await AcquisitionContinuationReserve.SaveWhileLockedAsync(db, current!, currentRun!,
                    input.UseEmergencySpaceReserve, input.Token, token);
                granted = true;
                // Persist follow-up intent before the resumer can enqueue/consume the signal.
                await new AcquisitionDownloadContinuationService(db).SaveWhileLockedAsync(current, currentRun!.Id,
                    AcquisitionDownloadContinuationService.Automatic, token);
            }, ct);
            var result = new AcquisitionStorageOverrideResult(id, run.Id, "applied", true);
            lock (entry) entry.Result = result;
            return result;
        }
        catch (Exception error) when (granted && error is InvalidOperationException or ArgumentException or OperationCanceledException)
        {
            // A persisted signal may already be executing. Never resubmit it, revoke its
            // reserve mid-write, or report that a new download has definitely started.
            var result = new AcquisitionStorageOverrideResult(id, entry.Preview.WorkflowRunId, "applied", false,
                "STORAGE_OVERRIDE_OUTCOME_UNKNOWN");
            lock (entry) entry.Result = result;
            return result;
        }
        finally {gate.Release();}
    }
}
