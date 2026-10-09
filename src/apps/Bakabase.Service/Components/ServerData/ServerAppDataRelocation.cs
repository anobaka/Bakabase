using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Infrastructures.Components.App.Relocation;
using Bakabase.Infrastructures.Components.App.SingleInstance;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bakabase.Service.Components.ServerData;

/// <summary>Copy to an empty destination, switch the anchor, and retain the stopped source as a recovery copy.</summary>
public static class ServerAppDataRelocation
{
    public const string MarkerName = ".bakabase-relocate.json";
    public const string WorkName = ".bakabase-relocate-work";
    private static readonly object Gate = new();
    private static readonly HashSet<string> Excludes = new(StringComparer.OrdinalIgnoreCase)
    {
        MarkerName, MarkerName + ".tmp", WorkName, SetupProcessCoordinator.LockFileName, DataDirectoryLock.FileName,
        AnchorRedirect.FileName, AnchorRedirect.FileName + ".tmp", ServerSetupSession.FileName,
        ServerSetupSession.FileName + ".tmp", ImportProgressStore.FileName, ImportProgressStore.FileName + ".tmp",
        SetupImportDraftStore.FileName, SetupImportDraftStore.FileName + ".tmp",
        ServerAppDataImport.MarkerName, ServerAppDataImport.MarkerName + ".tmp", ServerAppDataImport.WorkName,
        PendingRelocation.FileName, PendingRelocationRunner.StagingDirName
    };

    public sealed class Journal
    {
        public int SchemaVersion { get; set; } = 1;
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string SourcePath { get; set; } = "";
        public string TargetPath { get; set; } = "";
        public string? ImportSourcePath { get; set; }
        public string? OriginalDataPath { get; set; }
        public ImportPathPlan? PathPlan { get; set; }
        public string Phase { get; set; } = "queued";
        public string[] IncomingEntries { get; set; } = [];
    }

    public sealed record Validation(bool Valid, string? Error, string SourcePath, string TargetPath,
        string? ImportSourcePath = null, string? SourceVersion = null, string? OriginalDataPath = null);
    private sealed record Entry(string Path, bool Directory, long Length, long Modified);

    public static Validation Validate(string anchorPath, string sourcePath, string targetPath)
    {
        var source = sourcePath;
        var target = targetPath;
        try
        {
            source = ServerSetupSession.Canonical(sourcePath);
            target = ServerSetupSession.Canonical(targetPath);
            var anchor = ServerSetupSession.Canonical(anchorPath);
            if (Contains(source, target) || Contains(target, source))
                throw new IOException("Source and destination must be different, non-nested data directories.");
            if (Contains(target, anchor))
                throw new IOException("The destination cannot contain the application's data anchor.");
            if (!File.Exists(Path.Combine(source, "app.json")) || !File.Exists(Path.Combine(source, "bakabase_insideworld.db")))
                throw new IOException("The current data directory does not contain an initialized library.");
            if (File.Exists(Path.Combine(source, ServerAppDataImport.MarkerName)) ||
                File.Exists(Path.Combine(source, PendingRelocation.FileName)))
                throw new IOException("Complete or cancel the existing data operation before moving this library.");
            EnsureEmptyTarget(target, allowWork: false);
            return new Validation(true, null, source, target);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            return new Validation(false, e.Message, source, target);
        }
    }

    public static Journal Queue(string anchorPath, string sourcePath, string targetPath)
    {
        lock (Gate)
        {
            if (ReadPending(anchorPath) != null) throw new IOException("A data-directory change is already queued.");
            var validation = Validate(anchorPath, sourcePath, targetPath);
            if (!validation.Valid) throw new IOException(validation.Error);
            var journal = new Journal { SourcePath = validation.SourcePath, TargetPath = validation.TargetPath };
            WriteJournal(anchorPath, journal);
            return journal;
        }
    }

