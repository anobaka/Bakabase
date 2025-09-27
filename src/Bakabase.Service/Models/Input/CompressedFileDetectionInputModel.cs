using System.Text.Json.Serialization;

namespace Bakabase.Service.Models.Input;

public record CompressedFileDetectionInputModel
{
    public string[] Paths { get; set; } = [];
    public bool IncludeUnknownFiles { get; set; } = false;
    public int? UnknownFilesMinMb { get; set; }
}




