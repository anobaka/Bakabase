namespace Bakabase.Abstractions.Models.Input;

public class MediaLibraryV2PatchInputModel
{
    public string? Name { get; set; }
    public List<string>? Paths { get; set; }
    public int? ResourceCount { get; set; }
    public string? Color { get; set; }
    public string? SyncVersion { get; set; }
    public int? TemplateId { get; set; }
}
