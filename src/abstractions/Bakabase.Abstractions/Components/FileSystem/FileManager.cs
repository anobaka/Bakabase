using Bakabase.Abstractions.Extensions;
using Bakabase.Infrastructures.Components.App;

namespace Bakabase.Abstractions.Components.FileSystem;

public class FileManager(AppService appService) : IFileManager
{
    public string BaseDir => Path.Combine(appService.AppDataDirectory, "data").StandardizePath()!;

    public string BuildAbsolutePath(params object[] segmentsAfterBaseDir)
    {
        return Path.Combine([BaseDir, ..segmentsAfterBaseDir.Select(s => s.ToString()!)]).StandardizePath()!;
    }

    public async Task<string> Save(string path, byte[] data, CancellationToken ct)
    {
        var absolutePath = BuildAbsolutePath(path);
        var dir = Path.GetDirectoryName(absolutePath)!;
        Directory.CreateDirectory(dir);
        await File.WriteAllBytesAsync(absolutePath, data, ct);
        return absolutePath;
    }

    public string GetLocalCoverPathWithoutExtension(int resourceId) =>
        BuildAbsolutePath("covers", "local", resourceId.ToString());

    public string GetSourceCoverDir(string source, int resourceId) =>
        BuildAbsolutePath("covers", "source", source, resourceId.ToString());

    public string GetManualCoverPath(int resourceId, int index) =>
        BuildAbsolutePath("covers", "manual", $"{resourceId}-{index}.jpg");

    public string GetEnhancerFilePath(string enhancerId, string fileName) =>
        BuildAbsolutePath("enhancers", enhancerId, fileName);

    public string GetEnhancerResourceFilePath(string enhancerId, string resourceIdPart, string fileName) =>
        BuildAbsolutePath("enhancers", enhancerId, resourceIdPart, fileName);

    public string GetAttachmentPath(string fileName) =>
        BuildAbsolutePath("attachments", fileName);
}
