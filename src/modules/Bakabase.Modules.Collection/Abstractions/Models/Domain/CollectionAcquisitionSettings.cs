using System.Text.Json;

namespace Bakabase.Modules.Collection.Abstractions.Models.Domain;

/// <summary>
/// What a collection wants done differently when it acquires something.
/// <para>
/// A collection is usually a kind of thing — a circle's works, a series, a board's shares — and
/// the way to get one is the way to get all of them. Saying it once on the collection beats
/// choosing per item, which nobody would do for two hundred members.
/// </para>
/// </summary>
public record CollectionAcquisitionSettings
{
    /// <summary>
    /// Which recipe to run. Null lets the lead decide, which is right until it is not: a board
    /// whose shares always come as split archives wants its own recipe every time.
    /// </summary>
    public int? RecipeDefinitionId { get; init; }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Reads the settings a collection stores. A collection with none, or with something
    /// unreadable, acquires the ordinary way rather than not at all.
    /// </summary>
    public static CollectionAcquisitionSettings? Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            return JsonSerializer.Deserialize<CollectionAcquisitionSettings>(json, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public string Serialize() => JsonSerializer.Serialize(this, Json);
}
