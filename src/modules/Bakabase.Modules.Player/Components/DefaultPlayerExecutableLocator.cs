using System.Runtime.InteropServices;
using Bakabase.Modules.Player.Abstractions.Components;
using Bakabase.Modules.Player.Abstractions.Models.Domain;

namespace Bakabase.Modules.Player.Components;

/// <summary>
/// Probes registry hints (Windows), well-known directories and PATH for a
/// known player's executables.
/// </summary>
public class DefaultPlayerExecutableLocator : IPlayerExecutableLocator
{
    // These environmental seams keep discovery tests independent of installed software.
    protected virtual bool IsWindows => OperatingSystem.IsWindows();
    protected virtual bool IsMacOS => OperatingSystem.IsMacOS();
    protected virtual string UserHomeDirectory => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    protected virtual string SystemApplicationsDirectory => "/Applications";
    protected virtual string SearchPath => Environment.GetEnvironmentVariable("PATH") ?? string.Empty;

    public IReadOnlyList<string> Locate(KnownPlayerDefinition definition)
    {
        var found = new List<string>();
        var seen = new HashSet<string>(IsWindows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        void Add(string? path)
        {
            if (!string.IsNullOrWhiteSpace(path) && IsExecutable(path))
            {
                var full = Path.GetFullPath(path);
                if (seen.Add(full))
                {
                    found.Add(full);
                }
            }
        }

        if (IsWindows)
        {
            foreach (var hint in definition.RegistryHints)
            {
                var value = ReadRegistryValue(hint);
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                if (hint.ValueIsDirectory)
                {
                    foreach (var name in definition.ExecutableNames)
                    {
                        Add(Path.Combine(value, name));
                    }
                }
                else
                {
                    Add(value);
                }
            }
        }

        if (IsMacOS)
        {
            foreach (var relativePath in definition.MacAppBundleExecutables)
            {
                Add(Path.Combine(SystemApplicationsDirectory, relativePath));
                if (!string.IsNullOrEmpty(UserHomeDirectory))
                {
                    Add(Path.Combine(UserHomeDirectory, "Applications", relativePath));
                }
            }
        }

        foreach (var dir in definition.CandidateDirectories)
        {
            var expanded = Environment.ExpandEnvironmentVariables(dir);
            // Unexpanded variables (e.g. %ProgramFiles% on Linux) stay verbatim;
            // skip those instead of probing a literal "%..." path.
            if (expanded.Contains('%'))
            {
                continue;
            }

            foreach (var name in definition.ExecutableNames)
            {
                Add(Path.Combine(expanded, name));
            }
        }

        if (definition.SearchInPath)
        {
            var pathValue = SearchPath;
            foreach (var dir in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                foreach (var name in definition.ExecutableNames)
                {
                    Add(Path.Combine(dir, name));
                    if (!IsWindows &&
                        name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        Add(Path.Combine(dir, name[..^4]));
                    }
                }
            }
        }

        return found;
    }

    private static bool IsExecutable(string path)
    {
        if (!File.Exists(path)) return false;
        // access(X_OK) observes permissions for this process, including directory
        // traversal and ACLs; merely checking a Unix mode bit is insufficient.
        // GetBinaryType rejects text files named .exe on Windows.
        return OperatingSystem.IsWindows()
            ? GetBinaryType(path, out _)
            : Access(path, 1) == 0;
    }

    [DllImport("libc", EntryPoint = "access", SetLastError = true)]
    private static extern int Access([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int mode);

    [DllImport("kernel32.dll", EntryPoint = "GetBinaryTypeW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetBinaryType(string path, out uint binaryType);

    private static string? ReadRegistryValue(RegistryHint hint)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            var root = hint.Hive.Equals("HKCU", StringComparison.OrdinalIgnoreCase)
                ? Microsoft.Win32.Registry.CurrentUser
                : Microsoft.Win32.Registry.LocalMachine;
            using var key = root.OpenSubKey(hint.SubKey);
            return key?.GetValue(hint.ValueName) as string;
        }
        catch
        {
            // Registry access may fail; discovery falls back to directory probes.
            return null;
        }
    }
}
