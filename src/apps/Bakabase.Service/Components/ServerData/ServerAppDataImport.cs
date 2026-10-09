using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Infrastructures.Components.App.Relocation;
using Bakabase.Infrastructures.Components.App.SingleInstance;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Semver;

namespace Bakabase.Service.Components.ServerData;

/// <summary>
/// Import into a fixed data volume, before logging, options or SQLite open it. The source
/// is read only. A journal makes root-entry moves resumable, including on a bind mount
/// whose root itself cannot be renamed. Never start the host after an incomplete import.
/// </summary>
public static class ServerAppDataImport
{
    public const string MarkerName = ".bakabase-import.json";
    public const string WorkName = ".bakabase-import-work";
    public const string BackupsName = "backups/appdata-imports";
    private static readonly object Gate = new();
    private static readonly HashSet<string> Excludes = new(StringComparer.OrdinalIgnoreCase)
    {
        MarkerName, MarkerName + ".tmp", WorkName, SetupProcessCoordinator.LockFileName, "backups",
        ".bakabase-server-setup.json", ".bakabase-server-setup.json.tmp",
        ".bakabase-import-status.json", ".bakabase-import-status.json.tmp",
        SetupImportDraftStore.FileName, SetupImportDraftStore.FileName + ".tmp",
        ".bakabase-relocate.json", ".bakabase-relocate.json.tmp", ".bakabase-relocate-work",
        DataDirectoryLock.FileName, AnchorRedirect.FileName, PendingRelocation.FileName,
        PendingRelocationRunner.StagingDirName
    };

    public sealed class Validation
    {
        public bool Valid { get; set; }
        public string? Error { get; set; }
        public string SourcePath { get; set; } = "";
        public string CurrentPath { get; set; } = "";
        public string? SourceVersion { get; set; }
        public string? OriginalDataPath { get; set; }
    }

    public sealed class Journal
    {
        public int SchemaVersion { get; set; } = 1;
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string SourcePath { get; set; } = "";
        public string? TargetPath { get; set; }
        public string? OriginalDataPath { get; set; }
        public string Phase { get; set; } = "queued";
        public ImportPathPlan? PathPlan { get; set; }
        public string[] ExistingEntries { get; set; } = [];
        public string[] IncomingEntries { get; set; } = [];
    }

    // Do not touch AppService here: its static constructor already opens the data directory.
    internal static SemVersion RunningVersion => SemVersion.Parse(
        typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
            .InformationalVersion, SemVersionStyles.Any);

