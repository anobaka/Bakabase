using Bakabase.Abstractions.Extensions;
using Bakabase.Infrastructures.Components.App;

namespace Bakabase.Abstractions.Components.FileSystem;

public class FileManager(AppService appService) : IFileManager
{
    public string BaseDir => Path.Combine(appService.AppDataDirectory, AppDataStorage.DataDirectoryName).StandardizePath()!;

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
        AppDataStorage.Covers.GetPath(appService.AppDataDirectory, "local", resourceId.ToString()).StandardizePath()!;

    public string GetSourceCoverDir(string source, int resourceId) =>
        AppDataStorage.Covers.GetPath(appService.AppDataDirectory, "source", source, resourceId.ToString()).StandardizePath()!;

    public string GetManualCoverPath(int resourceId, int index) =>
        AppDataStorage.Covers.GetPath(appService.AppDataDirectory, "manual", $"{resourceId}-{index}.jpg").StandardizePath()!;

    public string GetEnhancerFilePath(string enhancerId, string fileName) =>
        AppDataStorage.EnhancerFiles.GetPath(appService.AppDataDirectory, enhancerId, fileName).StandardizePath()!;

    public string GetEnhancerResourceFilePath(string enhancerId, string resourceIdPart, string fileName) =>
        AppDataStorage.EnhancerFiles.GetPath(appService.AppDataDirectory, enhancerId, resourceIdPart, fileName).StandardizePath()!;

    public string GetAttachmentPath(string fileName) =>
        AppDataStorage.Attachments.GetPath(appService.AppDataDirectory, fileName).StandardizePath()!;

    public string GetAigcGeneratorDir(int generatorId) =>
        AppDataStorage.Aigc.GetPath(appService.AppDataDirectory, $"g{generatorId}").StandardizePath()!;

    public string GetAigcRunDir(int generatorId, int runId) =>
        AppDataStorage.Aigc.GetPath(appService.AppDataDirectory, $"g{generatorId}", $"r{runId}").StandardizePath()!;

    public string GetAigcArtifactRelativePath(int generatorId, int runId, string fileName) =>
        Path.Combine(AppDataStorage.AigcDirectoryName, $"g{generatorId}", $"r{runId}", fileName).StandardizePath()!;
}
