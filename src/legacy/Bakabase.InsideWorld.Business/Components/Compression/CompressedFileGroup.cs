using System.Collections.Generic;

namespace Bakabase.InsideWorld.Business.Components.Compression;

public class CompressedFileGroup
{
    public string KeyName { get; set; } = "";
    public string? Extension { get; set; }
    public List<string> Files { get; set; } = new();
    public List<long> FileSizes { get; set; } = new();

    public override string ToString()
    {
        return $"{KeyName} ({Extension ?? "unknown"}) → {Files.Count} files";
    }
}