    public static Validation Validate(string sourcePath, string currentPath, string? originalDataPath = null)
    {
        var result = new Validation { SourcePath = sourcePath, CurrentPath = currentPath, OriginalDataPath = originalDataPath };
        try
        {
            if (!string.IsNullOrWhiteSpace(originalDataPath))
            {
                var normalized = originalDataPath.Replace('\\', '/');
                if (!(normalized.StartsWith('/') || (normalized.Length >= 3 && char.IsLetter(normalized[0]) && normalized[1..3] == ":/")) ||
                    normalized.TrimEnd('/').Length == 0 || normalized.TrimEnd('/').Length == 2 && normalized[1] == ':')
                    throw new IOException("The original appdata path must be an absolute directory, not a drive root.");
            }
            var source = CanonicalDirectory(sourcePath);
            var current = CanonicalDirectory(currentPath);
            // An anchor may contain only a redirect. Do not mutate the old layout to resolve it.
            var redirect = AnchorRedirect.TryRead(source);
            if (redirect != null) source = CanonicalDirectory(redirect);
            if (!Directory.Exists(source)) throw new IOException("The source directory does not exist on the server.");
            if (Contains(source, current) || Contains(current, source))
                throw new IOException("Source and destination must be separate directories, without containment.");
            // The destination is locked while this API is available. Checking the source
            // lock also detects the same host directory bind-mounted under another path.
            using var sourceLock = OpenSourceLock(source);
            var appPath = Path.Combine(source, "app.json");
            RejectLink(appPath);
            var options = AppSection(JObject.Parse(File.ReadAllText(appPath)));
            // Older anchors stored DataPath in app.json. Select the effective directory instead.
            var legacyPath = (string?) options.GetValue("dataPath", StringComparison.OrdinalIgnoreCase);
            if (!File.Exists(Path.Combine(source, "bakabase_insideworld.db")) && !string.IsNullOrWhiteSpace(legacyPath))
                throw new IOException($"Select the actual data directory, or mount it in the container: {legacyPath}");
            RejectLink(Path.Combine(source, "bakabase_insideworld.db"));
            if (!File.Exists(Path.Combine(source, "bakabase_insideworld.db")))
                throw new IOException("The source must contain app.json and bakabase_insideworld.db.");
            var version = (string?) options.GetValue("version", StringComparison.OrdinalIgnoreCase);
            if (!SemVersion.TryParse(version, SemVersionStyles.Any, out var parsed))
                throw new IOException("The source app.json has no valid version; its upgrade path cannot be determined.");
            if (SemVersion.ComparePrecedence(parsed, RunningVersion) > 0)
                throw new IOException($"Source version {version} is newer than this server ({RunningVersion}). Upgrade the server first.");
            ValidateContainerAccess(source,
                string.Equals(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"), "true", StringComparison.OrdinalIgnoreCase));
            if (File.Exists(Path.Combine(source, PendingRelocation.FileName)) ||
                File.Exists(Path.Combine(source, ServerAppDataRelocation.MarkerName)) || File.Exists(Path.Combine(source, MarkerName)))
                throw new IOException("The source has an unfinished data move/import. Complete or cancel it first.");
            result.SourcePath = source;
            result.CurrentPath = current;
            result.SourceVersion = version;
            result.Valid = true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or JsonException or InvalidOperationException)
        {
            result.Error = e.Message;
        }
        return result;
    }

    internal static void ValidateContainerAccess(string source, bool inContainer)
    {
        if (!inContainer) return;
        var configs = Path.Combine(source, "configs");
        var path = Path.Combine(configs, "remote-access.json");
        if (!File.Exists(path)) return;
        RejectLink(configs);
        RejectLink(path);
        var document = JObject.Parse(File.ReadAllText(path));
        var options = document.GetValue("RemoteAccess", StringComparison.OrdinalIgnoreCase) as JObject ?? document;
        var mode = options.GetValue("mode", StringComparison.OrdinalIgnoreCase);
        if (mode == null || mode.Type == JTokenType.Null) return;
        if (!Enum.TryParse<RemoteAccessMode>(mode.ToString(), true, out var parsed) || !Enum.IsDefined(parsed))
            throw new IOException("The source remote-access.json contains an invalid mode.");
        if (parsed == RemoteAccessMode.Disabled)
            throw new IOException("The source explicitly disables remote access, which would block the Docker browser after import. " +
                                  "Import a separate copy into the native server first and choose the remote access/pairing settings you need, " +
                                  "then stop it and import that prepared copy into Docker. The original source has not been changed.");
    }

    public static Journal? ReadPending(string currentPath)
    {
        var marker = Path.Combine(currentPath, MarkerName);
        if (!File.Exists(marker)) return null;
        RejectLink(marker);
        var journal = JsonConvert.DeserializeObject<Journal>(File.ReadAllText(marker))
                      ?? throw new IOException("Invalid import journal.");
        if (!(journal.SchemaVersion == 1 && journal.PathPlan == null || journal.SchemaVersion == 2 && journal.PathPlan != null) ||
            !Guid.TryParseExact(journal.Id, "N", out _) ||
            journal.Phase is not ("queued" or "backing-up" or "installing"))
            throw new IOException("Unsupported import journal. Restore a backup before starting the server.");
        SetupImportPreflight.ValidatePlan(journal.PathPlan);
        foreach (var entry in journal.ExistingEntries.Concat(journal.IncomingEntries))
            if (entry is "" or "." or ".." || entry.Contains('/') || entry.Contains('\\') || Excludes.Contains(entry))
                throw new IOException("Invalid entry in import journal.");
        return journal;
    }

    public static void Queue(string sourcePath, string currentPath, string? originalDataPath = null, string? operationId = null,
        ImportPathPlan? pathPlan = null)
    {
        lock (Gate)
        {
            if (ReadPending(currentPath) != null) throw new IOException("An import is already pending. Cancel it first.");
            if (File.Exists(Path.Combine(currentPath, PendingRelocation.FileName)))
                throw new IOException("Cancel the pending data-path relocation first.");
            var validation = Validate(sourcePath, currentPath, originalDataPath);
            if (!validation.Valid) throw new IOException(validation.Error);
            if (operationId != null && !Guid.TryParseExact(operationId, "N", out _))
                throw new ArgumentException("The import operation identifier is invalid.", nameof(operationId));
            SetupImportPreflight.ValidatePlan(pathPlan);
            WriteJournal(currentPath, new Journal { Id = operationId ?? Guid.NewGuid().ToString("N"),
                SchemaVersion = pathPlan == null ? 1 : 2, PathPlan = pathPlan,
                SourcePath = validation.SourcePath, TargetPath = validation.CurrentPath, OriginalDataPath = originalDataPath });
        }
    }

    internal static void RequireMatchingJournal(Journal journal, string id, string sourcePath, string targetPath,
        string? originalDataPath, ImportPathPlan? pathPlan = null)
    {
        if (journal.SchemaVersion != (pathPlan == null ? 1 : 2) || !MatchingPlan(journal.PathPlan, pathPlan) || journal.Id != id || journal.TargetPath == null ||
            !ServerSetupSession.SameDirectory(journal.SourcePath, sourcePath) ||
            !ServerSetupSession.SameDirectory(journal.TargetPath, targetPath) ||
            !string.Equals(journal.OriginalDataPath, originalDataPath, StringComparison.Ordinal))
            throw new IOException("The destination contains an import record belonging to a different operation. No files were replaced.");
    }

    private static bool MatchingPlan(ImportPathPlan? left, ImportPathPlan? right) =>
        left == null ? right == null : right != null && left.SourceFingerprint == right.SourceFingerprint &&
            left.DraftId == right.DraftId && left.Rules.SequenceEqual(right.Rules);

    internal static Journal? ReadCompletedReceipt(string targetPath, string id, string sourcePath, string? originalDataPath,
        ImportPathPlan? pathPlan = null)
    {
        var backups = Path.Combine(targetPath, BackupsName);
        RejectLink(Path.Combine(targetPath, "backups"));
        RejectLink(backups);
        var receipt = Path.Combine(backups, id + ".json");
        RejectLink(receipt);
        if (!File.Exists(receipt)) return null;
        var journal = JsonConvert.DeserializeObject<Journal>(File.ReadAllText(receipt))
                      ?? throw new IOException("The completed import record is invalid.");
        SetupImportPreflight.ValidatePlan(journal.PathPlan);
        RequireMatchingJournal(journal, id, sourcePath, targetPath, originalDataPath, pathPlan);
        if (journal.Phase != "installing" || journal.IncomingEntries == null || journal.ExistingEntries == null ||
            !journal.IncomingEntries.Contains("app.json", StringComparer.Ordinal) ||
            !journal.IncomingEntries.Contains("bakabase_insideworld.db", StringComparer.Ordinal))
            throw new IOException("The completed import record does not describe a finished library.");
        foreach (var name in journal.IncomingEntries.Concat(journal.ExistingEntries))
            if (name is "" or "." or ".." || name.Contains('/') || name.Contains('\\') || Excludes.Contains(name))
                throw new IOException("The completed import record contains an invalid entry.");
        return journal;
    }

    public static void Cancel(string currentPath)
    {
        lock (Gate)
        {
            if (ReadPending(currentPath) is { Phase: not "queued" })
                throw new IOException("An import already started; restart to finish it before continuing.");
            File.Delete(Path.Combine(currentPath, MarkerName));
        }
    }

    /// <summary>The caller must own the destination's DataDirectoryLock for the entire run.</summary>
    public static void ApplyPending(string currentPath, Action<string>? progress = null,
        Action<AppDataImportProgressUpdate>? report = null, CancellationToken cancellationToken = default)
    {
        var journal = ReadPending(currentPath);
        if (journal == null) return;
        cancellationToken.ThrowIfCancellationRequested();
        var work = Path.Combine(currentPath, WorkName);
        var backups = Path.Combine(currentPath, BackupsName);
        RejectLink(work);
        RejectLink(Path.Combine(currentPath, "backups"));
        RejectLink(backups);
        var stage = Path.Combine(work, journal.Id);
        var backup = Path.Combine(backups, journal.Id);
        RejectLink(stage);
        RejectLink(backup);
        long completedBytes = 0, totalBytes = 0;
        var completedFiles = 0;
        var totalFiles = 0;

        void Report(string phase, string? currentFile = null, int completedEntries = 0, int totalEntries = 0) =>
            report?.Invoke(new AppDataImportProgressUpdate(phase, completedBytes, totalBytes,
                completedFiles, totalFiles, currentFile, completedEntries, totalEntries));

        if (journal.Phase == "queued")
        {
            Report("scanning");
            var validation = Validate(journal.SourcePath, currentPath, journal.OriginalDataPath);
            if (!validation.Valid) throw new IOException(validation.Error);
            var source = validation.SourcePath;
            progress?.Invoke($"Copying appdata from {source}. The source must remain stopped until import completes.");
            // Recent desktop builds hold this file exclusively. Open existing locks read-only
            // so a read-only /import mount works and no owner metadata in the source changes.
            using var sourceLock = OpenSourceLock(source);
            if (journal.PathPlan is { } pathPlan &&
                SetupImportPreflight.Fingerprint(source, cancellationToken) != pathPlan.SourceFingerprint)
                throw new IOException("The import source changed after preflight. No current data was replaced. Cancel this queued import, reopen Setup and scan the source again.");
            AppDataCopyPermissions.RestrictTargetRoot(source, currentPath);
            if (Directory.Exists(stage)) Directory.Delete(stage, true);
            AppDataCopyPermissions.CreatePrivateDirectory(work);
            AppDataCopyPermissions.CreatePrivateDirectory(stage);
            var files = Snapshot(source, (path, count, bytes) =>
            {
                totalFiles = count;
                totalBytes = bytes;
                Report("scanning", path);
            }, cancellationToken);
            Report("copying");
            var buffer = new byte[1024 * 1024];
            foreach (var entry in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Report("copying", entry.Path);
                var target = Path.Combine(stage, entry.Path);
                var original = Path.Combine(source, entry.Path);
                if (entry.Directory)
                {
                    AppDataCopyPermissions.CreateDirectory(original, target);
                    continue;
                }
                using (var input = new FileStream(original, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var output = AppDataCopyPermissions.CreateFile(original, target))
                {
                    while (true)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var read = input.Read(buffer, 0, buffer.Length);
                        if (read == 0) break;
                        output.Write(buffer, 0, read);
                        completedBytes += read;
                        Report("copying", entry.Path);
                    }
                    if (output.Length != entry.Length) throw new IOException("The source changed while copying. Stop the old instance and retry.");
                }
                File.SetLastWriteTimeUtc(target, entry.Modified);
                completedFiles++;
                Report("copying", entry.Path);
            }
            Report("verifying");
            if (!files.SequenceEqual(Snapshot(source, (path, _, _) => Report("verifying", path), cancellationToken)))
                throw new IOException("The source changed while copying. Stop the old instance and retry.");
            if (journal.PathPlan is { } reviewed &&
                SetupImportPreflight.Fingerprint(stage, cancellationToken) != reviewed.SourceFingerprint)
                throw new IOException("The copied data differs from the reviewed source. No current data was replaced. Scan the source again.");
            CheckDatabases(stage, (path, completed, total) => Report("verifying", path, completed, total), cancellationToken);
            if (journal.PathPlan is { Rules.Length: > 0 } mapped)
            {
                Report("mapping");
                AppDataPathMapping.Apply(stage, mapped.Rules, cancellationToken, update =>
                    Report("mapping", update.Table == null ? update.File : update.File + ":" + update.Table));
                CheckDatabases(stage, (path, completed, total) => Report("verifying", path, completed, total), cancellationToken);
            }
            Report("verifying", "app.json");
            PrepareOptions(stage, source, journal.OriginalDataPath);
            foreach (var entry in RootEntries(currentPath)) RejectLink(entry);
            journal.ExistingEntries = RootEntries(currentPath).Select(Path.GetFileName).ToArray()!;
            journal.IncomingEntries = RootEntries(stage).Select(Path.GetFileName).ToArray()!;
            journal.Phase = "backing-up";
            WriteJournal(currentPath, journal);
        }

        if (journal.Phase == "backing-up")
        {
            progress?.Invoke($"Backing up current appdata to {backup}.");
            Report("backing-up", totalEntries: journal.ExistingEntries.Length);
            AppDataCopyPermissions.CreatePrivateDirectory(backups);
            AppDataCopyPermissions.CreatePrivateDirectory(backup);
            var completedEntries = 0;
            foreach (var name in journal.ExistingEntries)
            {
                Report("backing-up", name, completedEntries, journal.ExistingEntries.Length);
                cancellationToken.ThrowIfCancellationRequested();
                MoveEntry(Path.Combine(currentPath, name), Path.Combine(backup, name));
                Report("backing-up", name, ++completedEntries, journal.ExistingEntries.Length);
            }
            journal.Phase = "installing";
            WriteJournal(currentPath, journal);
        }

        progress?.Invoke("Installing and checking imported appdata.");
        Report("installing", totalEntries: journal.IncomingEntries.Length);
        var installedEntries = 0;
        foreach (var name in journal.IncomingEntries)
        {
            Report("installing", name, installedEntries, journal.IncomingEntries.Length);
            cancellationToken.ThrowIfCancellationRequested();
            MoveEntry(Path.Combine(stage, name), Path.Combine(currentPath, name));
            Report("installing", name, ++installedEntries, journal.IncomingEntries.Length);
        }
        Report("verifying");
        CheckDatabases(currentPath, (path, completed, total) => Report("verifying", path, completed, total), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        RejectLink(backup + ".json");
        RejectLink(backup + ".json.tmp");
        File.WriteAllText(backup + ".json.tmp", JsonConvert.SerializeObject(journal, Formatting.Indented));
        File.Move(backup + ".json.tmp", backup + ".json", true);
        File.Delete(Path.Combine(currentPath, MarkerName));
        if (Directory.Exists(stage)) Directory.Delete(stage, true);
        progress?.Invoke($"Import complete. Previous server appdata is preserved at {backup}.");
        Report("starting");
    }

    private sealed record FileSnapshot(string Path, bool Directory, long Length, DateTime Modified);

    internal static FileStream? OpenSourceLock(string source)
    {
        var path = Path.Combine(source, DataDirectoryLock.FileName);
        RejectLink(path);
        try
        {
            return File.Exists(path) ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None) : null;
        }
        catch (IOException e)
        {
            throw new IOException("The source is in use or aliases this server's data directory. Stop the old instance and use separate source and destination directories.", e);
        }
    }

    private static List<FileSnapshot> Snapshot(string root, Action<string?, int, long>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new List<FileSnapshot>();
        var reportingInterval = Stopwatch.StartNew();
        var fileCount = 0;
        long totalBytes = 0;
        foreach (var entry in RootEntries(root)) Visit(entry);
        progress?.Invoke(result.LastOrDefault()?.Path, fileCount, totalBytes);
        return result.OrderBy(x => x.Path, StringComparer.Ordinal).ToList();

        void Visit(string entry)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RejectLink(entry);
            var relativePath = Path.GetRelativePath(root, entry);
            if (Directory.Exists(entry))
            {
                result.Add(new FileSnapshot(relativePath, true, 0, default));
                foreach (var child in Directory.EnumerateFileSystemEntries(entry)) Visit(child);
            }
            else
            {
                var info = new FileInfo(entry);
                result.Add(new FileSnapshot(relativePath, false, info.Length, info.LastWriteTimeUtc));
                fileCount++;
                totalBytes += info.Length;
                if (progress != null && reportingInterval.ElapsedMilliseconds >= 200)
                {
                    progress(relativePath, fileCount, totalBytes);
                    reportingInterval.Restart();
                }
            }
        }
    }

    private static IEnumerable<string> RootEntries(string root) =>
        Directory.EnumerateFileSystemEntries(root).Where(p => !Excludes.Contains(Path.GetFileName(p)));

    private static void PrepareOptions(string stage, string source, string? originalDataPath)
    {
        var path = Path.Combine(stage, "app.json");
        var document = JObject.Parse(File.ReadAllText(path));
        var options = AppSection(document);
        var roots = ImportedAppDataRoots.Read(stage).Concat(new[]
        {
            source,
            originalDataPath,
            (string?) options.GetValue("dataPath", StringComparison.OrdinalIgnoreCase),
            (string?) options.GetValue("prevDataPath", StringComparison.OrdinalIgnoreCase)
        }).Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var property in options.Properties().Where(p =>
                     p.Name.Equals("dataPath", StringComparison.OrdinalIgnoreCase) ||
                     p.Name.Equals("prevDataPath", StringComparison.OrdinalIgnoreCase) ||
                     p.Name.Equals("wwwRootPath", StringComparison.OrdinalIgnoreCase)).ToArray()) property.Remove();
        options["prevDataPath"] = source;
        File.WriteAllText(path, document.ToString(Formatting.Indented));
        File.WriteAllText(Path.Combine(stage, ImportedAppDataRoots.FileName), JsonConvert.SerializeObject(roots));
    }

