using System.Text.RegularExpressions;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Domain;

namespace Bakabase.Modules.Player.Components;

/// <summary>File enumeration only: no resource lookup and no cache writes. Both desktop discovery and
/// remote workers use the same extension/pattern semantics, with optional worker safety budgets.</summary>
public static class PlayableFileDiscovery
{
    public static async Task<string[]> DiscoverAsync(string path, bool isFile,
        ResourceProfilePlayableFileOptions? options, CancellationToken ct,
        Func<string, CancellationToken, Task>? onFile = null, int? maximumFiles = null)
    {
        _ = File.GetAttributes(path);
        if (options?.Extensions is not {Count: > 0}) return [];
        var extensions = options.Extensions.Select(e => e.StartsWith('.') ? e : $".{e}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Regex? regex = null;
        if (!string.IsNullOrEmpty(options.FileNamePattern))
        {
            try { regex = new Regex(options.FileNamePattern, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2)); }
            catch (ArgumentException) { /* Match legacy invalid-pattern behavior: ignore it. */ }
        }
        var result = new List<string>();
        var visited = 0;
        foreach (var file in isFile ? new[] {path} : Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            if (maximumFiles.HasValue && ++visited > maximumFiles.Value)
                throw new InvalidOperationException("The resource exceeds the processing file budget.");
            if (onFile != null) await onFile(file, ct);
            if (extensions.Contains(Path.GetExtension(file)) && (regex == null || regex.IsMatch(Path.GetFileName(file))))
                result.Add(file.StandardizePath()!);
        }
        return result.ToArray();
    }
}
