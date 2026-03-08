namespace Bakabase.Modules.Enhancer.Components.Enhancers.AI;

public record AiEnhancerContext
{
    /// <summary>
    /// Dynamic properties extracted by AI. Key = property name, Value = list of values.
    /// </summary>
    public Dictionary<string, List<string>> Properties { get; set; } = new();
}
