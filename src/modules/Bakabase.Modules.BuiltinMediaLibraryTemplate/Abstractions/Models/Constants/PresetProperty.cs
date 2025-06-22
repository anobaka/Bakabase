using System.ComponentModel.DataAnnotations;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Property.Components;

namespace Bakabase.Modules.Presets.Abstractions.Models.Constants;
using PP = PresetPropertyAttribute;
using PT = PropertyType;

public enum PresetProperty
{
    [Display(Name = "PresetProperty_Name", ResourceType = typeof(Resources.PresetsResource))]
    [PP(PT.SingleLineText)]
    Name = 1,

    [Display(Name = "PresetProperty_ReleaseDate", ResourceType = typeof(Resources.PresetsResource))]
    [PP(PT.Date)]
    ReleaseDate,

    [Display(Name = "PresetProperty_Author", ResourceType = typeof(Resources.PresetsResource))]
    [PP(PT.MultipleChoice)]
    Author,

    [Display(Name = "PresetProperty_Publisher", ResourceType = typeof(Resources.PresetsResource))]
    [PP(PT.MultipleChoice)]
    Publisher,

    [Display(Name = "PresetProperty_Series", ResourceType = typeof(Resources.PresetsResource))]
    [PP(PT.MultipleChoice)]
    Series,

    [Display(Name = "PresetProperty_Tag", ResourceType = typeof(Resources.PresetsResource))]
    [PP(PT.Tags)]
    Tag,

    [Display(Name = "PresetProperty_Language", ResourceType = typeof(Resources.PresetsResource))]
    [PP(PT.SingleChoice)]
    Language,

    [Display(Name = "PresetProperty_Original", ResourceType = typeof(Resources.PresetsResource))]
    [PP(PT.MultipleChoice)]
    Original,

    [Display(Name = "PresetProperty_Actor", ResourceType = typeof(Resources.PresetsResource))]
    [PP(PT.MultipleChoice)]
    Actor,

    [Display(Name = "PresetProperty_VoiceActor", ResourceType = typeof(Resources.PresetsResource))]
    [PP(PT.MultipleChoice)]
    VoiceActor,

    [Display(Name = "PresetProperty_Duration", ResourceType = typeof(Resources.PresetsResource))]
    [PP(PT.Time)]
    Duration,

    [Display(Name = "PresetProperty_Director", ResourceType = typeof(Resources.PresetsResource))]
    [PP(PT.MultipleChoice)]
    Director,

    [Display(Name = "PresetProperty_Singer", ResourceType = typeof(Resources.PresetsResource))]
    [PP(PT.MultipleChoice)]
    Singer,

    [Display(Name = "PresetProperty_EpisodeCount",
        ResourceType = typeof(Resources.PresetsResource))]
    [PP(PT.Number)]
    EpisodeCount,

    [Display(Name = "PresetProperty_Resolution", ResourceType = typeof(Resources.PresetsResource))]
    [PP(PT.SingleChoice)]
    Resolution,

    [Display(Name = "PresetProperty_AspectRatio", ResourceType = typeof(Resources.PresetsResource))]
    [PP(PT.SingleChoice)]
    AspectRatio,

    [Display(Name = "PresetProperty_SubtitleLanguage",
        ResourceType = typeof(Resources.PresetsResource))]
    [PP(PT.MultipleChoice)]
    SubtitleLanguage,

    [Display(Name = "PresetProperty_VideoCodec", ResourceType = typeof(Resources.PresetsResource))]
    [PP(PT.SingleChoice)]
    VideoCodec,

    [Display(Name = "PresetProperty_IsCensored", ResourceType = typeof(Resources.PresetsResource))]
    [PP(PT.Boolean)]
    IsCensored,

    [Display(Name = "PresetProperty_Is3D", ResourceType = typeof(Resources.PresetsResource))]
    [PP(PT.Boolean)]
    Is3D,

    [Display(Name = "PresetProperty_ImageCount", ResourceType = typeof(Resources.PresetsResource))]
    [PP(PT.Number)]
    ImageCount,

    [Display(Name = "PresetProperty_IsAi", ResourceType = typeof(Resources.PresetsResource))]
    [PP(PT.Boolean)]
    IsAi,

    [Display(Name = "PresetProperty_Developer", ResourceType = typeof(Resources.PresetsResource))]
    [PP(PT.SingleChoice)]
    Developer,

    [Display(Name = "PresetProperty_Character", ResourceType = typeof(Resources.PresetsResource))]
    [PP(PT.MultipleChoice)]
    Character,

    [Display(Name = "PresetProperty_AudioFormat", ResourceType = typeof(Resources.PresetsResource))]
    [PP(PT.SingleChoice)]
    AudioFormat,

    [Display(Name = "PresetProperty_Bitrate", ResourceType = typeof(Resources.PresetsResource))]
    [PP(PT.Number)]
    Bitrate,

    [Display(Name = "PresetProperty_Platform", ResourceType = typeof(Resources.PresetsResource))]
    [PP(PT.SingleChoice)]
    Platform,

    [Display(Name = "PresetProperty_SubscriptionPlatform",
        ResourceType = typeof(Resources.PresetsResource))]
    [PP(PT.SingleChoice)]
    SubscriptionPlatform,

    [Display(Name = "PresetProperty_Type", ResourceType = typeof(Resources.PresetsResource))]
    [PP(PT.MultipleChoice)]
    Type,
}