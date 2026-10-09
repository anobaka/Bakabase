using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Infrastructures.Components.App.SingleInstance;
using Semver;

namespace Bakabase.Service.Components.ServerData;

/// <summary>Capability-scoped setup, before AppService or any application database is opened.</summary>
public sealed class ServerSetupSession : IDisposable
{
    public const string FileName = ".bakabase-server-setup.json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] ControlFiles = [DataDirectoryLock.FileName, SetupProcessCoordinator.LockFileName, FileName, FileName + ".tmp",
        AnchorRedirect.FileName, AnchorRedirect.FileName + ".tmp", ImportProgressStore.FileName,
        ImportProgressStore.FileName + ".tmp", ServerAppDataImport.MarkerName, ServerAppDataImport.MarkerName + ".tmp",
        ServerAppDataImport.WorkName, SetupImportDraftStore.FileName, SetupImportDraftStore.FileName + ".tmp"];
    public static ServerSetupSession? Current { get; set; }
    internal static readonly object OperationGate = new();

    public sealed class SetupRequest
    {
        public string? TargetPath { get; set; }
        public string? SourcePath { get; set; }
        public string? OriginalDataPath { get; set; }
        public string? Operation { get; set; }
        public string? PathPreflightId { get; set; }
        public string? PathPreviewId { get; set; }
        public PathMappingRule[]? PathMappings { get; set; }
    }

    public sealed record SetupStatus(string Mode, string CurrentPath, bool CanChooseTargetPath, bool IsDocker,
        bool Submitted, string? MonitorToken, ImportProgressSnapshot? Progress, bool CanBrowse = false,
        string[]? AllowedOperations = null, bool CanBrowseDirectories = true, bool AutomaticMaintenance = false);
    public sealed record Validation(bool Valid, string? Error, string TargetPath, string? SourcePath = null,
        string? SourceVersion = null, string? OriginalDataPath = null);
    public sealed record CommitResult(bool RequiresRestart, string? MonitorToken, ImportProgressSnapshot? Progress, string? MonitorUrl = null);
    public sealed record SetupSelection(string DataPath, DataDirectoryLock DataLock);
    private sealed record Saved(string Token, bool Submitted = false, string? SelectedTarget = null,
        bool RedirectPending = false);

    private readonly object _gate = new();
    private readonly string _anchor;
    private readonly string _currentPath;
    private readonly bool _pathFixed;
    private readonly bool _isDocker;
    private readonly DataDirectoryLock? _originalLock;
    private readonly ImportProgressStore? _monitoring;
    private ImportProgressStore? _selectedMonitoring;
    private readonly DateTimeOffset _created = DateTimeOffset.UtcNow;
    private readonly TaskCompletionSource<SetupSelection> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Saved _saved;
    private SetupSelection? _selection;
    private CommitResult? _result;
    private bool _transferred;
    private bool _continueAfterResponse = true;
    private bool _commitSignaled;
    private bool _disposed;
    private SetupImportPreflight? _preflight;

    public string Mode { get; }
    public string Token { get { lock (_gate) return _saved.Token; } }
    public bool Submitted { get { lock (_gate) return _saved.Submitted; } }
    public ImportProgressStore? Monitoring { get { lock (_gate) return _selectedMonitoring ?? _monitoring; } }
    public Task<SetupSelection> Completion => _completion.Task;

    public ServerSetupSession(string anchorPath, string currentPath, bool pathFixed, DataDirectoryLock currentLock,
        bool isDocker = false)
    {
        _anchor = Canonical(anchorPath);
        _currentPath = Canonical(currentPath);
        _pathFixed = pathFixed || isDocker;
        _isDocker = isDocker;
        _originalLock = currentLock;
        if (!currentLock.IsHeld || !SameDirectory(currentLock.Directory, _currentPath))
            throw new InvalidOperationException("First-run setup must own the current data directory lock.");
        Mode = "first-run";
        _saved = ReadSaved(_anchor) ?? new Saved(NewToken());
        // Persist before exposing the listener: possession of the startup link is required
        // even if this machine listens beyond loopback.
        Persist(_anchor, _saved);
    }

    private ServerSetupSession(string currentPath, ImportProgressStore monitoring, string mode, string? anchorPath = null)
    {
        _currentPath = Canonical(currentPath);
        _anchor = Canonical(anchorPath ?? currentPath);
        _pathFixed = InContainer || AppDataLocator.IsEnvironmentOverride;
        _isDocker = InContainer;
        _monitoring = monitoring;
        _saved = new Saved(NewToken());
        Mode = mode;
    }

    public static ServerSetupSession ForImport(string currentPath, ImportProgressStore monitoring, string? anchorPath = null) =>
        new(currentPath, monitoring, "import", anchorPath);

    public static ServerSetupSession ForRelocation(string anchorPath, string currentPath, ImportProgressStore monitoring)
    {
        if (InContainer || AppDataLocator.IsEnvironmentOverride)
            throw new IOException("This data path is fixed by the deployment. Change its mount or BAKABASE_DATA_DIR instead.");
        return new(currentPath, monitoring, "relocate", anchorPath);
    }

    public static bool InContainer => string.Equals(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"),
        "true", StringComparison.OrdinalIgnoreCase);

    public SetupStatus Read()
    {
        lock (_gate)
            return new SetupStatus(Mode, _currentPath, !_pathFixed, _isDocker,
                _saved.Submitted, _result?.MonitorToken ?? _monitoring?.Token, _result?.Progress ?? _monitoring?.Read(),
                AllowedOperations: AllowedOperations(), AutomaticMaintenance: SetupProcessCoordinator.IsCoordinator || SetupChildConnection.Current != null);
    }

    public bool Authorize(string? token)
    {
        lock (_gate) return IsAuthorized(token);
    }

    public SetupDraftResult ReadDraft(string? token)
    {
        lock (_gate) { RequireAuthorization(token); return SetupImportDraftStore.Read(_anchor); }
    }

    public SetupDraftResult SaveDraft(SetupDraftRequest request, string? token)
    {
        lock (_gate) { RequireAuthorization(token); return SetupImportDraftStore.Save(_anchor, request); }
    }

    public SetupDraftResult ClearDraft(string? token)
    {
        lock (_gate)
        {
            RequireAuthorization(token);
            SetupImportDraftStore.Clear(_anchor);
            _preflight?.Dispose(); _preflight = null;
            return new(null);
        }
    }

    public SetupPreflightStatus StartPreflight(SetupRequest request, string? token, bool force = false)
    {
        lock (_gate)
        {
            RequireAuthorization(token);
            var selection = SetupImportDraftStore.SelectionOnly(request);
            if (Operation(selection) != "import") throw new IOException("Path preflight is available for imports only.");
            var validation = ValidateCore(selection);
            if (!validation.Valid) throw new IOException(validation.Error);
            selection.Operation = "import"; selection.SourcePath = validation.SourcePath; selection.TargetPath = validation.TargetPath;
            return (_preflight ??= new SetupImportPreflight(_anchor)).Start(selection, force);
        }
    }

    public SetupPreflightStatus ReadPreflight(string? token)
    {
        lock (_gate) { RequireAuthorization(token); return _preflight?.Read() ?? new(); }
    }

    public SetupPathTree ReadPathTree(string scanId, string? parent, int offset, string? token)
    {
        lock (_gate)
        {
            RequireAuthorization(token);
            return (_preflight ?? throw new IOException("Start the import preflight first.")).Tree(scanId, parent, offset);
        }
    }

    public SetupPathPreview PreviewPaths(SetupPreviewRequest request, string? token)
    {
        SetupImportPreflight preflight;
        lock (_gate)
        {
            RequireAuthorization(token);
            preflight = _preflight ?? throw new IOException("Start the import preflight first.");
        }
        var result = preflight.Preview(request);
        lock (_gate) RequireAuthorization(token);
        return result;
    }

    private ImportPathPlan? PathPlan(SetupRequest request, Validation validation)
    {
        if (request.PathPreflightId == null && request.PathPreviewId == null && (request.PathMappings?.Length ?? 0) == 0) return null;
        if (Operation(request) != "import") throw new IOException("Path mappings are available for imports only.");
        var normalized = new SetupRequest { Operation = "import", SourcePath = validation.SourcePath,
            TargetPath = validation.TargetPath, OriginalDataPath = request.OriginalDataPath,
            PathPreflightId = request.PathPreflightId, PathPreviewId = request.PathPreviewId, PathMappings = request.PathMappings };
        return (_preflight ?? throw new IOException("Complete the import preflight first.")).RequirePlan(normalized);
    }

    public ServerSetupDirectoryBrowser.Result BrowseDirectories(string? token, string? path, string? newFolderName,
        System.Threading.CancellationToken cancellationToken = default)
    {
        lock (_gate) RequireAuthorization(token);
        // A single network filesystem call can exceed the listing's scan budget. Do
        // not hold up commit/disposal, and never return a result after revocation.
        var result = ServerSetupDirectoryBrowser.Read(_currentPath, path, newFolderName, cancellationToken);
        lock (_gate) RequireAuthorization(token);
        return result;
    }

    private bool IsAuthorized(string? token) => !_disposed && !_saved.Submitted && token?.Length == 64 &&
        (Mode == "first-run" || DateTimeOffset.UtcNow - _created < TimeSpan.FromMinutes(30)) &&
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(token), Encoding.UTF8.GetBytes(_saved.Token));

    private void RequireAuthorization(string? token)
    {
        if (!IsAuthorized(token)) throw new UnauthorizedAccessException("This setup link is invalid, expired, or already submitted.");
    }

    private string[] AllowedOperations() => Mode == "first-run" ? ["initialize", "import"] :
        _pathFixed ? ["import"] : ["relocate", "import"];

    private string Operation(SetupRequest request)
    {
        var operation = request.Operation ?? (Mode == "first-run"
            ? string.IsNullOrWhiteSpace(request.SourcePath) ? "initialize" : "import" : Mode);
        if (!AllowedOperations().Contains(operation, StringComparer.Ordinal))
            throw new IOException("This operation is unavailable for the current instance or deployment.");
        return operation;
    }

    public Validation Validate(SetupRequest request, string? token)
    {
        lock (_gate)
        {
            RequireAuthorization(token);
            var validation = ValidateCore(request);
            if (validation.Valid)
            {
                try { PathPlan(request, validation); }
                catch (Exception error) when (error is IOException or ArgumentException or InvalidOperationException)
                { return validation with { Valid = false, Error = error.Message }; }
            }
            return validation;
        }
    }

    private Validation ValidateCore(SetupRequest request)
    {
        var target = _currentPath;
        try
        {
            var operation = Operation(request);
            target = string.IsNullOrWhiteSpace(request.TargetPath) ? _currentPath : Canonical(request.TargetPath);
            if (_pathFixed && !Same(target, _currentPath))
                throw new IOException("This server's data path is fixed by its deployment. Change the deployment configuration to choose another path.");
            if (operation == "relocate")
            {
                if (!string.IsNullOrWhiteSpace(request.SourcePath))
                    throw new IOException("Changing the data path always moves the current library; do not select an import source.");
                var relocation = ServerAppDataRelocation.Validate(_anchor, _currentPath, target);
                return new Validation(relocation.Valid, relocation.Error, target);
            }
            if (Mode == "first-run") EnsureEmptyTarget(target);
            if (operation == "import" && string.IsNullOrWhiteSpace(request.SourcePath))
                throw new IOException("Select the source AppData directory to import.");
            if (operation == "initialize" && !string.IsNullOrWhiteSpace(request.SourcePath))
                throw new IOException("Select import to use an existing AppData source.");
            if (Mode != "first-run" && operation == "import" && !Same(target, _currentPath))
            {
                var combined = ServerAppDataRelocation.ValidateImport(_anchor, _currentPath, target,
                    request.SourcePath!, request.OriginalDataPath);
                return new Validation(combined.Valid, combined.Error, target, combined.ImportSourcePath,
                    combined.SourceVersion, combined.OriginalDataPath);
            }
            if (!string.IsNullOrWhiteSpace(request.SourcePath))
            {
                var source = ServerAppDataImport.Validate(request.SourcePath, target, request.OriginalDataPath);
                return new Validation(source.Valid, source.Error, target, source.SourcePath, source.SourceVersion, source.OriginalDataPath);
            }
            return new Validation(true, null, target);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            return new Validation(false, e.Message, target, request.SourcePath, OriginalDataPath: request.OriginalDataPath);
        }
    }

    public CommitResult Commit(SetupRequest request, string? token)
    {
        lock (OperationGate)
        lock (_gate)
        {
            RequireAuthorization(token);
            var validation = ValidateCore(request);
            if (!validation.Valid) throw new IOException(validation.Error);
            var operation = Operation(request);
            var pathPlan = PathPlan(request, validation);
            if (ServerAppDataRelocation.ReadPending(_anchor) != null ||
                File.Exists(Path.Combine(_currentPath, ".pending_relocate")))
                throw new IOException("Finish or cancel the current data-path change before configuring another operation.");
            var combinedImport = Mode != "first-run" && operation == "import" && !Same(validation.TargetPath, _currentPath);
            if (operation == "relocate" || combinedImport)
            {
                if (_monitoring!.Read()?.Phase == "starting" || ServerAppDataImport.ReadPending(_currentPath) != null)
                    throw new IOException("Finish the current startup or import before changing the data path.");
                var journal = combinedImport
                    ? ServerAppDataRelocation.QueueImport(_anchor, _currentPath, validation.TargetPath, validation.SourcePath!, request.OriginalDataPath, pathPlan)
                    : ServerAppDataRelocation.Queue(_anchor, _currentPath, validation.TargetPath);
                try { _monitoring.EnsureRelocation(journal); }
                catch (Exception error)
                {
                    if (ServerAppDataRelocation.ReadPending(_anchor)?.Id == journal.Id)
                        ServerAppDataRelocation.Cancel(_anchor);
                    _monitoring.Fail("The data-path change was not queued. " + error.Message);
                    throw;
                }
                _saved = _saved with { Submitted = true };
                return _result = new CommitResult(!(SetupProcessCoordinator.IsCoordinator || SetupChildConnection.Current != null), _monitoring.Token, _monitoring.Read(), SetupProcessCoordinator.MonitorUrl);
            }
            if (Mode != "first-run" && operation == "import")
            {
                if (_monitoring!.Read()?.Phase == "starting") throw new IOException("Wait until the server has finished starting.");
                QueueAndMonitor(validation.SourcePath!, _currentPath, request.OriginalDataPath, _monitoring, pathPlan);
                _saved = _saved with { Submitted = true };
                return _result = new CommitResult(!(SetupProcessCoordinator.IsCoordinator || SetupChildConnection.Current != null), _monitoring.Token, _monitoring.Read(), SetupProcessCoordinator.MonitorUrl);
            }

            var selectedLock = _originalLock!;
            ServerAppDataImport.Journal? queued = null;
            var changed = !Same(validation.TargetPath, _currentPath);
            if (changed)
            {
                var attempt = DataDirectoryLock.TryAcquire(validation.TargetPath);
                if (!attempt.Acquired) throw new IOException($"Cannot lock the selected data directory: {attempt.Status}.", attempt.Error);
                selectedLock = attempt.Lock!;
            }
            try
            {
                // Recheck after locking; validation never grants permission to merge a
                // directory that acquired other files while the dialog was open.
                EnsureEmptyTarget(validation.TargetPath);
                var monitoring = new ImportProgressStore(validation.TargetPath);
                _selectedMonitoring = monitoring;
                if (validation.SourcePath != null)
                {
                    queued = QueueAndMonitor(validation.SourcePath, validation.TargetPath, request.OriginalDataPath, monitoring, pathPlan);
                }
                else
                {
                    monitoring.BeginInitialization();
                }
                var desiredRedirect = !Same(_anchor, validation.TargetPath);
                var saved = _saved with { Submitted = true, SelectedTarget = validation.TargetPath, RedirectPending = !_pathFixed };
                // This durable intent is written only after the selected destination has
                // its pending import or startup state. A crash can finish the redirect.
                Persist(_anchor, saved);
                _saved = saved;
                _selection = new SetupSelection(validation.TargetPath, selectedLock);
                if (!_pathFixed)
                {
                    try
                    {
                        WriteRedirect(_anchor, desiredRedirect ? validation.TargetPath : null);
                        saved = saved with { RedirectPending = false };
                        Persist(_anchor, saved);
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                    {
                        // The user's choice is already durable. Keep the destination lock
                        // and capability alive, but do not start against the old redirect.
                        _continueAfterResponse = false;
                        monitoring.Fail("Setup choices were saved, but their data-path redirect could not be completed. " +
                                        "Resolve this error and restart the server to finish setup: " + e.Message);
                        return _result = new CommitResult(true, monitoring.Token, monitoring.Read());
                    }
                }
                _saved = saved;
                return _result = new CommitResult(false, monitoring.Token, monitoring.Read(), SetupProcessCoordinator.MonitorUrl);
            }
            catch (Exception error)
            {
                try
                {
                    if (queued != null) RollbackQueue(validation.TargetPath, queued, _selectedMonitoring!, error);
                }
                finally
                {
                    _selectedMonitoring?.Dispose();
                    _selectedMonitoring = null;
                    if (changed) selectedLock.Dispose();
                }
                throw;
            }
        }
    }

    /// <summary>Called only after the successful HTTP response has reached the client.</summary>
    public void SignalCommitted(string? committedToken = null)
    {
        lock (_gate)
        {
            if (_disposed || _commitSignaled) return;
            if (committedToken != null && (committedToken.Length != 64 || !CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(committedToken), Encoding.UTF8.GetBytes(_saved.Token)))) return;
            if (Mode != "first-run" && _saved.Submitted && _result?.Progress is { } progress)
            {
                _commitSignaled = true;
                if (SetupChildConnection.Current is { } child) child.RequestMaintenance(progress.Id);
                else if (SetupProcessCoordinator.IsCoordinator)
                {
                    _monitoring?.Report(new AppDataImportProgressUpdate("stopping"));
                    SetupProcessCoordinator.RequestMaintenance(progress.Id);
                }
                return;
            }
            if (_selection == null || _disposed || !_continueAfterResponse) return;
            _commitSignaled = true;
            _transferred = true;
            _completion.TrySetResult(_selection);
        }
    }

    private static ServerAppDataImport.Journal QueueAndMonitor(string source, string target, string? original,
        ImportProgressStore monitoring, ImportPathPlan? pathPlan = null)
    {
        ServerAppDataImport.Queue(source, target, original, pathPlan: pathPlan);
        var journal = ServerAppDataImport.ReadPending(target)!;
        try { monitoring.EnsureQueued(journal); }
        catch (Exception error)
        {
            RollbackQueue(target, journal, monitoring, error);
            throw;
        }
        return journal;
    }

    private static void RollbackQueue(string target, ServerAppDataImport.Journal journal,
        ImportProgressStore monitoring, Exception cause)
    {
        try
        {
            var current = ServerAppDataImport.ReadPending(target);
            if (current?.Id == journal.Id && current.Phase == "queued") ServerAppDataImport.Cancel(target);
            monitoring.Fail("This setup request was not committed; no import was queued. " + cause.Message);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new IOException("Setup could not finish and could not cancel its saved import request. " +
                                  "The import is still queued; stop the server and resolve its journal before restarting. " + error.Message, cause);
        }
    }

    public static bool RequiresSetup(string anchorPath, string currentPath)
    {
        if (File.Exists(Path.Combine(currentPath, ServerAppDataImport.MarkerName))) return false;
        if (File.Exists(Path.Combine(currentPath, "bakabase_insideworld.db"))) return false;
        var options = Path.Combine(currentPath, "app.json");
        if (File.Exists(options))
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(options).TrimStart('\uFEFF'));
                var root = document.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    var app = root.EnumerateObject().FirstOrDefault(p => p.Name.Equals("App", StringComparison.OrdinalIgnoreCase)).Value;
                    if (app.ValueKind == JsonValueKind.Object) root = app;
                    var version = root.EnumerateObject().FirstOrDefault(p => p.Name.Equals("version", StringComparison.OrdinalIgnoreCase)).Value;
                    if (version.ValueKind == JsonValueKind.String && SemVersion.TryParse(version.GetString(), SemVersionStyles.Any, out _)) return false;
                }
            }
            catch (JsonException) { /* Existing unknown files are rejected by target validation, never overwritten. */ }
        }
        var saved = ReadSaved(anchorPath);
        if (saved?.Submitted != true || saved.SelectedTarget == null || !Same(Canonical(saved.SelectedTarget), Canonical(currentPath))) return true;
        if (!File.Exists(Path.Combine(currentPath, ImportProgressStore.FileName)))
            throw new IOException("The configured data directory has no library or startup state. Restore its mount or backup before restarting.");
        return false;
    }

    public static void RecoverSubmittedRedirect(string anchorPath, bool pathFixed)
    {
        var saved = ReadSaved(anchorPath);
        if (saved is not { Submitted: true, RedirectPending: true, SelectedTarget: not null }) return;
        if (pathFixed) throw new IOException("Setup has a pending data-path choice. Restart with the original deployment settings to complete it.");
        var target = Canonical(saved.SelectedTarget);
        if (!File.Exists(Path.Combine(target, ImportProgressStore.FileName)) &&
            !File.Exists(Path.Combine(target, "bakabase_insideworld.db")))
            throw new IOException("The selected setup data directory is unavailable. Restore its mount and restart.");
        WriteRedirect(anchorPath, Same(Canonical(anchorPath), target) ? null : target);
        Persist(anchorPath, saved with { RedirectPending = false });
    }

    private static void EnsureEmptyTarget(string target)
    {
        if (Path.GetPathRoot(target) == target) throw new IOException("Choose a dedicated data directory, not a filesystem root.");
        if (File.Exists(target)) throw new IOException("The selected data path is a file.");
        if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target)
                .Any(path => !ControlFiles.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)))
            throw new IOException("Choose an empty destination directory. To import a library, select its directory as the source instead.");
        foreach (var name in ControlFiles) RejectLink(Path.Combine(target, name));
    }

    private static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private static bool Same(string left, string right) => string.Equals(left, right,
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    internal static bool SameDirectory(string left, string right) => Same(Canonical(left), Canonical(right));

    internal static string Canonical(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) throw new IOException("Use an absolute directory path.");
        var full = Path.GetFullPath(path);
        var resolved = Path.GetPathRoot(full)!;
        foreach (var part in full[resolved.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            resolved = Path.Combine(resolved, part);
            var info = new DirectoryInfo(resolved);
            if (info.LinkTarget != null) resolved = Canonical(info.ResolveLinkTarget(true)!.FullName);
        }
        return Path.TrimEndingDirectorySeparator(resolved);
    }

    private static Saved? ReadSaved(string anchor)
    {
        var path = Path.Combine(anchor, FileName);
        RejectLink(path);
        if (!File.Exists(path)) return null;
        var saved = JsonSerializer.Deserialize<Saved>(File.ReadAllText(path), JsonOptions);
        if (saved?.Token is not { Length: 64 }) throw new IOException("The server setup state is invalid. Restore it before restarting.");
        return saved;
    }

    private static void Persist(string anchor, Saved saved)
    {
        Directory.CreateDirectory(anchor);
        var path = Path.Combine(anchor, FileName);
        RejectLink(path);
        RejectLink(path + ".tmp");
        File.Delete(path + ".tmp");
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using (var stream = new FileStream(path + ".tmp", options)) JsonSerializer.Serialize(stream, saved, JsonOptions);
        File.Move(path + ".tmp", path, true);
    }

    private static void WriteRedirect(string anchor, string? target)
    {
        var path = AnchorRedirect.GetRedirectPath(anchor);
        RejectLink(path);
        RejectLink(path + ".tmp");
        if (target == null) { AnchorRedirect.Delete(anchor); return; }
        Directory.CreateDirectory(anchor);
        File.WriteAllText(path + ".tmp", target);
        File.Move(path + ".tmp", path, true);
    }

    private static void RejectLink(string path)
    {
        if (new FileInfo(path).LinkTarget != null) throw new IOException($"Setup control files cannot be symbolic links: {path}");
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _preflight?.Dispose();
            if (!_transferred && _selection?.DataLock != _originalLock) _selection?.DataLock.Dispose();
            if (!_transferred) _selectedMonitoring?.Dispose();
        }
    }
}
