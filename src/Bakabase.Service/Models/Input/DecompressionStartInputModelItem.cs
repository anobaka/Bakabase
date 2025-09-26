
namespace Bakabase.Service.Models.Input;

public record DecompressionStartInputModelItem
{
    public string File { get; set; } = null!;
    public string? Password { get; set; }
    public bool DecompressToNewFolder { get; set; } = true;
    public bool DeleteAfterDecompression { get; set; }
    public bool MoveToParent { get; set; }
}