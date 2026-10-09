using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Abstractions.Components.App;
using Bakabase.Infrastructures.Components.App.SingleInstance;

namespace Bakabase.Service.Components.ServerData;

public sealed class SetupCoordinatorOptions
{
    public string[] Addresses { get; init; } = ["http://127.0.0.1:34567"];
    public string[] Arguments { get; init; } = [];
    public bool IsDesktop { get; init; }
    public Func<string, Task>? OnSetupUrl { get; init; }
    public Func<Task<string?>>? DirectoryPicker { get; init; }
    public Action<string>? OnBusinessReady { get; init; }
    public Action? OnBusinessUnavailable { get; init; }
    /// <summary>Desktop activation lives exactly as long as this process owns the control lock.</summary>
    public Func<IDisposable>? OnControlAcquired { get; init; }
}

/// <summary>Owns process transitions, never business services or user databases.</summary>
public static class SetupProcessCoordinator
{
    public const string LockFileName = ".bakabase-setup-process.lock";
    public static bool IsCoordinator { get; private set; }
    public static string? SetupAddress { get; private set; }
    public static string? MonitorUrl { get; private set; }
    internal static string? RelaySecret { get; private set; }
    private static Action<string>? _request;
    public static void RequestMaintenance(string id) => _request?.Invoke(id);

