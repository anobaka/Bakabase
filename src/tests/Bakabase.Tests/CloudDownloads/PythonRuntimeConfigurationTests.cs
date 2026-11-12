using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.CloudTools;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.TestKit.Implementations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Bakabase.Abstractions.Components.Network;

namespace Bakabase.Tests.CloudDownloads;

[TestClass]
public sealed class PythonRuntimeConfigurationTests
{
    private string _root = null!;
    private TestBOptionsManager<PythonRuntimeOptions> _options = null!;
    private FixtureService _service = null!;
    private ServiceProvider _services = null!;

    private sealed class FixtureService(TestBOptionsManager<PythonRuntimeOptions> options, string components, IServiceProvider services)
        : PythonRuntimeConfigurationService(options, components, services)
    {
        public Func<PythonRuntimeExecution?, PythonRuntimeExecution, CancellationToken, Task>? Preparing { get; set; }
        protected override Task PrepareToolsAsync(PythonRuntimeExecution? previous, PythonRuntimeExecution candidate, CancellationToken ct) =>
            Preparing?.Invoke(previous, candidate, ct) ?? Task.CompletedTask;
        public bool FailRefresh { get; set; }
        protected override Task RefreshContextsAsync() => FailRefresh
            ? Task.FromException(new IOException("refresh fixture failed")) : Task.CompletedTask;
    }

