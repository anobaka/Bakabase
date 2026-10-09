using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Modules.Acquisition.Abstractions.Components;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Modules.Acquisition.Components;

/// <summary>
/// User inputs obey the storage policy. Work and processing directories are allocated by the
/// workflow itself and may live inside appdata; they are not general-purpose user storage roots.
/// </summary>
public static class AcquisitionStoragePaths
{
    public static void EnsureSourceAllowed(AcquisitionStepContext context, AcquisitionWorkItem item, string path)
    {
        var policy = context.ServiceProvider.GetRequiredService<IUserStoragePolicy>();
        if (!policy.IsRestricted) return;
        var fullPath = Path.GetFullPath(path);
        foreach (var directory in new[] {context.WorkingDirectory, item.ProcessingStateDirectory})
        {
            if (string.IsNullOrWhiteSpace(directory)) continue;
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
            if (fullPath != root && !fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                continue;
            // Never grant the internal-directory exception through a link into external storage.
            var cursor = fullPath;
            while (cursor.Length >= root.Length)
            {
                if ((File.Exists(cursor) || Directory.Exists(cursor)) &&
                    (File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Symbolic links cannot be used as internal acquisition inputs.");
                if (cursor == root) return;
                cursor = Path.GetDirectoryName(cursor)!;
            }
        }
        policy.EnsurePathAllowed(path);
    }
}
