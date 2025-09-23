using Bakabase.Service.Models.Input.Constants;

namespace Bakabase.Service.Models.Input;

public record DecompressionStartInputModelItem
{
    public string File { get; set; } = null!;
    public string? Password { get; set; }
    public bool DeleteCompressedAfter { get; set; }
    public ExtractActionAfterDecompression ExtractAction { get; set; } = ExtractActionAfterDecompression.None;
}