    public static Validation ValidateImport(string anchorPath, string currentPath, string targetPath,
        string importSourcePath, string? originalDataPath = null)
    {
        var placement = Validate(anchorPath, currentPath, targetPath);
        if (!placement.Valid) return placement;
        // The external library must be separate from both the active library and its
        // proposed replacement, including aliases of the active instance's lock file.
        var source = ServerAppDataImport.Validate(importSourcePath, placement.SourcePath, originalDataPath);
        if (!source.Valid) return placement with { Valid = false, Error = source.Error };
        if (Contains(source.SourcePath, ServerSetupSession.Canonical(anchorPath)))
            return placement with { Valid = false, Error = "The import source cannot contain this instance's setup directory." };
        source = ServerAppDataImport.Validate(source.SourcePath, placement.TargetPath, originalDataPath);
        return placement with { Valid = source.Valid, Error = source.Error, ImportSourcePath = source.SourcePath,
            SourceVersion = source.SourceVersion, OriginalDataPath = originalDataPath };
    }

    public static Journal QueueImport(string anchorPath, string currentPath, string targetPath,
        string importSourcePath, string? originalDataPath = null, ImportPathPlan? pathPlan = null)
    {
        lock (Gate)
        {
            if (ReadPending(anchorPath) != null) throw new IOException("A data-directory change is already queued.");
            var validation = ValidateImport(anchorPath, currentPath, targetPath, importSourcePath, originalDataPath);
            if (!validation.Valid) throw new IOException(validation.Error);
            // Version 2 is intentional: an older executable must reject this plan,
            // rather than silently treating it as an ordinary move of the current data.
            SetupImportPreflight.ValidatePlan(pathPlan);
            var journal = new Journal { SchemaVersion = pathPlan == null ? 2 : 3, PathPlan = pathPlan, SourcePath = validation.SourcePath,
                TargetPath = validation.TargetPath, ImportSourcePath = validation.ImportSourcePath,
                OriginalDataPath = originalDataPath };
            WriteJournal(anchorPath, journal);
            return journal;
        }
    }

    public static Journal? ReadPending(string anchorPath)
    {
        var path = Path.Combine(anchorPath, MarkerName);
        RejectLink(path);
        if (!File.Exists(path)) return null;
        var journal = JsonConvert.DeserializeObject<Journal>(File.ReadAllText(path)) ?? throw new IOException("Invalid relocation journal.");
        var combined = (journal.SchemaVersion == 2 && journal.PathPlan == null || journal.SchemaVersion == 3 && journal.PathPlan != null) &&
                       !string.IsNullOrWhiteSpace(journal.ImportSourcePath);
        if ((!combined && (journal.SchemaVersion != 1 || journal.ImportSourcePath != null)) ||
            journal.SchemaVersion == 1 && journal.PathPlan != null ||
            !Guid.TryParseExact(journal.Id, "N", out _) || journal.IncomingEntries == null ||
            (combined ? journal.Phase is not ("queued" or "importing" or "ready")
                : journal.Phase is not ("queued" or "copying" or "installing" or "ready")))
            throw new IOException("Unsupported relocation journal. Restore a backup before starting.");
        SetupImportPreflight.ValidatePlan(journal.PathPlan);
        journal.SourcePath = ServerSetupSession.Canonical(journal.SourcePath);
        journal.TargetPath = ServerSetupSession.Canonical(journal.TargetPath);
        if (Contains(journal.SourcePath, journal.TargetPath) || Contains(journal.TargetPath, journal.SourcePath))
            throw new IOException("The relocation journal has overlapping source and destination paths.");
        if (combined)
        {
            journal.ImportSourcePath = ServerSetupSession.Canonical(journal.ImportSourcePath!);
            if (Contains(journal.SourcePath, journal.ImportSourcePath) || Contains(journal.ImportSourcePath, journal.SourcePath) ||
                Contains(journal.TargetPath, journal.ImportSourcePath) || Contains(journal.ImportSourcePath, journal.TargetPath) ||
                Contains(journal.ImportSourcePath, ServerSetupSession.Canonical(anchorPath)))
                throw new IOException("The import source, current library and destination must be separate, non-nested directories.");
        }
        foreach (var name in journal.IncomingEntries)
            if (name is "" or "." or ".." || name.Contains('/') || name.Contains('\\') || Excludes.Contains(name))
                throw new IOException("The relocation journal contains an invalid root entry.");
        return journal;
    }