    private static JObject AppSection(JObject document) =>
        document.GetValue("App", StringComparison.OrdinalIgnoreCase) switch
        {
            null => document, // Legacy flat options are still accepted.
            JObject app => app,
            _ => throw new IOException("The App section of app.json must be an object.")
        };

    private static void CheckDatabases(string root, Action<string, int, int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // Open only the copy. Recover/checkpoint copied WAL files before any entry is moved.
        var databases = Directory.GetFiles(root, "*.db");
        for (var index = 0; index < databases.Length; index++)
        {
            var db = databases[index];
            progress?.Invoke(Path.GetRelativePath(root, db), index, databases.Length);
            cancellationToken.ThrowIfCancellationRequested();
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = db, Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA integrity_check;";
            if (!string.Equals(command.ExecuteScalar()?.ToString(), "ok", StringComparison.OrdinalIgnoreCase))
                throw new IOException($"SQLite integrity check failed: {Path.GetFileName(db)}");
            cancellationToken.ThrowIfCancellationRequested();
            command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            command.ExecuteNonQuery();
            progress?.Invoke(Path.GetRelativePath(root, db), index + 1, databases.Length);
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(Path.Combine(root, "bakabase_insideworld.db")))
            throw new IOException("Imported library database is missing.");
    }

    private static void MoveEntry(string from, string to)
    {
        RejectLink(from);
        RejectLink(to);
        if (File.Exists(to) || Directory.Exists(to))
        {
            if (File.Exists(from) || Directory.Exists(from)) throw new IOException($"Import move conflicts with {to}.");
            return; // Already moved before the process was interrupted.
        }
        if (Directory.Exists(from)) Directory.Move(from, to);
        else if (File.Exists(from)) File.Move(from, to);
        else throw new IOException($"Import entry is missing: {from}");
    }

