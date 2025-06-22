using System.ComponentModel.DataAnnotations;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.Enhancer.Models.Domain.Constants;
using Bakabase.Modules.Presets.Abstractions.Components;

namespace Bakabase.Modules.Presets.Abstractions.Models.Constants;

using P = PresetProperty;

public enum PresetResourceType
{
    [Display(Name = "PresetResourceType_Video", ResourceType = typeof(Resources.PresetsResource))]
    [PresetResourceType([P.Duration, P.Name, P.Tag, P.ReleaseDate, P.Type], [], MediaType.Video)]
    Video = 1000,

    [Display(Name = "PresetResourceType_Movie", ResourceType = typeof(Resources.PresetsResource))]
    [PresetResourceType([
        P.Duration, P.Name, P.Tag, P.ReleaseDate, P.Type, P.Director, P.Actor, P.Language, P.SubtitleLanguage
    ], [], MediaType.Video)]
    Movie,

    [Display(Name = "PresetResourceType_Anime", ResourceType = typeof(Resources.PresetsResource))]
    [PresetResourceType([
        P.Duration, P.Name, P.Tag, P.ReleaseDate, P.Type, P.VoiceActor, P.Series, P.EpisodeCount, P.Language,
        P.SubtitleLanguage,
        P.Tag, P.Character
    ], [EnhancerId.Bangumi, EnhancerId.Bakabase], MediaType.Video)]
    Anime,

    [Display(Name = "PresetResourceType_Ova", ResourceType = typeof(Resources.PresetsResource))]
    [PresetResourceType([
        P.Duration, P.Name, P.Tag, P.ReleaseDate, P.Type, P.VoiceActor, P.Series, P.EpisodeCount, P.Language,
        P.SubtitleLanguage,
        P.Tag
    ], [EnhancerId.Bangumi, EnhancerId.Bakabase], MediaType.Video)]
    Ova,

    [Display(Name = "PresetResourceType_TvSeries",
        ResourceType = typeof(Resources.PresetsResource))]
    [PresetResourceType([
        P.Duration, P.Name, P.Tag, P.ReleaseDate, P.Type, P.Name, P.Series, P.EpisodeCount, P.Director, P.Actor,
        P.Language,
        P.SubtitleLanguage,
        P.Tag
    ], [], MediaType.Video)]
    TvSeries,

    [Display(Name = "PresetResourceType_TvShow", ResourceType = typeof(Resources.PresetsResource))]
    [PresetResourceType([
        P.Duration, P.Name, P.Tag, P.ReleaseDate, P.Type, P.Name, P.EpisodeCount, P.Director, P.Actor, P.Language,
        P.SubtitleLanguage, P.Tag
    ], [], MediaType.Video)]
    TvShow,

    [Display(Name = "PresetResourceType_Documentary",
        ResourceType = typeof(Resources.PresetsResource))]
    [PresetResourceType([
        P.Duration, P.Name, P.Tag, P.ReleaseDate, P.Type, P.Name, P.ReleaseDate, P.Director, P.Duration, P.Language,
        P.SubtitleLanguage, P.Tag
    ], [], MediaType.Video)]
    Documentary,

    [Display(Name = "PresetResourceType_Clip", ResourceType = typeof(Resources.PresetsResource))]
    [PresetResourceType([P.Duration, P.Name, P.Tag, P.ReleaseDate, P.Type, P.Name, P.Duration, P.Tag, P.Author], [],
        MediaType.Video)]
    Clip,

    [Display(Name = "PresetResourceType_LiveStream",
        ResourceType = typeof(Resources.PresetsResource))]
    [PresetResourceType([P.Duration, P.Name, P.Tag, P.ReleaseDate, P.Type, P.Author, P.Name, P.Duration, P.Tag], [],
        MediaType.Video)]
    LiveStream,

    [Display(Name = "PresetResourceType_VideoSubscription",
        ResourceType = typeof(Resources.PresetsResource))]
    [PresetResourceType([
        P.Duration, P.Name, P.Tag, P.ReleaseDate, P.Type, P.Name, P.Tag, P.Author, P.Duration, P.SubscriptionPlatform
    ], [], MediaType.Video)]
    VideoSubscription,

