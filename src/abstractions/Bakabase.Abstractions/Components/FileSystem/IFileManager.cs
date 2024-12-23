namespace Bakabase.Abstractions.Components.FileSystem;

public interface IFileManager
{
    string BaseDir { get; }
    string BuildAbsolutePath(params object[] segmentsAfterBaseDir);
    Task<string> Save(string relativePath, byte[] data, CancellationToken ct);
}