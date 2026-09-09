using System.Text.Json;
using System.Text.Json.Serialization;
using Bakabase.Modules.Workflow.Abstractions.Components;

namespace Bakabase.Modules.Workflow.Components;

/// <summary>
/// Stores an item on the run row and reads it back later, possibly in another process.
/// <para>
/// The type travels with the value because nothing else knows it: a chain's item type changes as
/// transforms replace items, and the run row is read long after the chain that produced it has
/// been forgotten. Carrying it makes the snapshot self-describing at the cost of one string.
/// </para>
/// </summary>
public static class WorkflowItemSnapshot
{
    private record Envelope(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("item")] JsonElement Item);

    public static string Capture(object item)
    {
        var type = item.GetType();
        // Namespace-qualified name plus the assembly's simple name — enough for Type.GetType to
        // find it, without pinning a version that would break on every release.
        var typeName = $"{type.FullName}, {type.Assembly.GetName().Name}";

        return JsonSerializer.Serialize(new
        {
            type = typeName,
            item = JsonSerializer.SerializeToElement(item, type, WorkflowJson.Options)
        }, WorkflowJson.Options);
    }

    /// <exception cref="InvalidOperationException">
    /// The snapshot is malformed, or names a type this build no longer has — an activity removed
    /// or renamed between the suspension and the resume.
    /// </exception>
    public static object Restore(string json)
    {
        var envelope = JsonSerializer.Deserialize<Envelope>(json, WorkflowJson.Options)
                       ?? throw new InvalidOperationException("The item snapshot is empty.");

        var type = Type.GetType(envelope.Type)
                   ?? throw new InvalidOperationException(
                       $"The item snapshot names '{envelope.Type}', which this build does not have.");

        return envelope.Item.Deserialize(type, WorkflowJson.Options)
               ?? throw new InvalidOperationException(
                   $"The item snapshot for '{envelope.Type}' deserialized to null.");
    }
}
