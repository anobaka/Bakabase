using System.Text.Json.Serialization;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Steam.Models;

public record SteamOwnedGamesResponse
{
    [JsonPropertyName("response")]
    public SteamOwnedGamesResponseBody? Response { get; set; }
}

public record SteamOwnedGamesResponseBody
{
    [JsonPropertyName("game_count")]
    public int GameCount { get; set; }

    [JsonPropertyName("games")]
    public List<SteamOwnedGame>? Games { get; set; }
}

public record SteamOwnedGame
{
    [JsonPropertyName("appid")]
    public int AppId { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("playtime_forever")]
    public int PlaytimeForever { get; set; }

    [JsonPropertyName("rtime_last_played")]
    public long RtimeLastPlayed { get; set; }

    [JsonPropertyName("img_icon_url")]
    public string? ImgIconUrl { get; set; }

    [JsonPropertyName("has_community_visible_stats")]
    public bool HasCommunityVisibleStats { get; set; }
}

public record SteamAppDetailsResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("data")]
    public SteamAppDetails? Data { get; set; }
}

public record SteamAppDetails
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("steam_appid")]
    public int SteamAppId { get; set; }

    [JsonPropertyName("short_description")]
    public string? ShortDescription { get; set; }

    [JsonPropertyName("detailed_description")]
    public string? DetailedDescription { get; set; }

    [JsonPropertyName("header_image")]
    public string? HeaderImage { get; set; }

    [JsonPropertyName("capsule_image")]
    public string? CapsuleImage { get; set; }

    [JsonPropertyName("developers")]
    public List<string>? Developers { get; set; }

    [JsonPropertyName("publishers")]
    public List<string>? Publishers { get; set; }

    [JsonPropertyName("metacritic")]
    public SteamMetacritic? Metacritic { get; set; }

    [JsonPropertyName("genres")]
    public List<SteamGenre>? Genres { get; set; }

    [JsonPropertyName("categories")]
    public List<SteamCategory>? Categories { get; set; }

    [JsonPropertyName("release_date")]
    public SteamReleaseDate? ReleaseDate { get; set; }
}

public record SteamMetacritic
{
    [JsonPropertyName("score")]
    public int Score { get; set; }
}

public record SteamGenre
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

public record SteamCategory
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

public record SteamReleaseDate
{
    [JsonPropertyName("coming_soon")]
    public bool ComingSoon { get; set; }

    [JsonPropertyName("date")]
    public string? Date { get; set; }
}
