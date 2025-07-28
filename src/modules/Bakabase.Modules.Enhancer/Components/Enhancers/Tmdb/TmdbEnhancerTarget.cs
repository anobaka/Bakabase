using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Enhancer.Abstractions.Attributes;
using Bakabase.Modules.Enhancer.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Enhancer.Components.EnhancementConverters;

namespace Bakabase.Modules.Enhancer.Components.Enhancers.Tmdb;

public enum TmdbEnhancerTarget
{
    [EnhancerTarget(StandardValueType.String, PropertyType.SingleLineText,
        [EnhancerTargetOptionsItem.AutoBindProperty])]
    Title = 1,

    [EnhancerTarget(StandardValueType.String, PropertyType.SingleLineText,
        [EnhancerTargetOptionsItem.AutoBindProperty])]
    OriginalTitle = 2,

    [EnhancerTarget(StandardValueType.String, PropertyType.MultilineText,
        [EnhancerTargetOptionsItem.AutoBindProperty],
        reservedPropertyCandidate: ReservedProperty.Introduction)]
    Overview = 3,

    [EnhancerTarget(StandardValueType.Decimal, PropertyType.Rating,
        [EnhancerTargetOptionsItem.AutoBindProperty], false, typeof(RatingMax10),
        reservedPropertyCandidate: ReservedProperty.Rating)]
    Rating = 4,

    [EnhancerTarget(StandardValueType.Decimal, PropertyType.Number,
        [EnhancerTargetOptionsItem.AutoBindProperty])]
    VoteCount = 5,

    [EnhancerTarget(StandardValueType.DateTime, PropertyType.DateTime,
        [EnhancerTargetOptionsItem.AutoBindProperty])]
    ReleaseDate = 6,

    [EnhancerTarget(StandardValueType.Decimal, PropertyType.Number,
        [EnhancerTargetOptionsItem.AutoBindProperty])]
    Runtime = 7,

    [EnhancerTarget(StandardValueType.ListTag, PropertyType.Tags,
        [EnhancerTargetOptionsItem.AutoBindProperty])]
    Genres = 8,

    [EnhancerTarget(StandardValueType.ListString, PropertyType.MultipleChoice,
        [EnhancerTargetOptionsItem.AutoBindProperty])]
    ProductionCountries = 9,

    [EnhancerTarget(StandardValueType.ListString, PropertyType.MultipleChoice,
        [EnhancerTargetOptionsItem.AutoBindProperty])]
    SpokenLanguages = 10,

    [EnhancerTarget(StandardValueType.String, PropertyType.SingleLineText,
        [EnhancerTargetOptionsItem.AutoBindProperty])]
    Status = 11,

    [EnhancerTarget(StandardValueType.String, PropertyType.SingleLineText,
        [EnhancerTargetOptionsItem.AutoBindProperty])]
    Tagline = 12,

    [EnhancerTarget(StandardValueType.Decimal, PropertyType.Number,
        [EnhancerTargetOptionsItem.AutoBindProperty])]
    Budget = 13,

    [EnhancerTarget(StandardValueType.Decimal, PropertyType.Number,
        [EnhancerTargetOptionsItem.AutoBindProperty])]
    Revenue = 14,

    [EnhancerTarget(StandardValueType.ListString, PropertyType.Attachment,
        [EnhancerTargetOptionsItem.AutoBindProperty, EnhancerTargetOptionsItem.CoverSelectOrder])]
    Cover = 15,

    [EnhancerTarget(StandardValueType.ListString, PropertyType.Attachment,
        [EnhancerTargetOptionsItem.AutoBindProperty])]
    Backdrop = 16,
}