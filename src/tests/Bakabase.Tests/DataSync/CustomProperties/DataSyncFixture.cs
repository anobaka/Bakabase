using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Dto;
using Bakabase.Modules.Property.Abstractions.Components;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Modules.Property.Components.Properties.Attachment;
using Bakabase.Modules.Property.Components.Properties.Choice;
using Bakabase.Modules.Property.Components.Properties.Choice.Abstractions;
using Bakabase.Modules.Property.Components.Properties.Multilevel;
using Bakabase.Modules.Property.Components.Properties.Number;
using Bakabase.Modules.Property.Components.Properties.Tags;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;

namespace Bakabase.Tests.DataSync.CustomProperties;

/// <summary>A property <see cref="DataSyncFixture.SeedCustomPropertiesAsync"/> added.</summary>
/// <param name="Id">Its local id in the seeded provider.</param>
public sealed record DataSyncFixtureProperty(int Id, string Name, PropertyType Type);

/// <summary>
/// The custom property part of the round-trip fixture (v3.1 §11.2 <c>DataSyncFixture</c>, §13.4 here), seeded through
/// the real <see cref="ICustomPropertyService"/>: all 16 types; IgnoreCase on and off; duplicate-equivalent labels under
/// IgnoreCase off (ordinal duplicates) and on (case variants, which only an IgnoreCase switched on after the fact
/// keeps, F72); tags with a group, with none and with <c>""</c>; a 3-level multilevel tree; colours; default values;
/// every type setting away from its default. Option uuids are fixed, so goldens can name them.
/// </summary>
/// <remarks>
/// A receiver that creates these properties folds the IgnoreCase duplicates into their first member (v3.1 H4):
/// <see cref="FoldedOnCreate"/> lists the option uuids it therefore never holds. The comparison form already treats
/// both sides as equal. The extension groups of the round trip are package C's.
/// </remarks>
public static class DataSyncFixture
{
    public const string GenreName = "Genre";
    public const string MoodName = "Mood";
    public const string StudioTagsName = "Studio tags";
    public const string KeywordsName = "Keywords";
    public const string RegionName = "Region";

    /// <summary>Option uuids of IgnoreCase duplicates, which a receiver's create folds away (v3.1 H4).</summary>
    public static IReadOnlySet<string> FoldedOnCreate { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "00000000-0000-4000-8000-000000000103", // Genre: "action" folds into "Action"
        "00000000-0000-4000-8000-000000000303", // Keywords: ("studio", "KYOTO") folds into ("Studio", "Kyoto")
        "00000000-0000-4000-8000-000000000405", // Region: "ASIA" folds into "Asia"; its child moves under "Asia"
    };

