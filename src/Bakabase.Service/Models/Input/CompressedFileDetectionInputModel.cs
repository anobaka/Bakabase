using System.Text.Json.Serialization;

namespace Bakabase.Service.Models.Input;

public record CompressedFileDetectionInputModel
{
    public string[] Paths { get; set; } = [];
    public int? IncludeUnknownFilesLargerThanMb { get; set; }
}




