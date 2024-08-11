namespace Bakabase.Abstractions.Components.FileManager;

public interface IFileManager
{
    string BaseDir { get; }
    string BuildAbsolutePath(string relativePath);
    Task<string> Save(string filename, byte[] data, CancellationToken ct);
    string? GetUri(string filename);
}