namespace Bakabase.Abstractions.Components.ResourceMove;

public interface IResourceMovePanelSettings
{
    bool AutoOverwrite { get; }
    Task SetAutoOverwrite(bool enabled);
}
