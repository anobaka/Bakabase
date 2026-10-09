using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Infrastructures.Components.App.SingleInstance;

namespace Bakabase.Service.Components.ServerData;

/// <summary>The only process that copies/replaces data. It never constructs a business host.</summary>
public static class SetupMaintenanceWorker
{
    public static async Task<int> RunAsync(SetupChildConnection connection, CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, connection.Stopping);
        var locks = new List<DataDirectoryLock>();
        ImportProgressStore? monitor = null;
        try
        {
            var anchor = AppDataLocator.ResolveAnchor();
            DataDirectoryLock Own(string path)
            {
                var owned = locks.Find(l => ServerSetupSession.SameDirectory(l.Directory, path));
                if (owned != null) return owned;
                var claim = DataDirectoryLock.TryAcquire(path);
                if (!claim.Acquired) throw new IOException($"Cannot lock maintenance directory {path}: {claim.Status}.", claim.Error);
                locks.Add(claim.Lock!); return claim.Lock!;
            }
            Own(anchor);
            ServerSetupSession.RecoverSubmittedRedirect(anchor, AppDataLocator.IsEnvironmentOverride || ServerSetupSession.InContainer);
            var data = AppDataLocator.ResolveEffectiveDataDirectory(anchor);
            Own(data);
            monitor = new ImportProgressStore(data);
            ImportProgressStore.Current = monitor;
            var move = ServerAppDataRelocation.ReadPending(anchor);
            var draftId = move?.PathPlan?.DraftId;
            if (move != null)
            {
                var sourceLock = Own(move.SourcePath); var targetLock = Own(move.TargetPath);
                monitor.BeginRelocation(move);
                var moved = await Task.Run(() => ServerAppDataRelocation.ApplyPending(anchor, sourceLock, targetLock,
                    Console.WriteLine, monitor.Report, linked.Token), linked.Token);
                if (moved != null && !ServerSetupSession.SameDirectory(data, moved))
                {
                    monitor.Dispose(); monitor = new ImportProgressStore(moved); ImportProgressStore.Current = monitor; data = moved;
                }
            }
            var pending = ServerAppDataImport.ReadPending(data);
            if (pending != null)
            {
                draftId ??= pending.PathPlan?.DraftId;
                monitor.Begin(pending);
                await Task.Run(() => ServerAppDataImport.ApplyPending(data, Console.WriteLine, monitor.Report, linked.Token), linked.Token);
            }
            linked.Token.ThrowIfCancellationRequested();
            // Clearing the matching edit draft cannot invalidate the completed installation.
            // If cleanup fails, keep the recoverable JSON and proceed with the verified data.
            if (draftId != null)
                try { SetupImportDraftStore.Clear(anchor, draftId); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                { Console.Error.WriteLine("Import succeeded, but the saved setup draft could not be cleared: " + error.Message); }
            monitor.Starting();
            connection.Send("finished", data);
            return 0;
        }
        catch (Exception error)
        {
            monitor?.Fail(error is OperationCanceledException ? "Maintenance interrupted. Restart Setup to resume from its saved record." : error.Message);
            try { connection.Fatal(error.Message); } catch (IOException) { }
            Console.Error.WriteLine($"Setup worker failed: {error}");
            return 1;
        }
        finally
        {
            ImportProgressStore.Current = null; monitor?.Dispose();
            foreach (var held in locks) held.Dispose();
        }
    }
}