    public static async Task<int> RunAsync(SetupCoordinatorOptions options, CancellationToken cancellationToken = default)
    {
        var anchor = AppDataLocator.ResolveAnchor();
        Directory.CreateDirectory(anchor);
        var lockPath = Path.Combine(anchor, LockFileName);
        if (new FileInfo(lockPath).LinkTarget != null) throw new IOException("Setup control lock cannot be a symbolic link.");
        var lockOptions = new FileStreamOptions { Mode = FileMode.OpenOrCreate, Access = FileAccess.ReadWrite, Share = FileShare.None };
        if (!OperatingSystem.IsWindows()) lockOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        FileStream? controlStream = null;
        var handoffDeadline = DateTime.UtcNow.AddSeconds(RestartHandoff.TryReadPredecessorPid(options.Arguments) == null ? 1 : 30);
        while (controlStream == null)
        {
            try { controlStream = new FileStream(lockPath, lockOptions); }
            catch (IOException) when (DateTime.UtcNow < handoffDeadline)
            { await Task.Delay(100, cancellationToken); }
        }
        using var control = controlStream;
        using var activation = options.OnControlAcquired?.Invoke();
        IsCoordinator = true;
        ImportProgressServer? privateServer = null, publicServer = null;
        ImportProgressStore? monitor = null;
        ServerSetupSession? setup = null;
        Child? child = null;
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task? refreshTask = null;
        var request = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _request = id => request.TrySetResult(id);
        var pathFixed = AppDataLocator.IsEnvironmentOverride || ServerSetupSession.InContainer;
        string DataPath() => AppDataLocator.ResolveEffectiveDataDirectory(anchor);
        async Task Refresh()
        {
            while (!lifetime.IsCancellationRequested)
            {
                try { Volatile.Read(ref monitor)?.RefreshFromDisk(DataPath()); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { }
                try { await Task.Delay(250, lifetime.Token); } catch (OperationCanceledException) { break; }
            }
        }
        async Task PublicHost()
        {
            if (!options.IsDesktop && publicServer == null)
                publicServer = await ImportProgressServer.StartAsync(monitor!, options.Addresses);
        }
        async Task BeforeBind()
        {
            if (publicServer != null) { await publicServer.DisposeAsync(); publicServer = null; }
        }
        async Task ShowProgress()
        {
            var url = (options.IsDesktop ? SetupAddress : options.Addresses[0].Replace("0.0.0.0", "127.0.0.1")) +
                      ImportProgressServer.PagePath + "#token=" + monitor!.Token;
            Console.WriteLine("Server maintenance: " + url);
            if (options.OnSetupUrl != null) await options.OnSetupUrl(url);
        }
        void SaveFailure(string error)
        {
            using var held = OwnData(DataPath());
            using var writer = new ImportProgressStore(DataPath());
            writer.Fail(error);
            monitor!.RefreshFromDisk(DataPath());
            // Fail logs persistence errors; the independent UI must still keep its failure.
            monitor.FailReadOnly(error);
        }
        try
        {
            // Refuse a legacy/unmanaged business process before exposing setup or changing state.
            using (var held = OwnData(anchor)) ServerSetupSession.RecoverSubmittedRedirect(anchor, pathFixed);
            var data = DataPath();
            using (OwnData(data)) { }
            monitor = new ImportProgressStore(data, readOnly: true);
            ImportProgressStore.Current = monitor;
            privateServer = await ImportProgressServer.StartAsync(monitor, ["http://127.0.0.1:0"], options.DirectoryPicker);
            SetupAddress = privateServer.Addresses[0];
            MonitorUrl = (options.IsDesktop ? SetupAddress : "") + ImportProgressServer.PagePath;
            if (ServerSetupSession.RequiresSetup(anchor, data))
            {
                using var initialLock = OwnData(data);
                setup = new ServerSetupSession(anchor, data, pathFixed, initialLock, ServerSetupSession.InContainer);
                ServerSetupSession.Current = setup;
                await PublicHost();
                var baseUrl = options.IsDesktop ? SetupAddress : options.Addresses[0].Replace("0.0.0.0", "127.0.0.1");
                var setupUrl = baseUrl + ImportProgressServer.SetupPath + "#setupToken=" + setup.Token;
                Console.WriteLine("Server setup: " + setupUrl);
                if (options.OnSetupUrl != null) await options.OnSetupUrl(setupUrl);
                var selection = await setup.Completion.WaitAsync(lifetime.Token);
                selection.DataLock.Dispose();
                // Publish the selected operation before clearing the session override.
                // Otherwise an in-flight progress request can observe the initial empty
                // store and incorrectly revoke the newly issued monitoring capability.
                monitor = ReplaceMonitoring(monitor, DataPath());
                setup.Monitoring?.Dispose();
                setup.Dispose(); setup = null; ServerSetupSession.Current = null;
            }
            refreshTask = Task.Run(Refresh);
            while (!lifetime.IsCancellationRequested)
            {
                var move = ServerAppDataRelocation.ReadPending(anchor);
                var import = ServerAppDataImport.ReadPending(DataPath());
                var needsWorker = move != null || import != null;
                var tracking = needsWorker || monitor.Applied && monitor.Read()?.Phase is "starting" or "failed";
                if (tracking)
                {
                    // Ensure a token exists even for an interrupted older journal.
                    using (var held = OwnData(DataPath()))
                    using (var writer = new ImportProgressStore(DataPath()))
                    { if (move != null) writer.EnsureRelocation(move); else if (import != null) writer.EnsureQueued(import); }
                    monitor.RefreshFromDisk(DataPath());
                    await PublicHost(); await ShowProgress();
                }
                if (needsWorker)
                {
                    child = await Child.Start("worker", options.Arguments, SetupAddress!, BeforeBind, null, null, lifetime.Token);
                    var exit = await child.Process.WaitForExitAsync(lifetime.Token).ContinueWith(_ => child.Process.ExitCode,
                        lifetime.Token, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
                    var reason = child.Error;
                    await child.DisposeAsync(); child = null;
                    monitor = ReplaceMonitoring(monitor, DataPath());
                    if (exit == 0 && (ServerAppDataRelocation.ReadPending(anchor) != null || ServerAppDataImport.ReadPending(DataPath()) != null))
                        throw new IOException("The maintenance worker exited without completing its saved operation. The business application was not started.");
                    if (exit != 0)
                    {
                        SaveFailure(reason ?? $"Maintenance worker exited unexpectedly (code {exit}). The business application has not been started. Restart Setup to recover from the saved record.");
                        await PublicHost(); await ShowProgress();
                        await Task.Delay(Timeout.Infinite, lifetime.Token);
                    }
                }
                var businessReady = false;
                child = await Child.Start("business", options.Arguments, SetupAddress!, BeforeBind,
                    address => { businessReady = true; privateServer.ApplicationAddress = address; options.OnBusinessReady?.Invoke(address); },
                    async mode =>
                    {
                        if (!businessReady) throw new IOException("Wait for the application to finish starting before opening Setup.");
                        if (ServerAppDataImport.ReadPending(DataPath()) != null || ServerAppDataRelocation.ReadPending(anchor) != null)
                            throw new IOException("Finish the current data operation before starting another.");
                        lock (ServerSetupSession.OperationGate)
                        {
                            ServerSetupSession.Current?.Dispose();
                            ServerSetupSession.Current = mode == "relocate"
                                ? ServerSetupSession.ForRelocation(anchor, DataPath(), monitor)
                                : ServerSetupSession.ForImport(DataPath(), monitor, anchor);
                        }
                        var token = ServerSetupSession.Current.Token;
                        var setupUrl = (options.IsDesktop ? SetupAddress : "") + ImportProgressServer.SetupPath;
                        if (options.OnSetupUrl != null) await options.OnSetupUrl(setupUrl + "#setupToken=" + token);
                        return JsonSerializer.Serialize(new { setupToken = token, setupUrl });
                    }, lifetime.Token);
                child.MaintenanceRequested = id => request.TrySetResult(id);
                var exited = child.Process.WaitForExitAsync(lifetime.Token);
                var completed = await Task.WhenAny(exited, request.Task);
                if (completed == exited)
                {
                    options.OnBusinessUnavailable?.Invoke();
                    await exited;
                    var exit = child.Process.ExitCode;
                    var error = child.Error;
                    await child.DisposeAsync(); child = null;
                    if (exit == 0) return 0;
                    if (!tracking) return exit;
                    SaveFailure(error ?? $"Business startup exited unexpectedly (code {exit}). Restart Setup after resolving the error.");
                    await PublicHost(); await ShowProgress();
                    await Task.Delay(Timeout.Infinite, lifetime.Token);
                }
                var requestedId = await request.Task;
                var pendingId = ServerAppDataRelocation.ReadPending(anchor)?.Id ?? ServerAppDataImport.ReadPending(DataPath())?.Id;
                if (requestedId != pendingId) throw new IOException("The committed maintenance operation no longer matches its request.");
                options.OnBusinessUnavailable?.Invoke();
                monitor.RefreshFromDisk(DataPath());
                monitor.Report(new AppDataImportProgressUpdate("stopping"));
                await ShowProgress(); // The desktop parent window must survive its business child's exit.
                child.Send("stop");
                try { await exited.WaitAsync(TimeSpan.FromSeconds(30), lifetime.Token); }
                catch (TimeoutException)
                {
                    Console.Error.WriteLine("Business shutdown is still pending. Data will not be copied while it is running.");
                    // A slow shutdown is not permission to kill it and start replacing files.
                    await exited.WaitAsync(lifetime.Token);
                }
                if (child.Process.ExitCode != 0)
                {
                    var error = child.Error ?? "The business process did not exit cleanly. Restart Setup to resume safely.";
                    await child.DisposeAsync(); child = null;
                    SaveFailure(error); await PublicHost(); await ShowProgress();
                    await Task.Delay(Timeout.Infinite, lifetime.Token);
                }
                await child.DisposeAsync(); child = null;
                request = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                ServerSetupSession.Current?.Dispose(); ServerSetupSession.Current = null;
            }
            return 0;
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { return 0; }
        catch (Exception error) when (monitor?.Token != null)
        {
            options.OnBusinessUnavailable?.Invoke();
            Console.Error.WriteLine($"Setup transition failed: {error}");
            if (child != null) { await child.DisposeAsync(); child = null; }
            // Includes failure before a worker can authenticate/connect. Do not abandon the UI.
            try { SaveFailure(error.Message); }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            { monitor.FailReadOnly(error.Message); Console.Error.WriteLine($"Unable to persist Setup failure: {failure.Message}"); }
            await PublicHost(); await ShowProgress();
            try { await Task.Delay(Timeout.Infinite, lifetime.Token); } catch (OperationCanceledException) { }
            return 1;
        }
        finally
        {
            lifetime.Cancel();
            if (child != null) await child.DisposeAsync();
            if (refreshTask != null) await refreshTask;
            if (publicServer != null) await publicServer.DisposeAsync();
            if (privateServer != null) await privateServer.DisposeAsync();
            setup?.Dispose(); ServerSetupSession.Current?.Dispose(); ServerSetupSession.Current = null;
            monitor?.Dispose(); ImportProgressStore.Current = null;
            _request = null; RelaySecret = null; IsCoordinator = false; SetupAddress = null; MonitorUrl = null;
        }
    }

    internal static ImportProgressStore ReplaceMonitoring(ImportProgressStore previous, string directory)
    {
        var replacement = new ImportProgressStore(directory, readOnly: true);
        ImportProgressStore.Current = replacement;
        previous.Dispose();
        return replacement;
    }

    internal static DataDirectoryLock OwnData(string path)
    {
        var claim = DataDirectoryLock.TryAcquire(path);
        return claim.Lock ?? throw new IOException($"Cannot lock appdata at {path}: {claim.Status}.", claim.Error);
    }

    private sealed class Child : IAsyncDisposable
    {
        private readonly NamedPipeServerStream _pipe;
        private readonly StreamReader _reader;
        private readonly StreamWriter _writer;
        private readonly object _gate = new();
        private readonly CancellationTokenSource _reading = new();
        public Process Process { get; }
        public string? Error { get; private set; }
        public Action<string>? MaintenanceRequested { get; set; }
        private Child(Process process, NamedPipeServerStream pipe)
        {
            Process = process; _pipe = pipe;
            _reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, true);
            _writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true);
        }
        public static async Task<Child> Start(string role, string[] arguments, string setupAddress,
            Func<Task> beforeBind, Action<string>? ready, Func<string, Task<string>>? createSetup, CancellationToken token)
        {
            var name = "bk" + Guid.NewGuid().ToString("N")[..16];
            var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            var process = Process.Start(SetupChildConnection.ChildStartInfo(role, arguments, name, secret))
                          ?? throw new IOException("Cannot start the Setup child process.");
            var child = new Child(process, pipe);
            Console.WriteLine($"Setup {role} process: {process.Id}");
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromSeconds(25));
                var connected = pipe.WaitForConnectionAsync(timeout.Token);
                var exited = process.WaitForExitAsync(timeout.Token);
                if (await Task.WhenAny(connected, exited) == exited)
                    throw new IOException($"Setup {role} exited before connecting (code {process.ExitCode}).");
                await connected;
                child._writer.AutoFlush = true;
                var hello = await SetupChildConnection.ReadMessage(child._reader, timeout.Token);
                if (hello?.Type != "hello" || hello.Value?.Length != secret.Length || !CryptographicOperations.FixedTimeEquals(
                        Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(hello.Value)))
                    throw new IOException("Setup child authentication failed.");
                if (role == "business") RelaySecret = secret;
                child.Send("accepted", setupAddress, MonitorUrl, Environment.ProcessId);
                _ = child.Receive(beforeBind, ready, createSetup);
                return child;
            }
            catch { await child.DisposeAsync(); throw; }
        }
        public void Send(string type, string? value = null, string? monitorUrl = null, int? parentProcessId = null)
        { lock (_gate) { if (!_pipe.IsConnected) throw new IOException("Setup child is not connected."); _writer.WriteLine(JsonSerializer.Serialize(new SetupChildConnection.Message(type, value, monitorUrl, parentProcessId), SetupChildConnection.Json)); } }
        private async Task Receive(Func<Task> beforeBind, Action<string>? ready, Func<string, Task<string>>? createSetup)
        {
            try
            {
                while (await SetupChildConnection.ReadMessage(_reader, _reading.Token) is { } message)
                {
                    switch (message.Type)
                    {
                        case "binding": await beforeBind(); Send("bind"); break;
                        case "ready": ready?.Invoke(message.Value!); break;
                        case "fatal": Error = message.Value; break;
                        case "commit": ServerSetupSession.Current?.SignalCommitted(message.Value); break;
                        case "maintenance": MaintenanceRequested?.Invoke(message.Value!); break;
                        case "setup":
                            try { Send("setup-result", await createSetup!(message.Value!)); }
                            catch (Exception e) { Send("setup-error", e.Message); }
                            break;
                    }
                }
            }
            catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException) { }
        }
        public async ValueTask DisposeAsync()
        {
            if (!Process.HasExited)
            {
                try { Send("stop"); } catch (IOException) { }
                try { await Process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)); }
                catch (TimeoutException) { Process.Kill(entireProcessTree: true); await Process.WaitForExitAsync(); }
            }
            _reading.Cancel(); await _pipe.DisposeAsync(); Process.Dispose();
        }
    }
}
