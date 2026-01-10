using Bakabase.InsideWorld.Business.Components.Dependency.Abstractions;

namespace Bakabase.Tests.Implementations;

public class TestDependencyLocalizer : IDependencyLocalizer
{
    public string? Dependency_Component_Name(string key) => $"Component_{key}";
    public string? Dependency_Component_Description(string key) => $"Description_{key}";
    public string Dependency_NotInstalled_Message(string dependencyDisplayName)
    {
        throw new System.NotImplementedException();
    }

    public string Dependency_Installing_Message(string dependencyDisplayName)
    {
        throw new System.NotImplementedException();
    }

    public string Dependency_Required_Message(string dependencyDisplayName, string requiredByDisplayName)
    {
        throw new System.NotImplementedException();
    }
}