    /// <summary>
    /// The seeded properties, in seeding order. <c>IgnoreCaseAfterAdd</c> marks those added with IgnoreCase off and
    /// switched on afterwards, so the service keeps their case-variant duplicates.
    /// </summary>
    public static IReadOnlyList<(string Name, PropertyType Type, object? Options, bool IgnoreCaseAfterAdd)> Definitions { get; } =
    [
        ("Title", PropertyType.SingleLineText, null, false),
        ("Notes", PropertyType.MultilineText, null, false),
        ("Series", PropertyType.SingleChoice, new SingleChoicePropertyOptions
        {
            IgnoreCase = false,
            Choices =
            [
                Choice("00000000-0000-4000-8000-000000000001", "Main", "#e5484d"),
                Choice("00000000-0000-4000-8000-000000000002", "Spin-off"),
                // An ordinal duplicate under IgnoreCase off: kept by every side, one class.
                Choice("00000000-0000-4000-8000-000000000003", "Main"),
            ],
            DefaultValue = "00000000-0000-4000-8000-000000000002",
        }, false),
        (GenreName, PropertyType.MultipleChoice, new MultipleChoicePropertyOptions
        {
            IgnoreCase = false,
            Choices =
            [
                Choice("00000000-0000-4000-8000-000000000101", "Action", "#3e63dd"),
                Choice("00000000-0000-4000-8000-000000000102", "Drama"),
                Choice("00000000-0000-4000-8000-000000000103", "action"),
            ],
            DefaultValue = ["00000000-0000-4000-8000-000000000102", "00000000-0000-4000-8000-000000000101"],
        }, true),
        (MoodName, PropertyType.MultipleChoice, new MultipleChoicePropertyOptions
        {
            IgnoreCase = true,
            Choices =
            [
                Choice("00000000-0000-4000-8000-000000000201", "Calm", "#30a46c"),
                Choice("00000000-0000-4000-8000-000000000202", "Tense"),
            ],
        }, false),
        ("Score", PropertyType.Number, new NumberPropertyOptions { Precision = 2 }, false),
        ("Progress", PropertyType.Percentage, new PercentagePropertyOptions { Precision = 1, ShowProgressBar = true }, false),
        ("My rating", PropertyType.Rating, new RatingPropertyOptions { MaxValue = 10 }, false),
        ("Watched", PropertyType.Boolean, null, false),
        ("Homepage", PropertyType.Link, null, false),
        ("Gallery", PropertyType.Attachment, new AttachmentPropertyOptions { Layout = AttachmentLayout.Carousel }, false),
        ("Released", PropertyType.Date, null, false),
        ("Imported", PropertyType.DateTime, null, false),
        ("Length", PropertyType.Time, null, false),
        ("Computed", PropertyType.Formula, null, false),
        (StudioTagsName, PropertyType.Tags, new TagsPropertyOptions
        {
            IgnoreCase = false,
            Tags =
            [
                Tag("00000000-0000-4000-8000-000000000301", "Studio", "Kyoto", "#3e63dd"),
                Tag("00000000-0000-4000-8000-000000000302", null, "Isekai"),
                // Written with "", stored as no group: TagValue keeps "" as none.
                Tag("00000000-0000-4000-8000-000000000304", "", "Mecha"),
            ],
        }, false),
        (KeywordsName, PropertyType.Tags, new TagsPropertyOptions
        {
            IgnoreCase = false,
            Tags =
            [
                Tag("00000000-0000-4000-8000-000000000305", "Studio", "Kyoto"),
                Tag("00000000-0000-4000-8000-000000000306", null, "Short"),
                Tag("00000000-0000-4000-8000-000000000303", "studio", "KYOTO", "#e5484d"),
            ],
        }, true),
        (RegionName, PropertyType.Multilevel, new MultilevelPropertyOptions
        {
            IgnoreCase = false,
            ValueIsSingleton = true,
            Data =
            [
                Node("00000000-0000-4000-8000-000000000401", "Asia", "#30a46c",
                    Node("00000000-0000-4000-8000-000000000402", "Japan", null,
                        Node("00000000-0000-4000-8000-000000000403", "Kyoto", "#e5484d"))),
                Node("00000000-0000-4000-8000-000000000404", "Europe", null),
                Node("00000000-0000-4000-8000-000000000405", "ASIA", null,
                    Node("00000000-0000-4000-8000-000000000406", "Korea", null)),
            ],
            DefaultValue = ["00000000-0000-4000-8000-000000000403"],
        }, true),
    ];

    /// <summary>
    /// Adds <see cref="Definitions"/> through <see cref="ICustomPropertyService"/> (one <c>AddRange</c>, then a
    /// <c>Put</c> per property whose IgnoreCase is switched on afterwards) and returns them in the same order.
    /// </summary>
    public static async Task<IReadOnlyList<DataSyncFixtureProperty>> SeedCustomPropertiesAsync(IServiceProvider sp)
    {
        var service = sp.GetRequiredService<ICustomPropertyService>();
        var added = await service.AddRange(Definitions.Select(d => new CustomPropertyAddOrPutDto
        {
            Name = d.Name, Type = d.Type, Options = Serialize(d.Options, ignoreCase: d.IgnoreCaseAfterAdd ? false : null),
        }).ToArray());
        for (var i = 0; i < Definitions.Count; i++)
        {
            var d = Definitions[i];
            if (!d.IgnoreCaseAfterAdd) continue;
            // Put passes the stored options as the previous ones: the normalizer never folds a stored option (F72).
            await service.Put(added[i].Id, new CustomPropertyAddOrPutDto
            {
                Name = d.Name, Type = d.Type, Options = Serialize(d.Options, ignoreCase: true),
            });
        }

        return added.Select((p, i) => new DataSyncFixtureProperty(p.Id, Definitions[i].Name, Definitions[i].Type))
            .ToList();
    }

    private static string? Serialize(object? options, bool? ignoreCase)
    {
        if (options is null) return null;
        var json = JsonConvert.SerializeObject(options);
        if (ignoreCase is not { } flag) return json;
        var copy = JsonConvert.DeserializeObject(json, options.GetType())!;
        ((IReferencePropertyOptions) copy).IgnoreCase = flag;
        return JsonConvert.SerializeObject(copy);
    }

    private static ChoiceOptions Choice(string uuid, string label, string? color = null) =>
        new() { Value = uuid, Label = label, Color = color };

    private static TagsPropertyOptions.TagOptions Tag(string uuid, string? group, string name, string? color = null) =>
        new(group, name) { Value = uuid, Color = color };

    private static MultilevelDataOptions Node(string uuid, string label, string? color,
        params MultilevelDataOptions[] children) =>
        new() { Value = uuid, Label = label, Color = color!, Children = children.Length == 0 ? null : children.ToList() };
}
