using Microsoft.Extensions.AI;

namespace Bakabase.Modules.AI.Components.Tools;

/// <summary>
/// Registry for all available LLM tools. Collects AIFunctions from all registered ILlmTool implementations.
/// </summary>
public class LlmToolRegistry(IEnumerable<ILlmTool> tools)
{
    private readonly Lazy<IReadOnlyList<AITool>> _allTools = new(() =>
        tools.SelectMany(t => t.GetFunctions()).Cast<AITool>().ToList());

    public IReadOnlyList<AITool> GetAllTools() => _allTools.Value;

    public ChatOptions WithTools(ChatOptions? options = null)
    {
        options ??= new ChatOptions();
        options.Tools = [.._allTools.Value];
        return options;
    }
}
