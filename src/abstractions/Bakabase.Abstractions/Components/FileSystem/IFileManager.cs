namespace Bakabase.Abstractions.Components.FileSystem;

public interface IFileManager
{
    string BaseDir { get; }
    string BuildAbsolutePath(params object[] segments);
    Task<string> Save(string filename, byte[] data, CancellationToken ct);
}