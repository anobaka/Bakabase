namespace Bakabase.Abstractions.Components;

public interface IThumbnailManager
{
    Task<string> Get(string path, CancellationToken ct);
}