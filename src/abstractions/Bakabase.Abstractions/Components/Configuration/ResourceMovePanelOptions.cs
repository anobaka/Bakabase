using Bootstrap.Components.Configuration.Abstractions;

namespace Bakabase.Abstractions.Components.Configuration;

[Options(fileKey: "resource-move-panel")]
public class ResourceMovePanelOptions
{
    public List<ResourceMoveDestination> Destinations { get; set; } = [];
    public bool AutoOverwrite { get; set; }
    public long Revision { get; set; }
}

public record ResourceMoveDestination
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Path { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string Scope { get; set; } = "tab";
    public string? TabId { get; set; }
    public int Order { get; set; }
    public bool IsDeleted { get; set; }
}
