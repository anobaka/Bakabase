using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Bakabase.Tests")]

namespace Bakabase.Abstractions.Helpers;

/// <summary>Reveals an item on a Linux desktop, falling back to opening its containing folder.</summary>
public static class LinuxFileManager
{
    public static void RevealInParentDirectory(string path) => RevealInParentDirectory(path, Start);

    internal static void RevealInParentDirectory(string path, Func<ProcessStartInfo, IRevealProcess?> start)
    {
        var fullPath = Path.GetFullPath(path);
        // Encoding each Linux path component preserves spaces, quotes, #, ?, and literal
        // backslashes as filename data rather than URI or GVariant syntax.
        var uri = OperatingSystem.IsWindows() ? new Uri(fullPath).AbsoluteUri :
            "file://" + string.Join("/", fullPath.Split('/').Select(Uri.EscapeDataString));
        var showItems = Command("gdbus", "call", "--session", "--dest", "org.freedesktop.FileManager1",
            "--object-path", "/org/freedesktop/FileManager1", "--method", "org.freedesktop.FileManager1.ShowItems",
            "--timeout", "2", "['" + uri.Replace("'", "\\'") + "']", "''");
        try
        {
            using var process = start(showItems);
            if (process != null)
            {
                // gdbus's initial introspection has its own timeout. Bound the entire
                // child process, not just the ShowItems method's --timeout option.
                if (process.WaitForExit(2000))
                {
                    if (process.ExitCode == 0) return;
                }
                else
                {
                    try { process.Kill(); }
                    catch (Exception error) when (error is Win32Exception or InvalidOperationException) { }
                }
            }
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException or IOException)
        {
            // Missing gdbus, an unavailable session bus, and unsupported file managers
            // all use the same safe fallback. Never launch the item's associated app.
        }

        var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(fullPath)) ?? fullPath;
        using var fallback = start(Command("xdg-open", parent));
        if (fallback == null) throw new InvalidOperationException("The file manager could not be started.");
    }

    private static ProcessStartInfo Command(string executable, params string[] arguments)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        return info;
    }

    internal interface IRevealProcess : IDisposable
    {
        bool WaitForExit(int milliseconds);
        int ExitCode { get; }
        void Kill();
    }

    private static IRevealProcess? Start(ProcessStartInfo info) => Process.Start(info) is { } process
        ? new RevealProcess(process) : null;

    private sealed class RevealProcess(Process process) : IRevealProcess
    {
        public bool WaitForExit(int milliseconds) => process.WaitForExit(milliseconds);
        public int ExitCode => process.ExitCode;
        public void Kill() => process.Kill(entireProcessTree: true);
        public void Dispose() => process.Dispose();
    }
}
