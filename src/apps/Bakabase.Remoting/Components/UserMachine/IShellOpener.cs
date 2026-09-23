using System.Diagnostics;
using Bakabase.Infrastructures.Components.App;

namespace Bakabase.Remoting.Components.UserMachine;

/// <summary>
/// The one place the client asks the operating system to open something.
/// </summary>
/// <remarks>
/// A seam rather than a direct call, because everything interesting about these handlers
/// happens before it: translating a path, refusing one that maps nowhere, falling back to
/// a parent folder, rejecting a scheme that is a program launch in disguise. Those are
/// the decisions worth testing, and none of them can be tested if reaching them means
/// spawning a file manager.
/// </remarks>
public interface IShellOpener
{
    /// <summary>
    /// Shows a file or folder in the file manager.
    /// </summary>
    /// <param name="inParentDirectory">
    /// True to open the containing folder with the item selected, false to open the item
    /// itself.
    /// </param>
    void Reveal(string path, bool inParentDirectory);

    /// <summary>Hands a file or a URL to whatever the OS associates with it.</summary>
    void Launch(string target);

    /// <summary>
    /// Starts a specific program with arguments, for when the user chose a player rather
    /// than leaving it to the OS.
    /// </summary>
    /// <param name="useShellExecute">
    /// True for a Windows shortcut, which the OS refuses to start any other way.
    /// </param>
    void LaunchProcess(string executable, string arguments, bool useShellExecute);

    /// <summary>
    /// Runs a downloaded program from its own folder.
    /// </summary>
    /// <param name="workingDirectory">
    /// Null to let the OS pick. A game that reads its data with relative paths finds
    /// nothing when started from somewhere else, so this is not optional for one.
    /// </param>
    void LaunchProgram(string path, string? workingDirectory);
}

public sealed class OsShellOpener : IShellOpener
{
    public void Reveal(string path, bool inParentDirectory) => OsShell.Open(path, inParentDirectory);

    public void Launch(string target) => Process.Start(new ProcessStartInfo(target) {UseShellExecute = true});

    public void LaunchProcess(string executable, string arguments, bool useShellExecute) =>
        Process.Start(new ProcessStartInfo(executable)
        {
            Arguments = arguments,
            UseShellExecute = useShellExecute
        });

    public void LaunchProgram(string path, string? workingDirectory) =>
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true,
            WorkingDirectory = workingDirectory ?? string.Empty
        });
}
