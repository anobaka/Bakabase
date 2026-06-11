using Bakabase.Modules.Player.Abstractions.Components;
using Bakabase.Modules.Player.Abstractions.Models.Domain;

namespace Bakabase.Modules.Player.Tests.Helpers;

internal sealed class FakePlayerExecutableLocator : IPlayerExecutableLocator
{
    private readonly Dictionary<string, List<string>> _pathsByDefinitionId = [];

    public int LocateCallCount { get; private set; }

    public void Set(string definitionId, params string[] paths)
        => _pathsByDefinitionId[definitionId] = paths.ToList();

    public IReadOnlyList<string> Locate(KnownPlayerDefinition definition)
    {
        LocateCallCount++;
        return _pathsByDefinitionId.GetValueOrDefault(definition.Id) ?? [];
    }
}
