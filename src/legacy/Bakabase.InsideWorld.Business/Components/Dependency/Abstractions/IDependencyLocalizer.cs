namespace Bakabase.InsideWorld.Business.Components.Dependency.Abstractions;

public interface IDependencyLocalizer
{
    string? Dependency_Component_Name(string key);
    string? Dependency_Component_Description(string key);
    string Dependency_NotInstalled_Message(string dependencyDisplayName);
    string Dependency_Installing_Message(string dependencyDisplayName);
    string Dependency_Required_Message(string dependencyDisplayName, string requiredByDisplayName);
}