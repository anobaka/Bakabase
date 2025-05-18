namespace Bakabase.Modules.MediaLibraryTemplate.Abstractions.Models.Domain;

public record PathMediaLibraryTemplateBinding
{
    public string Path { get; set; } = null!;
    public List<int>? TemplateIdsChain { get; set; }
    public List<MediaLibraryTemplate>? TemplatesChain { get; set; }
}