    private static void WriteJournal(string root, Journal journal)
    {
        var marker = Path.Combine(root, MarkerName);
        RejectLink(marker);
        RejectLink(marker + ".tmp");
        File.WriteAllText(marker + ".tmp", JsonConvert.SerializeObject(journal, Formatting.Indented));
        File.Move(marker + ".tmp", marker, true);
    }

    private static void RejectLink(string path)
    {
        if (new FileInfo(path).LinkTarget != null || new DirectoryInfo(path).LinkTarget != null)
            throw new IOException($"Symbolic links are not supported during appdata import: {path}");
    }

    private static bool Contains(string parent, string child) =>
        string.Equals(parent, child, StringComparison.OrdinalIgnoreCase) ||
        child.StartsWith(Path.TrimEndingDirectorySeparator(parent) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static string CanonicalDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new IOException("Use an absolute directory path on the server.");
        var full = Path.GetFullPath(path);
        var resolved = Path.GetPathRoot(full)!;
        foreach (var segment in full[resolved.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            resolved = Path.Combine(resolved, segment);
            var info = new DirectoryInfo(resolved);
            if (info.LinkTarget != null) resolved = CanonicalDirectory(info.ResolveLinkTarget(true)!.FullName);
        }
        return Path.TrimEndingDirectorySeparator(resolved);
    }
}