    public static void Cancel(string anchorPath)
    {
        lock (Gate)
        {
            var journal = ReadPending(anchorPath);
            if (journal == null) return;
            if (journal.Phase != "queued") throw new IOException("The data-directory change has already begun. Restart to finish it.");
            File.Delete(Path.Combine(anchorPath, MarkerName));
        }
    }

    /// <summary>The caller retains both locks through switching its in-memory data/progress ownership.</summary>
    public static string? ApplyPending(string anchorPath, DataDirectoryLock sourceLock, DataDirectoryLock targetLock,
        Action<string>? log = null, Action<AppDataImportProgressUpdate>? report = null,
        CancellationToken cancellationToken = default)
    {
        var journal = ReadPending(anchorPath);
        if (journal == null) return null;
        RequireLock(sourceLock, journal.SourcePath);
        RequireLock(targetLock, journal.TargetPath);
        cancellationToken.ThrowIfCancellationRequested();
        var source = journal.SourcePath;
        var target = journal.TargetPath;
        var current = AppDataLocator.ResolveEffectiveDataDirectory(anchorPath);
        if (!ServerSetupSession.SameDirectory(current, source) && !ServerSetupSession.SameDirectory(current, target))
            throw new IOException("The anchor changed since relocation was queued. Restore the intended pointer before retrying.");
        var resumedReady = journal.Phase == "ready";
        var work = Path.Combine(target, WorkName);
        var stage = Path.Combine(work, journal.Id);
        RejectLink(work);
        RejectLink(stage);
        long completedBytes = 0, totalBytes = 0;
        var completedFiles = 0;
        var totalFiles = 0;
        void Report(string phase, string? path = null, int done = 0, int total = 0) =>
            report?.Invoke(new AppDataImportProgressUpdate(phase, completedBytes, totalBytes,
                completedFiles, totalFiles, path, done, total));

        if (journal.ImportSourcePath != null && journal.Phase != "ready")
        {
            PrepareImportedTarget(anchorPath, journal, log, update =>
            {
                completedBytes = update.CompletedBytes;
                totalBytes = update.TotalBytes;
                completedFiles = update.CompletedFiles;
                totalFiles = update.TotalFiles;
                report?.Invoke(update);
            }, name => Report("verifying", name), cancellationToken);
        }

        if (journal.Phase is "queued" or "copying")
        {
            EnsureEmptyTarget(target, allowWork: true);
            if (journal.Phase == "queued")
            {
                journal.Phase = "copying";
                WriteJournal(anchorPath, journal);
            }
            Report("scanning");
            log?.Invoke($"Copying AppData to {target}. The original directory will be retained at {source}.");
            AppDataCopyPermissions.RestrictTargetRoot(source, target);
            if (Directory.Exists(stage)) Directory.Delete(stage, true);
            AppDataCopyPermissions.CreatePrivateDirectory(work);
            AppDataCopyPermissions.CreatePrivateDirectory(stage);
            var entries = Snapshot(source, cancellationToken, (path, files, bytes) =>
            {
                totalFiles = files;
                totalBytes = bytes;
                Report("scanning", path);
            });
            var buffer = new byte[1024 * 1024];
            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var original = Path.Combine(source, entry.Path);
                var copied = Path.Combine(stage, entry.Path);
                if (entry.Directory) { AppDataCopyPermissions.CreateDirectory(original, copied); continue; }
                Report("copying", entry.Path);
                using (var input = new FileStream(original, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var output = AppDataCopyPermissions.CreateFile(original, copied))
                {
                    int read;
                    while ((read = input.Read(buffer, 0, buffer.Length)) != 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        output.Write(buffer, 0, read);
                        completedBytes += read;
                        Report("copying", entry.Path);
                    }
                    if (output.Length != entry.Length) throw new IOException("The source changed during copying. Stop every instance and retry.");
                }
                File.SetLastWriteTimeUtc(copied, new DateTime(entry.Modified, DateTimeKind.Utc));
                completedFiles++;
                Report("copying", entry.Path);
            }
            Report("verifying");
            if (!entries.SequenceEqual(Snapshot(source, cancellationToken)))
                throw new IOException("The source changed during copying. Stop every instance and retry.");
            CheckDatabases(stage, cancellationToken, path => Report("verifying", path));
            PrepareOptions(stage, source);
            journal.IncomingEntries = Directory.EnumerateFileSystemEntries(stage).Select(Path.GetFileName).ToArray()!;
            journal.Phase = "installing";
            WriteJournal(anchorPath, journal);
        }

        if (journal.Phase == "installing")
        {
            // Only entries from this journal may be present alongside the staging directory.
            EnsureOwnedTarget(target, journal.IncomingEntries);
            var moved = 0;
            foreach (var name in journal.IncomingEntries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Report("installing", name, moved, journal.IncomingEntries.Length);
                MoveEntry(Path.Combine(stage, name), Path.Combine(target, name));
                Report("installing", name, ++moved, journal.IncomingEntries.Length);
            }
            Report("verifying");
            CheckDatabases(target, cancellationToken, path => Report("verifying", path));
            journal.Phase = "ready";
            WriteJournal(anchorPath, journal);
        }

        cancellationToken.ThrowIfCancellationRequested();
        EnsureOwnedTarget(target, journal.IncomingEntries);
        if (resumedReady) CheckDatabases(target, cancellationToken, path => Report("verifying", path));
        // The callback persists this phase before the capability is copied. The existing
        // mini-server keeps serving the same token while the caller replaces its store.
        Report("starting");
        CopyMonitoringState(current, target);
        cancellationToken.ThrowIfCancellationRequested();
        WriteRedirect(anchorPath, target);
        File.Delete(Path.Combine(anchorPath, MarkerName));
        if (Directory.Exists(stage)) Directory.Delete(stage, false);
        if (Directory.Exists(work) && !Directory.EnumerateFileSystemEntries(work).Any()) Directory.Delete(work, false);
        return target;
    }

