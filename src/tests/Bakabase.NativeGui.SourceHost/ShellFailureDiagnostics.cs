using System.Diagnostics;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;

namespace Bakabase.NativeGui.SourceHost;

// Test-entry diagnostics only. The production adapter catches these failures
// and displays a window; do not read that window's editable error field.
internal sealed class ShellFailureDiagnostics : IDisposable
{
    internal const string Scope = "native-gui-shell-failure-diagnostic";
    internal const int MaxObservations = 8;
    internal const int MaxBytes = 4096;
    private static readonly HashSet<string> ExceptionTypes =
    [
        "System.ArgumentException", "System.ArgumentNullException", "System.ArgumentOutOfRangeException",
        "System.BadImageFormatException", "System.DllNotFoundException", "System.EntryPointNotFoundException",
        "System.InvalidOperationException", "System.NotSupportedException", "System.NullReferenceException",
        "System.TypeInitializationException", "System.TypeLoadException", "System.MissingMethodException",
        "System.MissingFieldException", "System.IO.FileNotFoundException", "System.IO.DirectoryNotFoundException",
        "System.IO.IOException", "System.UnauthorizedAccessException", "System.Reflection.TargetInvocationException",
        "System.Runtime.InteropServices.COMException", "System.ComponentModel.Win32Exception",
        "Avalonia.Markup.Xaml.XamlLoadException", "Avalonia.Markup.Xaml.XamlParseException"
    ];

    [ThreadStatic] private static bool _insideHandler;
    private readonly Assembly _shellAssembly;
    private readonly FileStream _stream;
    private readonly List<Observation> _observations = [];
    private readonly object _sync = new();
    private int _count;
    private bool _disabled;

    private ShellFailureDiagnostics(FileStream stream, Assembly shellAssembly)
    {
        _stream = stream;
        _shellAssembly = shellAssembly;
        Write();
        AppDomain.CurrentDomain.FirstChanceException += Observe;
    }

    internal static ShellFailureDiagnostics Install(FixtureOptions options, Assembly shellAssembly)
    {
        if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true" ||
            Environment.GetEnvironmentVariable("RUNNER_ENVIRONMENT") != "github-hosted" ||
            !(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()))
            throw new InvalidOperationException("Source diagnostics require a disposable hosted runner.");
        // FixtureOptions.Parse has already proved this exact private data root.
        for (var current = new DirectoryInfo(options.DataDirectory); current != null; current = current.Parent)
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Source diagnostic path changed.");
        var path = Path.Combine(options.DataDirectory, $"native-shell-diagnostics-{Environment.ProcessId}.json");
        var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        try { return new ShellFailureDiagnostics(stream, shellAssembly); }
        catch { stream.Dispose(); throw; }
    }

    private void Observe(object? _, FirstChanceExceptionEventArgs args)
    {
        if (_insideHandler || Volatile.Read(ref _count) >= MaxObservations) return;
        _insideHandler = true;
        try
        {
            // Capture method metadata only. Exception.StackTrace may not yet be
            // populated at first chance. No filename, arguments or message is read.
            var observation = Describe(args.Exception, new StackTrace(false), _shellAssembly);
            if (observation == null) return;
            lock (_sync)
            {
                if (_disabled || _count >= MaxObservations) return;
                _observations.Add(observation);
                Volatile.Write(ref _count, _observations.Count);
                Write();
            }
        }
        catch
        {
            // Diagnostics must not replace the original exception or recurse.
            _disabled = true;
            Volatile.Write(ref _count, MaxObservations);
        }
        finally { _insideHandler = false; }
    }

    internal static Observation? Describe(Exception error, StackTrace trace, Assembly shellAssembly)
    {
        if (trace.FrameCount > 128) return null;
        var insideMainWindow = false;
        string? tag = null;
        for (var index = 0; index < trace.FrameCount; index++)
        {
            var method = trace.GetFrame(index)?.GetMethod();
            var type = method?.DeclaringType;
            if (type?.Assembly != shellAssembly) continue;
            var pair = (type.FullName, method!.Name);
            if (pair == ("Bakabase.Shell.Components.AvaloniaGuiAdapter", "ShowMainWebView"))
                insideMainWindow = true;
            tag ??= pair switch
            {
                ("Bakabase.Shell.Windows.MainWindow", ".ctor") => "main-window-constructor",
                ("Bakabase.Shell.Windows.MainWindow", "InitializeComponent") => "main-window-xaml",
                ("Bakabase.Shell.Controls.NativeWebViewHost", "CreateNativeControlCore") => "native-control-create",
                ("Bakabase.Shell.Controls.NativeWebViewHost", "CreateWindows") => "windows-control-create",
                ("Bakabase.Shell.Controls.NativeWebViewHost", "Navigate") => "navigate",
                ("Bakabase.Shell.Controls.NativeWebViewHost", "NavigateWindows") => "windows-navigate",
                ("Bakabase.Shell.Components.AvaloniaGuiAdapter", "ShowMainWebView") => "show-main-window",
                _ => null
            };
        }
        if (!insideMainWindow) return null;
        var typeName = error.GetType().FullName;
        return new Observation(typeName != null && ExceptionTypes.Contains(typeName) ? typeName : "Other",
            error.HResult, tag ?? "show-main-window");
    }

    private void Write()
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1, scope = Scope, pid = Environment.ProcessId, observations = _observations
        });
        if (bytes.Length > MaxBytes) throw new InvalidOperationException("Source diagnostic byte budget exceeded.");
        _stream.Position = 0;
        _stream.Write(bytes);
        _stream.SetLength(bytes.Length);
        _stream.Flush();
    }

    public void Dispose()
    {
        AppDomain.CurrentDomain.FirstChanceException -= Observe;
        lock (_sync) { _disabled = true; Volatile.Write(ref _count, MaxObservations); _stream.Dispose(); }
    }

    internal sealed record Observation(string exceptionType, int hresult, string method);
}
