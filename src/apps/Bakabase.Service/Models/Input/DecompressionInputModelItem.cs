namespace Bakabase.Service.Models.Input;

public record DecompressionInputModelItem
{
    public string Key { get; set; } = null!;
    public string Directory { get; set; } = null!;
    public string[] Files { get; set; } = [];
    public string? Password { get; set; }
    public bool DecompressToNewFolder { get; set; } = true;
    public bool DeleteAfterDecompression { get; set; }
    public bool MoveToParent { get; set; }
    public bool OverwriteExistFiles { get; set; }
}