    private static void PrepareImportedTarget(string anchorPath, Journal journal, Action<string>? log,
        Action<AppDataImportProgressUpdate>? report, Action<string> verifying, CancellationToken cancellationToken)
    {
        var target = journal.TargetPath;
        if (journal.Phase == "queued")
        {
            EnsureEmptyTarget(target, allowWork: false);
            journal.Phase = "importing";
            // Persist the coordinator first. Every subsequent target write belongs to
            // this operation, and cancellation can no longer discard its recovery record.
            WriteJournal(anchorPath, journal);
        }
        cancellationToken.ThrowIfCancellationRequested();
        var pending = ServerAppDataImport.ReadPending(target);
        var receipt = ServerAppDataImport.ReadCompletedReceipt(target, journal.Id, journal.ImportSourcePath!, journal.OriginalDataPath, journal.PathPlan);
        if (pending != null)
        {
            ServerAppDataImport.RequireMatchingJournal(pending, journal.Id, journal.ImportSourcePath!, target, journal.OriginalDataPath, journal.PathPlan);
        }
        else if (receipt == null)
        {
            // A missing receipt never grants permission to queue over files left by
            // another operation or by an incomplete installation with a lost journal.
            EnsureEmptyTarget(target, allowWork: false);
            ServerAppDataImport.Queue(journal.ImportSourcePath!, target, journal.OriginalDataPath, journal.Id, journal.PathPlan);
            pending = ServerAppDataImport.ReadPending(target)!;
        }
        if (pending != null)
        {
            ServerAppDataImport.ApplyPending(target, log, report, cancellationToken);
            receipt = ServerAppDataImport.ReadCompletedReceipt(target, journal.Id, journal.ImportSourcePath!, journal.OriginalDataPath, journal.PathPlan);
        }
        if (receipt == null) throw new IOException("The imported destination has no completed record. Its data pointer was not changed.");
        cancellationToken.ThrowIfCancellationRequested();
        var importWork = Path.Combine(target, ServerAppDataImport.WorkName);
        RejectLink(importWork);
        // The inner marker is removed just before staging cleanup. A crash in that
        // interval leaves only this operation's empty directory, never a reason to
        // queue again or recursively remove a different operation's working files.
        var importStage = Path.Combine(importWork, journal.Id);
        RejectLink(importStage);
        if (Directory.Exists(importStage)) Directory.Delete(importStage, false);
        if (Directory.Exists(importWork)) Directory.Delete(importWork, false);
        journal.IncomingEntries = receipt.IncomingEntries.Concat(new[] { "backups" }).Distinct(StringComparer.Ordinal).ToArray();
        EnsureOwnedTarget(target, journal.IncomingEntries);
        CheckDatabases(target, cancellationToken, verifying);
        journal.Phase = "ready";
        WriteJournal(anchorPath, journal);
        log?.Invoke($"Imported data is ready at {target}. The previous library is retained at {journal.SourcePath}.");
    }