    [TestInitialize]
    public void Initialize()
    {
        if (OperatingSystem.IsWindows()) Assert.Inconclusive("Executable fixtures use Unix scripts; native Windows acceptance is separate.");
        _root = Path.Combine(Path.GetTempPath(), "python-runtime-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _options = new(new());
        _services = new ServiceCollection().BuildServiceProvider();
        _service = new(_options, Path.Combine(_root, "components"), _services);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _services?.Dispose();
        if (_root != null && Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private async Task<PythonRuntimeConfiguration> Python(string name, string version = "3.13.5")
    {
        var executable = Path.Combine(_root, name);
        await File.WriteAllTextAsync(executable, """
            #!/bin/sh
            if [ "$1" = "-B" ]; then shift; fi
            if [ "$2" = "-m" ] && [ "$3" = "venv" ]; then
              mkdir -p "$5/bin"
              cp "$0" "$5/bin/python"
              exit 0
            fi
            if [ "$2" = "-m" ] && [ "$3" = "pip" ]; then exit 0; fi
            case "$3" in
              *platform.python_version*) cat "$0.probe.json" ;;
              *isolated-ok*) printf 'isolated-ok\n' ;;
              *importlib.metadata*) printf '6.4.2\n' ;;
              *) exit 71 ;;
            esac
            """);
        File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        await File.WriteAllTextAsync(executable + ".probe.json", JsonSerializer.Serialize(new
        { version, implementation = "CPython", machine = "aarch64", bits = 64, executable, basePrefix = _root, cacheTag = "cpython-313" }));
        return new() { Mode = PythonRuntimeMode.External, PythonExecutable = executable };
    }

    [TestMethod]
    public async Task ValidationChecksAnIsolatedEnvironmentWithoutApplyingOrLeavingTemporaryFiles()
    {
        var configuration = await Python("external python");
        var result = await _service.ValidateAsync(configuration, default);
        Assert.IsTrue(result.IsValid, result.Error);
        Assert.IsFalse(result.Applied);
        Assert.AreEqual(PythonRuntimeMode.Managed, result.Configuration.Mode);
        Assert.AreEqual(PythonRuntimeMode.Managed, _options.Value.Configuration.Mode);
        Assert.AreEqual(0, Directory.GetDirectories(Path.Combine(_root, "components", "cloud-python"), "validation-*").Length);
        Assert.AreEqual("3.13.5", result.Runtime?.PythonVersion);
    }

    [TestMethod]
    public async Task InvalidCandidateLeavesTheExistingSourceUnchanged()
    {
        var initial = await Python("working");
        Assert.IsTrue((await _service.ApplyAsync(initial, default)).Applied);
        var snapshot = _service.Capture();
        var invalid = await Python("too-old", "3.9.20");
        var result = await _service.ApplyAsync(invalid, default);
        Assert.IsFalse(result.IsValid);
        Assert.IsFalse(result.Applied);
        Assert.AreSame(snapshot, _service.Capture());
        Assert.AreEqual(initial.PythonExecutable, _options.Value.Configuration.PythonExecutable);
    }

    [TestMethod]
    public async Task CandidateIsPreparedBeforeCommitAndCapturedOldLaunchRemainsStable()
    {
        var initial = await Python("old python");
        Assert.IsTrue((await _service.ApplyAsync(initial, default)).Applied);
        var snapshot = _service.Capture();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _service.Preparing = async (_, _, ct) => { started.SetResult(); await release.Task.WaitAsync(ct); };
        var next = await Python("new python");
        var applying = _service.ApplyAsync(next, default);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreSame(snapshot, _service.Capture());
        Assert.AreEqual(initial.PythonExecutable, _options.Value.Configuration.PythonExecutable);
        release.SetResult();
        Assert.IsTrue((await applying).Applied);
        Assert.AreEqual(next.PythonExecutable, _service.Capture().PythonExecutable);
        Assert.AreEqual(initial.PythonExecutable, snapshot.PythonExecutable);
        Assert.AreEqual(initial.PythonExecutable, snapshot.StartInfo(["-I", "-c", "pass"], _root).FileName);
        Assert.AreNotEqual(snapshot.Info.SourceFingerprint, _service.Capture().Info.SourceFingerprint);
    }

    [TestMethod]
    public async Task CancellationWhilePreparingDoesNotChangeTheSource()
    {
        var initial = await Python("initial");
        Assert.IsTrue((await _service.ApplyAsync(initial, default)).Applied);
        var snapshot = _service.Capture();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _service.Preparing = async (_, _, ct) => { started.SetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, ct); };
        using var cancellation = new CancellationTokenSource();
        var applying = _service.ApplyAsync(await Python("cancelled"), cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => applying);
        Assert.AreSame(snapshot, _service.Capture());
        Assert.AreEqual(initial.PythonExecutable, _options.Value.Configuration.PythonExecutable);
    }

    [TestMethod]
    public async Task ConcurrentAppliesCommitInPreparationOrder()
    {
        var initial = await Python("initial");
        Assert.IsTrue((await _service.ApplyAsync(initial, default)).Applied);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var preparations = new List<string>();
        _service.Preparing = async (_, candidate, ct) =>
        {
            preparations.Add(candidate.PythonExecutable);
            if (preparations.Count == 1) { started.SetResult(); await release.Task.WaitAsync(ct); }
        };
        var first = await Python("first");
        var second = await Python("second");
        var applyingFirst = _service.ApplyAsync(first, default);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var applyingSecond = _service.ApplyAsync(second, default);
        Assert.IsFalse(applyingSecond.IsCompleted);
        release.SetResult();
        Assert.IsTrue((await applyingFirst).Applied);
        Assert.IsTrue((await applyingSecond).Applied);
        CollectionAssert.AreEqual(new[] { first.PythonExecutable, second.PythonExecutable }, preparations);
        Assert.AreEqual(second.PythonExecutable, _service.Capture().PythonExecutable);
    }

    [TestMethod]
    public async Task ChangingAnExternalInterpreterOnDiskRequiresRevalidation()
    {
        var external = await Python("mutable external");
        Assert.IsTrue((await _service.ApplyAsync(external, default)).Applied);
        var snapshot = _service.Capture();
        await File.AppendAllTextAsync(external.PythonExecutable!, "\n# interpreter replacement\n");
        var current = await _service.GetConfigurationAsync(default);
        Assert.IsNull(current.Runtime);
        StringAssert.Contains(current.Error!, "changed on disk");
        Assert.AreSame(snapshot, _service.Capture());
        Assert.IsTrue((await _service.ApplyAsync(external, default)).Applied);
        Assert.AreNotEqual(snapshot.Info.SourceFingerprint, _service.Capture().Info.SourceFingerprint);
    }

    [TestMethod]
    public void CondaLaunchUsesAnExplicitPrefixAndPreservesArgumentBoundaries()
    {
        var prefix = Path.Combine(_root, "conda environment");
        var configuration = new PythonRuntimeConfiguration { Mode = PythonRuntimeMode.Conda,
            CondaExecutable = Path.Combine(_root, "conda executable"), CondaPrefix = prefix };
        var execution = new PythonRuntimeExecution(configuration, new("3.13.5", "arm64", "/base/python", "source"), "/private venv/bin/python");
        var start = execution.StartInfo(["-I", "-u", "script with spaces.py", "$(must-not-run)"], _root);
        Assert.IsFalse(start.UseShellExecute);
        CollectionAssert.AreEqual(new[] { "run", "--no-capture-output", "--prefix", prefix, "/private venv/bin/python", "-B", "-I", "-u", "script with spaces.py", "$(must-not-run)" }, start.ArgumentList.ToArray());
        configuration.CondaPrefix = "/changed";
        Assert.AreEqual(prefix, execution.Configuration.CondaPrefix);
        var exposed = execution.Configuration;
        exposed.CondaPrefix = "/also-changed";
        Assert.AreEqual(prefix, execution.Configuration.CondaPrefix);
    }

    private sealed class FixtureTool : CloudPythonToolService
    {
        public FixtureTool(string components, IServiceProvider services)
            : base(NullLoggerFactory.Instance, components, "fixture-gdown", null!,
                new BakabaseWebProxy(new TestBOptions<NetworkOptions>(new())), services) { }
        protected override string PackageName => "gdown";
        public override string ValidatedVersion => "6.4.2";
        protected override string KeyInLocalizer => "Gdown";
        public override string Id => "fixture-gdown";
    }

    [TestMethod]
    public async Task LegacyManagedEnvironmentIsReusedButExternalSourceGetsItsOwnGeneration()
    {
        var configuration = await Python("base python");
        var components = Path.Combine(_root, "components");
        using var services = new ServiceCollection().AddSingleton<PythonRuntimeConfigurationService>(_service).BuildServiceProvider();
        var tool = new FixtureTool(components, services);
        var legacy = Path.Combine(tool.DefaultLocation, "venv-6.4.2", "bin", "python");
        Directory.CreateDirectory(Path.GetDirectoryName(legacy)!);
        File.Copy(configuration.PythonExecutable!, legacy);
        var managed = new PythonRuntimeExecution(new(), new("3.13.16", "arm64", configuration.PythonExecutable!, "managed-source"));
        Assert.AreEqual(legacy, tool.ExecutionFor(managed).PythonExecutable);
        await tool.PrepareForAsync(managed, default);
        Assert.AreEqual(0, Directory.GetFiles(tool.DefaultLocation, "source-*.json").Length, "Healthy legacy environments must not require reinstalling packages.");
        var external = await _service.ProbeAsync(configuration, default);
        Assert.AreNotEqual(legacy, tool.ExecutionFor(external).PythonExecutable);
        await tool.PrepareForAsync(external, default);
        var captured = tool.ExecutionFor(external);
        Assert.AreNotEqual(legacy, captured.PythonExecutable);
        StringAssert.Contains(captured.PythonExecutable, external.Info.SourceFingerprint);
        Assert.IsTrue(await tool.IsReadyExecutionAsync(captured, default));
        Assert.IsTrue(File.Exists(legacy), "The old environment remains available to active downloads.");
    }

    [TestMethod]
    public async Task FailedEnvironmentPreparationDoesNotPublishAPointerOrDeleteExistingEnvironment()
    {
        var good = await Python("healthy base");
        var components = Path.Combine(_root, "components");
        using var services = new ServiceCollection().AddSingleton<PythonRuntimeConfigurationService>(_service).BuildServiceProvider();
        var tool = new FixtureTool(components, services);
        var source = await _service.ProbeAsync(good, default);
        await tool.PrepareForAsync(source, default);
        var captured = tool.ExecutionFor(source);
        var bad = await Python("failing pip");
        var text = await File.ReadAllTextAsync(bad.PythonExecutable!);
        await File.WriteAllTextAsync(bad.PythonExecutable!, text.Replace("if [ \"$2\" = \"-m\" ] && [ \"$3\" = \"pip\" ]; then exit 0; fi", "if [ \"$2\" = \"-m\" ] && [ \"$3\" = \"pip\" ]; then exit 99; fi"));
        var candidate = await _service.ProbeAsync(bad, default);
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => tool.PrepareForAsync(candidate, default));
        Assert.IsFalse(File.Exists(Path.Combine(tool.DefaultLocation, "source-" + candidate.Info.SourceFingerprint + ".json")));
        Assert.IsTrue(await tool.IsReadyExecutionAsync(captured, default));
        Assert.AreEqual(captured.PythonExecutable, tool.ExecutionFor(source).PythonExecutable);
        Assert.AreEqual(1, Directory.GetDirectories(tool.DefaultLocation, "venv-*").Length);
    }

    [TestMethod]
    public async Task RefreshFailureAfterCommitDoesNotReportTheAppliedSourceAsUnapplied()
    {
        _service.FailRefresh = true;
        var candidate = await Python("working despite refresh failure");
        var result = await _service.ApplyAsync(candidate, default);
        Assert.IsTrue(result.IsValid, result.Error);
        Assert.IsTrue(result.Applied);
        Assert.AreEqual(candidate.PythonExecutable, _service.Capture().PythonExecutable);
    }

    [TestMethod]
    public void RelativePathsAndCommandsAreRejectedBeforeExecution()
    {
        Assert.ThrowsException<ArgumentException>(() => PythonRuntimeConfigurationService.Normalize(new()
            { Mode = PythonRuntimeMode.External, PythonExecutable = "python -m anything" }));
        Assert.ThrowsException<ArgumentException>(() => PythonRuntimeConfigurationService.Normalize(new()
            { Mode = PythonRuntimeMode.Conda, CondaExecutable = "/conda", CondaPrefix = "relative" }));
    }
}
