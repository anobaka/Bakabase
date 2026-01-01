using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Helpers;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using StackExchange.Profiling;

namespace Bakabase.Abstractions.Components.Cover;

public record CoverDiscoveryResult(bool IsVirtualPath, string Path, string Ext, byte[]? Data = null)
{
    /// <summary>
    /// The path may be an inner path inside a compressed file, video, etc. You should check its existence before apply io operations on it.
    /// </summary>
    public string Path { get; } = Path;

    public byte[]? Data { get; } = Data;
    private readonly string _ext = Ext;

    /// <summary>
    /// It means <see cref="Path"/> is not a real path if it is true.
    /// </summary>
    public bool IsVirtualPath { get; } = IsVirtualPath;

    public async Task<string> SaveTo(string pathWithoutExtension, bool overwrite, CancellationToken ct)
    {
        var path = $"{pathWithoutExtension}{_ext}";

        if (!overwrite && File.Exists(path))
        {
            throw new Exception(
                $"Failed to save cover, since there is already a file exists in [{path}] and {nameof(overwrite)} is not set to true.");
        }

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

        return path;
    }

    public async Task<Image<Argb32>> LoadByImageSharp(CancellationToken ct)
    {
        using (MiniProfiler.Current.Step("LoadByImageSharp.Inner"))
        {
            if (IsVirtualPath)
            {
                using (MiniProfiler.Current.Step($"Image.LoadAsync (from memory, {Data!.Length} bytes)"))
                {
                    return await Image.LoadAsync<Argb32>(new MemoryStream(Data!), ct);
                }
            }

            var ext = System.IO.Path.GetExtension(Path);
            if (ext == InternalOptions.IcoFileExtension)
            {
                byte[]? data;
                using (MiniProfiler.Current.Step("ExtractIconAsPng"))
                {
                    data = ImageHelpers.ExtractIconAsPng(Path);
                }

                using (MiniProfiler.Current.Step("Image.Load (from icon data)"))
                {
                    return Image.Load<Argb32>(data);
                }
            }

            using (MiniProfiler.Current.Step($"Image.LoadAsync (from file: {Path})"))
            {
                return await Image.LoadAsync<Argb32>(Path, ct);
            }
        }
    }
}