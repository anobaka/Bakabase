using Bakabase.Modules.Player.Abstractions.Components;
using Bakabase.Modules.Player.Abstractions.Models.Domain;

namespace Bakabase.Modules.Player.Components;

/// <summary>
/// Probes registry hints (Windows), well-known directories and PATH for a
/// known player's executables.
/// </summary>
public class DefaultPlayerExecutableLocator : IPlayerExecutableLocator
{
    public IReadOnlyList<string> Locate(KnownPlayerDefinition definition)
    {
        var found = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string? path)
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                var full = Path.GetFullPath(path);
                if (seen.Add(full))
                {
                    found.Add(full);
                }
            }
        }

        if (OperatingSystem.IsWindows())
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
            var pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (var dir in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                foreach (var name in definition.ExecutableNames)
                {
                    Add(Path.Combine(dir, name));
                    if (!OperatingSystem.IsWindows() &&
                        name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        Add(Path.Combine(dir, name[..^4]));
                    }
                }
            }
        }

        return found;
    }

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
