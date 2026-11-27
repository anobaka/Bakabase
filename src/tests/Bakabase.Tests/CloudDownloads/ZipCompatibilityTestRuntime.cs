using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.CloudTools;
using Bakabase.InsideWorld.Models.Configs;

namespace Bakabase.Tests.CloudDownloads;

[TestClass]
public sealed class ZipCompatibilityTestRuntime
{
    private const string OverrideVariable = "BAKABASE_TEST_PYZIPPER_PYTHON";
    private const string Package = "pyzipper==0.4.0";
    private static readonly Lazy<Task<PythonRuntimeExecution>> Runtime = new(PrepareAsync);
    private static DirectoryInfo? _environment;

    internal static Task<PythonRuntimeExecution> GetAsync() => Runtime.Value;

    private static async Task<PythonRuntimeExecution> PrepareAsync()
    {
        var executable = Environment.GetEnvironmentVariable(OverrideVariable);
        if (string.IsNullOrWhiteSpace(executable))
        {
            if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true")
                Assert.Inconclusive($"Set {OverrideVariable} to an isolated Python environment containing {Package}.");

            _environment = Directory.CreateTempSubdirectory("bakabase-zip-tests-");
            var root = _environment.FullName;
            executable = OperatingSystem.IsWindows() ? Path.Combine(root, "Scripts", "python.exe") : Path.Combine(root, "bin", "python");
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            var stage = "creating the Python venv";
            try
            {
                await CloudToolProcess.RunAsync(OperatingSystem.IsWindows() ? "python" : "python3",
                    ["-I", "-m", "venv", root], root, timeout.Token);
                stage = $"installing {Package}";
                await CloudToolProcess.RunAsync(executable, ["-I", "-m", "pip", "--disable-pip-version-check", "--no-input",
                    "install", "--no-cache-dir", "--only-binary=:all:", Package], root, timeout.Token);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"ZIP test runtime setup failed while {stage}. Set {OverrideVariable} to use an existing environment.", ex);
            }
        }
        Assert.IsTrue(File.Exists(executable), $"ZIP test interpreter does not exist: {executable}");
        return new(new() {Mode = PythonRuntimeMode.External, PythonExecutable = executable}, new("test", "test", executable, "fixture"));
    }

    [AssemblyCleanup]
    public static void Cleanup() => _environment?.Delete(recursive: true);
}