    [Display(Name = "PresetResourceType_Av", ResourceType = typeof(Resources.PresetsResource))]
    [PresetResourceType([
        P.Duration, P.Name, P.Tag, P.ReleaseDate, P.Type, P.Name, P.Actor, P.Publisher, P.IsCensored, P.Tag
    ], [], MediaType.Video)]
    Av,

    [Display(Name = "PresetResourceType_AvClip", ResourceType = typeof(Resources.PresetsResource))]
    [PresetResourceType([P.Duration, P.Name, P.Tag, P.ReleaseDate, P.Type, P.Name, P.Actor, P.IsCensored, P.Tag], [],
        MediaType.Video)]
    AvClip,

    [Display(Name = "PresetResourceType_AvSubscription",
        ResourceType = typeof(Resources.PresetsResource))]
    [PresetResourceType([
        P.Duration, P.Name, P.Tag, P.ReleaseDate, P.Type, P.Name, P.Actor, P.IsCensored, P.Tag, P.SubscriptionPlatform
    ], [], MediaType.Video)]
    AvSubscription,

    [Display(Name = "PresetResourceType_Mmd", ResourceType = typeof(Resources.PresetsResource))]
    [PresetResourceType([
        P.Duration, P.Name, P.Tag, P.ReleaseDate, P.Type, P.Name, P.Actor, P.Tag, P.SubscriptionPlatform
    ], [], MediaType.Video)]
    Mmd,

    [Display(Name = "PresetResourceType_AdultMmd",
        ResourceType = typeof(Resources.PresetsResource))]
    [PresetResourceType([
        P.Duration, P.Name, P.Tag, P.ReleaseDate, P.Type, P.Name, P.Actor, P.Tag, P.SubscriptionPlatform
    ], [], MediaType.Video)]
    AdultMmd,

    [Display(Name = "PresetResourceType_Vr", ResourceType = typeof(Resources.PresetsResource))]
    [PresetResourceType([P.Duration, P.Name, P.Tag, P.ReleaseDate, P.Type, P.Name, P.Tag], [], MediaType.Video)]
    Vr,

    [Display(Name = "PresetResourceType_VrAv", ResourceType = typeof(Resources.PresetsResource))]
    [PresetResourceType([P.Duration, P.Name, P.Tag, P.ReleaseDate, P.Type, P.Name, P.Actor, P.IsCensored, P.Tag], [],
        MediaType.Video)]
    VrAv,

    [Display(Name = "PresetResourceType_VrAnime", ResourceType = typeof(Resources.PresetsResource))]
    [PresetResourceType([P.Duration, P.Name, P.Tag, P.ReleaseDate, P.Type, P.Name, P.Tag, P.Character], [],
        MediaType.Video)]
    VrAnime,

    [Display(Name = "PresetResourceType_AiVideo", ResourceType = typeof(Resources.PresetsResource))]
    [PresetResourceType([P.Duration, P.Name, P.Tag, P.ReleaseDate, P.Type, P.Name, P.IsAi, P.Tag], [], MediaType.Video)]
    AiVideo,

    [Display(Name = "PresetResourceType_AsmrVideo",
        ResourceType = typeof(Resources.PresetsResource))]
    [PresetResourceType([P.Duration, P.Name, P.Tag, P.ReleaseDate, P.Type, P.Name, P.Tag], [EnhancerId.Bakabase],
        MediaType.Video)]
    AsmrVideo,

    [Display(Name = "PresetResourceType_Image", ResourceType = typeof(Resources.PresetsResource))]
    [PresetResourceType([P.Name, P.Tag, P.Type, P.Is3D, P.IsAi], [], MediaType.Image)]
    Image = 2000,

    [Display(Name = "PresetResourceType_Manga", ResourceType = typeof(Resources.PresetsResource))]
    [PresetResourceType([
        P.Name, P.Tag, P.Type, P.Is3D, P.IsAi,
        P.Name, P.Author, P.Series, P.Tag, P.Language, P.ImageCount, P.Character
    ], [EnhancerId.Bakabase], MediaType.Image)]
    Manga,

    [Display(Name = "PresetResourceType_Comic", ResourceType = typeof(Resources.PresetsResource))]
    [PresetResourceType([
        P.Name, P.Tag, P.Type, P.Is3D, P.IsAi,
        P.Name, P.Author, P.Series, P.Publisher, P.Tag, P.Language, P.ImageCount, P.Character
    ], [EnhancerId.Bakabase], MediaType.Image)]
    Comic,

