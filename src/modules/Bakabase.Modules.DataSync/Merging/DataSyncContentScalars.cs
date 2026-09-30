using System.Text.Json.Nodes;

namespace Bakabase.Modules.DataSync.Merging;

/// <summary>
/// The scalar paths of a kind's canonical content (§6.5, §8.11), which an apply's change list records with their
/// before and after values: every top-level member that is not an array, every member of a nested object as
/// <c>parent.member</c> (<c>settings.precision</c>), and <c>defaultValue</c> as a whole. Other arrays are child lists.
/// </summary>
public static class DataSyncContentScalars
{
    public static IReadOnlyDictionary<string, JsonNode?> Of(JsonObject content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var scalars = new SortedDictionary<string, JsonNode?>(StringComparer.Ordinal);
        foreach (var (name, value) in content)
        {
            switch (value)
            {
                case JsonArray when name == "defaultValue":
                    scalars[name] = value;
                    break;
                case JsonArray:
                    break;
                case JsonObject nested:
                    foreach (var (member, inner) in nested) scalars[name + "." + member] = inner;
                    break;
                default:
                    scalars[name] = value;
                    break;
            }
        }

        return scalars;
    }
}
