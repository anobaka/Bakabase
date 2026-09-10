using System.Text.Json;
using System.Text.Json.Nodes;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Vndb;

/// <summary>
/// The bodies VNDB's query API is asked with.
/// <para>
/// Kept apart from the sending of them because a filter written wrongly does not fail — it returns
/// a different, plausible list, and a subscription would quietly collect the wrong works. The
/// shapes are what can be pinned down; the network cannot.
/// </para>
/// </summary>
public static class VndbRequests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>The most VNDB will return at once.</summary>
    public const int MaxResultsPerPage = 100;

    /// <summary>What is read about each visual novel in a listing.</summary>
    public const string ListingFields = "id, title, alttitle, released, image.url";

    /// <summary>What is read when the question is "what belongs with this one".</summary>
    public const string RelationFields =
        "id, title, alttitle, relations.id, relations.title, relations.relation, relations.relation_official, relations.image.url";

    /// <summary>Everything a developer has made.</summary>
    /// <param name="producerId">A producer id, <c>p1</c> shaped.</param>
    public static string ByDeveloper(string producerId, int page) =>
        Query(new JsonArray("developer", "=", new JsonArray("id", "=", producerId)), ListingFields, page);

    /// <summary>One visual novel and what VNDB says belongs with it.</summary>
    public static string Relations(string vnId) =>
        Query(new JsonArray("id", "=", vnId), RelationFields, 1);

    /// <summary>One visual novel, for putting a name to an id.</summary>
    public static string One(string vnId) => Query(new JsonArray("id", "=", vnId), ListingFields, 1);

    private static string Query(JsonArray filters, string fields, int page) =>
        new JsonObject
        {
            ["filters"] = filters,
            ["fields"] = fields,
            ["results"] = MaxResultsPerPage,
            ["page"] = page
        }.ToJsonString(Json);
}
