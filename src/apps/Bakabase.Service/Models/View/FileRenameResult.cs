namespace Bakabase.Service.Models.View;

public class FileRenameResult
{
    public string OldPath { get; set; } = string.Empty;
    public string NewPath { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? Error { get; set; }
} 