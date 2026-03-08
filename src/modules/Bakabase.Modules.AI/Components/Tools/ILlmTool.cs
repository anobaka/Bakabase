using Microsoft.Extensions.AI;

namespace Bakabase.Modules.AI.Components.Tools;

/// <summary>
/// Marker interface for classes that provide AI tool methods.
/// Methods should be decorated with [Description] attributes for LLM tool calling.
/// </summary>
public interface ILlmTool
{
    /// <summary>
    /// Returns the AIFunctions provided by this tool class.
    /// </summary>
    IEnumerable<AIFunction> GetFunctions();
}
