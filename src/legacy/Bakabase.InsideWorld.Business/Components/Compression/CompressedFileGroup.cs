using System.Collections.Generic;
using System.IO;

namespace Bakabase.InsideWorld.Business.Components.Compression;

public class CompressedFileGroup
{
    public string KeyName { get; set; } = "";
    public string? Extension { get; set; }
    public List<string> Files { get; set; } = new();
    public List<long> FileSizes { get; set; } = new();

    public static T FromSingleFile<T>(string path) where T : CompressedFileGroup, new() => new()
    {
        KeyName = Path.GetFileNameWithoutExtension(path)?.Trim() ?? "",
        Extension = Path.GetExtension(path),
        Files = new List<string> { path }
    };

    public override string ToString()
    {
        return $"{KeyName} ({Extension ?? "unknown"}) → {Files.Count} files";
    }
}