    [Display(Name = "PresetResourceType_Doushijin",
        ResourceType = typeof(Resources.PresetsResource))]
    [PresetResourceType([
        P.Name, P.Tag, P.Type, P.Is3D, P.IsAi,
        P.Name, P.Author, P.Series, P.Publisher, P.Tag, P.Language, P.ImageCount, P.Character
    ], [EnhancerId.Bakabase, EnhancerId.ExHentai], MediaType.Image)]
    Doushijin,

    [Display(Name = "PresetResourceType_Artbook", ResourceType = typeof(Resources.PresetsResource))]
    [PresetResourceType([P.Name, P.Tag, P.Type, P.Is3D, P.IsAi, P.Name, P.Publisher, P.Author, P.Tag, P.ImageCount],
        [], MediaType.Image)]
    Artbook,

    [Display(Name = "PresetResourceType_Illustration",
        ResourceType = typeof(Resources.PresetsResource))]
    [PresetResourceType([P.Name, P.Tag, P.Type, P.Is3D, P.IsAi, P.Name, P.Author, P.Tag, P.ImageCount], [],
        MediaType.Image)]
    Illustration,

    [Display(Name = "PresetResourceType_ArtistCg",
        ResourceType = typeof(Resources.PresetsResource))]
    [PresetResourceType([
        P.Name, P.Tag, P.Type, P.Is3D, P.IsAi, P.Name, P.Publisher, P.Author, P.Tag, P.ImageCount, P.Character
    ], [EnhancerId.Bakabase, EnhancerId.ExHentai], MediaType.Image)]
    ArtistCg,

    [Display(Name = "PresetResourceType_GameCg", ResourceType = typeof(Resources.PresetsResource))]
    [PresetResourceType([
        P.Name, P.Tag, P.Type, P.Is3D, P.IsAi, P.Name, P.Publisher, P.Author, P.Tag, P.ImageCount, P.Character
    ], [EnhancerId.Bakabase, EnhancerId.ExHentai], MediaType.Image)]
    GameCg,

    [Display(Name = "PresetResourceType_ImageSubscription",
        ResourceType = typeof(Resources.PresetsResource))]
    [PresetResourceType([P.Name, P.Tag, P.Type, P.Is3D, P.IsAi, P.Name, P.Tag, P.Author, P.SubscriptionPlatform], [],
        MediaType.Image)]
    ImageSubscription,

    [Display(Name = "PresetResourceType_IllustrationSubscription",
        ResourceType = typeof(Resources.PresetsResource))]
    [PresetResourceType([P.Name, P.Tag, P.Type, P.Is3D, P.IsAi, P.Name, P.Tag, P.Author, P.SubscriptionPlatform], [],
        MediaType.Image)]
    IllustrationSubscription,

    [Display(Name = "PresetResourceType_MangaSubscription",
        ResourceType = typeof(Resources.PresetsResource))]
    [PresetResourceType([P.Name, P.Tag, P.Type, P.Is3D, P.IsAi, P.Name, P.Tag, P.Author, P.SubscriptionPlatform], [],
        MediaType.Image)]
    MangaSubscription,

    [Display(Name = "PresetResourceType_Manga3D", ResourceType = typeof(Resources.PresetsResource))]
    [PresetResourceType([P.Name, P.Tag, P.Type, P.Is3D, P.IsAi, P.Name, P.ImageCount, P.Tag, P.Author, P.Character],
        [EnhancerId.Bakabase, EnhancerId.ExHentai], MediaType.Image)]
    Manga3D,

    [Display(Name = "PresetResourceType_Photograph",
        ResourceType = typeof(Resources.PresetsResource))]
    [PresetResourceType([P.Name, P.Tag, P.Type, P.Is3D, P.IsAi, P.Name, P.ImageCount, P.Tag, P.Author], [],
        MediaType.Image)]
    Photograph,

    [Display(Name = "PresetResourceType_Cosplay", ResourceType = typeof(Resources.PresetsResource))]
    [PresetResourceType([
        P.Name, P.Tag, P.Type, P.Is3D, P.IsAi, P.Name, P.ImageCount, P.Tag, P.Author, P.Actor, P.Publisher,
        P.Character
    ], [], MediaType.Image)]
    Cosplay,

