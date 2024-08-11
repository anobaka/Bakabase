using Bakabase.Abstractions.Components.Configuration;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Bakabase.Abstractions.Components.Cover;


public record CoverDiscoveryResult(string Path, byte[]? Data)
{
    /// <summary>
    /// The path may be an inner path inside a compressed file, video, etc. You should check its existence before apply io operations on it.
    /// </summary>
    public string Path { get; set; } = Path;

    public byte[]? Data { get; set; } = Data;

    public async Task SaveTo(string path, bool overwrite, CancellationToken ct)
    {
        if (!overwrite && File.Exists(path))
        {
            throw new Exception(
                $"Failed to save cover, since there is already a file exists in [{path}] and {nameof(overwrite)} is not set to true.");
        }

        var image = Data != null ? Image.Load<Rgb24>(Data) : await Image.LoadAsync<Rgb24>(Path, ct);

        if (Data != null)
        {
            await File.WriteAllBytesAsync(path, Data, ct);
        }
        else
        {
            await using var fs = new FileStream(Path, FileMode.Open);
            await using var to = new FileStream(path, FileMode.Truncate);
            await fs.CopyToAsync(to, ct);
        }
    }
}