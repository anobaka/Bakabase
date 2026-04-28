using System.Collections.Generic;
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

    /// <summary>
    /// Returns metadata about each tool function for configuration UI.
    /// </summary>
    IEnumerable<LlmToolMetadata> GetMetadata();
}

public record LlmToolMetadata
{
    /// <summary>
    /// Unique name matching the AIFunction name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Human-readable description for UI display.
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// If true, this tool only reads data and does not modify anything.
    /// Read-only tools are enabled by default.
    /// </summary>
    public bool IsReadOnly { get; init; }
}