    private static Entry[] Snapshot(string root, CancellationToken cancellation, Action<string, int, long>? progress = null)
    {
        var entries = new List<Entry>();
        var files = 0;
        long bytes = 0;
        void Visit(string directory)
        {
            foreach (var path in Directory.EnumerateFileSystemEntries(directory).OrderBy(p => p, StringComparer.Ordinal))
            {
                cancellation.ThrowIfCancellationRequested();
                if (directory == root && Excludes.Contains(Path.GetFileName(path))) continue;
                RejectLink(path);
                var relative = Path.GetRelativePath(root, path);
                if (Directory.Exists(path))
                {
                    entries.Add(new Entry(relative, true, 0, 0));
                    Visit(path);
                }
                else
                {
                    var info = new FileInfo(path);
                    entries.Add(new Entry(relative, false, info.Length, info.LastWriteTimeUtc.Ticks));
                    files++;
                    bytes += info.Length;
                }
                progress?.Invoke(relative, files, bytes);
            }
        }
        Visit(root);
        return entries.ToArray();
    }

    private static void PrepareOptions(string target, string source)
    {
        var path = Path.Combine(target, "app.json");
        var document = JObject.Parse(File.ReadAllText(path));
        var options = document.GetValue("App", StringComparison.OrdinalIgnoreCase) switch
        {
            null => document,
            JObject app => app,
            _ => throw new IOException("The App section of app.json must be an object.")
        };
        var roots = ImportedAppDataRoots.Read(target).Concat(new[] { source,
            (string?) options.GetValue("dataPath", StringComparison.OrdinalIgnoreCase),
            (string?) options.GetValue("prevDataPath", StringComparison.OrdinalIgnoreCase) })
            .Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.Ordinal).ToArray();
        foreach (var property in options.Properties().Where(p => p.Name.Equals("dataPath", StringComparison.OrdinalIgnoreCase) ||
                     p.Name.Equals("prevDataPath", StringComparison.OrdinalIgnoreCase) ||
                     p.Name.Equals("wwwRootPath", StringComparison.OrdinalIgnoreCase)).ToArray()) property.Remove();
        options["prevDataPath"] = source;
        File.WriteAllText(path, document.ToString(Formatting.Indented));
        File.WriteAllText(Path.Combine(target, ImportedAppDataRoots.FileName), JsonConvert.SerializeObject(roots));
    }

    private static void CheckDatabases(string target, CancellationToken cancellation, Action<string> progress)
    {
        // Historical backups are preserved verbatim; only the active top-level DBs open.
        foreach (var path in Directory.GetFiles(target, "*.db"))
        {
            cancellation.ThrowIfCancellationRequested();
            RejectLink(path);
            progress(Path.GetFileName(path));
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
                { DataSource = path, Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA integrity_check;";
            if (!string.Equals(command.ExecuteScalar()?.ToString(), "ok", StringComparison.OrdinalIgnoreCase))
                throw new IOException($"SQLite integrity check failed: {Path.GetFileName(path)}");
            cancellation.ThrowIfCancellationRequested();
            command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            command.ExecuteNonQuery();
        }
        if (!File.Exists(Path.Combine(target, "bakabase_insideworld.db"))) throw new IOException("The copied library database is missing.");
    }

    private static void EnsureEmptyTarget(string target, bool allowWork)
    {
        if (Path.GetPathRoot(target) == target || File.Exists(target)) throw new IOException("Choose an empty data directory, not a file or filesystem root.");
        if (!Directory.Exists(target)) return;
        foreach (var path in Directory.EnumerateFileSystemEntries(target))
        {
            RejectLink(path);
            var name = Path.GetFileName(path);
            if (name != DataDirectoryLock.FileName && !(allowWork && name == WorkName))
                throw new IOException("The destination must be empty. Existing files will not be merged or overwritten.");
        }
    }

    private static void EnsureOwnedTarget(string target, string[] entries)
    {
        foreach (var path in Directory.EnumerateFileSystemEntries(target))
        {
            RejectLink(path);
            var name = Path.GetFileName(path);
            if (name != DataDirectoryLock.FileName && name != WorkName && name != ImportProgressStore.FileName &&
                name != ImportProgressStore.FileName + ".tmp" && !entries.Contains(name, StringComparer.Ordinal))
                throw new IOException($"Unexpected destination entry during relocation: {name}");
        }
    }

    private static void RequireLock(DataDirectoryLock value, string path)
    {
        if (!value.IsHeld || !ServerSetupSession.SameDirectory(value.Directory, path))
            throw new IOException($"Relocation requires the caller to hold the data lock for {path}.");
    }

    private static void MoveEntry(string source, string target)
    {
        RejectLink(source);
        RejectLink(target);
        var fromExists = File.Exists(source) || Directory.Exists(source);
        var toExists = File.Exists(target) || Directory.Exists(target);
        if (toExists && !fromExists) return;
        if (toExists) throw new IOException($"Relocation entry conflicts with {target}.");
        if (Directory.Exists(source)) Directory.Move(source, target);
        else if (File.Exists(source)) File.Move(source, target);
        else throw new IOException($"Relocation entry is missing: {source}");
    }

    private static bool Contains(string parent, string child)
    {
        // Canonical resolves links, but preserves spelling on case-insensitive volumes.
        // Be conservative about overlap on macOS: a differently cased parent must not
        // allow staging inside the source. Lock ownership still uses exact Unix paths.
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(parent, child, comparison) ||
               child.StartsWith(Path.TrimEndingDirectorySeparator(parent) + Path.DirectorySeparatorChar, comparison);
    }

    private static void CopyMonitoringState(string source, string target)
    {
        // In a replay after the redirect committed, the active store already writes here.
        // Do not race its heartbeat on the same .tmp file or overwrite its newer snapshot.
        if (ServerSetupSession.SameDirectory(source, target)) return;
        var input = Path.Combine(source, ImportProgressStore.FileName);
        RejectLink(input);
        if (!File.Exists(input)) throw new IOException("Relocation monitoring state is missing.");
        WritePrivate(Path.Combine(target, ImportProgressStore.FileName), File.ReadAllText(input));
    }

    private static void WriteJournal(string anchor, Journal journal) =>
        WritePrivate(Path.Combine(anchor, MarkerName), JsonConvert.SerializeObject(journal, Formatting.Indented));

    private static void WriteRedirect(string anchor, string target)
    {
        var path = AnchorRedirect.GetRedirectPath(anchor);
        RejectLink(path);
        RejectLink(path + ".tmp");
        File.WriteAllText(path + ".tmp", target);
        File.Move(path + ".tmp", path, true);
    }

    private static void WritePrivate(string path, string content)
    {
        RejectLink(path);
        RejectLink(path + ".tmp");
        File.Delete(path + ".tmp");
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using (var stream = new FileStream(path + ".tmp", options))
        using (var writer = new StreamWriter(stream)) writer.Write(content);
        File.Move(path + ".tmp", path, true);
    }

    private static void RejectLink(string path)
    {
        if (new FileInfo(path).LinkTarget != null || new DirectoryInfo(path).LinkTarget != null)
            throw new IOException($"Symbolic links are not supported during AppData relocation: {path}");
    }
}
