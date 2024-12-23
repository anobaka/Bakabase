using Bakabase.Abstractions.Extensions;
using Bakabase.Infrastructures.Components.App;

namespace Bakabase.Abstractions.Components.FileSystem;

public class FileManager : IFileManager
{
    public string BaseDir { get; } = Path.Combine(AppService.DefaultAppDataDirectory, "data").StandardizePath()!;

    public string BuildAbsolutePath(params object[] segmentsAfterBaseDir)
    {
        return Path.Combine([BaseDir, ..segmentsAfterBaseDir.Select(s => s.ToString()!)]).StandardizePath()!;
    }

    public async Task<string> Save(string relativePath, byte[] data, CancellationToken ct)
    {
        var path = BuildAbsolutePath(relativePath);
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        await File.WriteAllBytesAsync(path, data, ct);
        return path;
    }
}