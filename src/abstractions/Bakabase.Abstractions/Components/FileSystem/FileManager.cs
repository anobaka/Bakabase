using Bakabase.Abstractions.Extensions;
using Bakabase.Infrastructures.Components.App;

namespace Bakabase.Abstractions.Components.FileSystem;

public class FileManager : IFileManager
{
    public string BaseDir { get; } = Path.Combine(AppService.DefaultAppDataDirectory, "data").StandardizePath()!;

    public string BuildAbsolutePath(params string[] segments)
    {
        return Path.Combine([BaseDir, ..segments]).StandardizePath()!;
    }

    public async Task<string> Save(string filename, byte[] data, CancellationToken ct)
    {
        var path = BuildAbsolutePath(filename);
        await File.WriteAllBytesAsync(path, data, ct);
        return path;
    }
}