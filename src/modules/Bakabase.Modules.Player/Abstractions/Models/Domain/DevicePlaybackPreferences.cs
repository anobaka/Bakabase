using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace Bakabase.Modules.Player.Abstractions.Models.Domain;

/// <summary>Playback choices for this desktop installation, shared by its own
/// server and the relays through which it manages other servers.</summary>
[JsonObject(MissingMemberHandling = MissingMemberHandling.Error)]
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DevicePlaybackPreferences
{
    [System.Text.Json.Serialization.JsonRequired]
    [JsonProperty(Required = Required.Always)]
    public required int Version { get; init; }

    [System.Text.Json.Serialization.JsonRequired]
    [JsonProperty(Required = Required.Always)]
    public required DevicePlaybackChoice Image { get; init; }

    [System.Text.Json.Serialization.JsonRequired]
    [JsonProperty(Required = Required.Always)]
    public required DevicePlaybackChoice Text { get; init; }

    [System.Text.Json.Serialization.JsonRequired]
    [JsonProperty(Required = Required.Always)]
    public required DevicePlaybackChoice Audio { get; init; }

    [System.Text.Json.Serialization.JsonRequired]
    [JsonProperty(Required = Required.Always)]
    public required DevicePlaybackChoice Video { get; init; }

    [System.Text.Json.Serialization.JsonRequired]
    [JsonProperty(Required = Required.Always)]
    public required DevicePlaybackChoice Other { get; init; }

    public static DevicePlaybackPreferences CreateDefault() => new()
    {
        Version = 1,
        Image = new DevicePlaybackChoice { Mode = "automatic" },
        Text = new DevicePlaybackChoice { Mode = "automatic" },
        Audio = new DevicePlaybackChoice { Mode = "automatic" },
        Video = new DevicePlaybackChoice { Mode = "automatic" },
        Other = new DevicePlaybackChoice { Mode = "automatic" }
    };

    /// <summary>Only bounded identifiers are stored here: no executable, command,
    /// URL or filesystem path can be supplied through a playback preference.</summary>
    public void Validate()
    {
        if (Version != 1)
            throw new ArgumentException("Unsupported device playback preferences version.");

        ValidateChoice(Image, nameof(Image));
        ValidateChoice(Text, nameof(Text));
        ValidateChoice(Audio, nameof(Audio));
        ValidateChoice(Video, nameof(Video));
        ValidateChoice(Other, nameof(Other));
    }

    private static void ValidateChoice(DevicePlaybackChoice? choice, string name)
    {
        if (choice == null || choice.Mode is not ("automatic" or "browser" or "native" or "ask"))
            throw new ArgumentException($"Choose a valid playback mode for {name.ToLowerInvariant()}.");

        if (choice.NativeSchemeId is { } id &&
            (id.Length is < 1 or > 64 || id.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('_' or '-'))))
            throw new ArgumentException("A native player must be identified by its simple scheme ID.");
    }
}

[JsonObject(MissingMemberHandling = MissingMemberHandling.Error)]
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DevicePlaybackChoice
{
    /// <summary>automatic, browser, native or ask.</summary>
    [System.Text.Json.Serialization.JsonRequired]
    [JsonProperty(Required = Required.Always)]
    public required string Mode { get; init; }

    /// <summary>A simple player scheme identifier, never a URL or executable path.</summary>
    public string? NativeSchemeId { get; init; }
}