    [Display(Name = "PresetResourceType_AiImage", ResourceType = typeof(Resources.PresetsResource))]
    [PresetResourceType([P.Name, P.Tag, P.Type, P.Is3D, P.IsAi, P.Name, P.ImageCount, P.Tag, P.Author], [],
        MediaType.Image)]
    AiImage,

    [Display(Name = "PresetResourceType_Audio", ResourceType = typeof(Resources.PresetsResource))]
    [PresetResourceType([P.Name, P.Tag, P.Type, P.Bitrate, P.Duration], [], MediaType.Audio)]
    Audio = 3000,

    [Display(Name = "PresetResourceType_AsmrAudio",
        ResourceType = typeof(Resources.PresetsResource))]
    [PresetResourceType([P.Name, P.Tag, P.Type, P.Bitrate, P.Duration, P.Name, P.Author, P.Duration, P.Tag], [],
        MediaType.Audio)]
    AsmrAudio,

    [Display(Name = "PresetResourceType_Music", ResourceType = typeof(Resources.PresetsResource))]
    [PresetResourceType([
        P.Name, P.Tag, P.Type, P.Bitrate, P.Duration, P.Name, P.Singer, P.Duration, P.AudioFormat, P.Singer,
        P.Publisher,
        P.Bitrate, P.Tag
    ], [], MediaType.Audio)]
    Music,

    [Display(Name = "PresetResourceType_Podcast", ResourceType = typeof(Resources.PresetsResource))]
    [PresetResourceType([P.Name, P.Tag, P.Type, P.Bitrate, P.Duration, P.Name, P.Author, P.Duration, P.Tag, P.Author],
        [], MediaType.Audio)]
    Podcast,

    [Display(Name = "PresetResourceType_Application",
        ResourceType = typeof(Resources.PresetsResource))]
    [PresetResourceType([P.Type, P.Tag, P.Name, P.Publisher, P.Series, P.Tag, P.Platform], [], MediaType.Application)]
    Application = 4000,

    [Display(Name = "PresetResourceType_Game", ResourceType = typeof(Resources.PresetsResource))]
    [PresetResourceType([P.Name, P.Publisher, P.Series, P.Tag, P.Publisher, P.Developer, P.Platform, P.Type], [],
        MediaType.Application)]
    Game,

    [Display(Name = "PresetResourceType_Galgame", ResourceType = typeof(Resources.PresetsResource))]
    [PresetResourceType([P.Name, P.Publisher, P.Series, P.Publisher, P.Developer, P.Tag, P.Platform],
        [EnhancerId.Bakabase], MediaType.Application)]
    Galgame,

    [Display(Name = "PresetResourceType_VrGame", ResourceType = typeof(Resources.PresetsResource))]
    [PresetResourceType([P.Name, P.Publisher, P.Series, P.Publisher, P.Developer, P.Tag, P.Platform], [],
        MediaType.Application)]
    VrGame,

    [Display(Name = "PresetResourceType_Text", ResourceType = typeof(Resources.PresetsResource))]
    [PresetResourceType([P.Tag, P.Name], [], MediaType.Text)]
    Text = 5000,

    [Display(Name = "PresetResourceType_Novel", ResourceType = typeof(Resources.PresetsResource))]
    [PresetResourceType([P.Name, P.Author, P.Publisher, P.Series, P.Tag, P.Language], [], MediaType.Text)]
    Novel,

    [Display(Name = "PresetResourceType_MotionManga",
        ResourceType = typeof(Resources.PresetsResource))]
    [PresetResourceType([P.Name, P.Author, P.Publisher, P.Series, P.Tag, P.Language, P.SubtitleLanguage], [],
        MediaType.Video)]
    MotionManga = 10000,

    [Display(Name = "PresetResourceType_Mod", ResourceType = typeof(Resources.PresetsResource))]
    [PresetResourceType([P.Name, P.Tag, P.Platform], [], MediaType.Application)]
    Mod,

    [Display(Name = "PresetResourceType_Tool", ResourceType = typeof(Resources.PresetsResource))]
    [PresetResourceType([P.Name, P.Tag, P.Platform], [], MediaType.Application)]
    Tool
}