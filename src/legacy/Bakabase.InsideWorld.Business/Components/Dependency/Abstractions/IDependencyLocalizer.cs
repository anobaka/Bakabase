namespace Bakabase.InsideWorld.Business.Components.Dependency.Abstractions;

public interface IDependencyLocalizer
{
    string? Dependency_Component_Name(string key);
    string? Dependency_Component_Description(string key);
}