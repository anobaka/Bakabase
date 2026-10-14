using System.Text.Json.Serialization;
using Bakabase.Modules.Player.Components;

namespace Bakabase.Modules.Player.Abstractions.Models.Domain;

/// <summary>Local opening choices. Server/node identity and library incarnation
/// keep equal resource numbers on unrelated devices from sharing a rule.</summary>
[Newtonsoft.Json.JsonObject(MissingMemberHandling = Newtonsoft.Json.MissingMemberHandling.Error)]
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DevicePlaybackOverride
{
    [Newtonsoft.Json.JsonProperty(Required = Newtonsoft.Json.Required.Always)]
    public required string Target { get; init; }
    public int? ResourceId { get; init; }
    [JsonRequired]
    [Newtonsoft.Json.JsonProperty(Required = Newtonsoft.Json.Required.Always)]
    public Dictionary<string, DevicePlaybackOverrideChoice> Choices { get; init; } = [];

    public static readonly string[] Kinds = ["image", "audio", "video", "text", "other"];

    public static void ValidateTarget(string target)
    {
        var parts = target?.Split(':');
        if (parts == null || !((parts.Length == 2 && parts[0] == "server") ||
                              (parts.Length == 3 && parts[0] == "node")) ||
            parts.Skip(1).Any(p => p.Length is < 1 or > 128 ||
                p.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_'))))
            throw new ArgumentException("Choose a valid target device and library identity.");
    }

    public void Validate()
    {
        ValidateTarget(Target);
        if (ResourceId is <= 0 || Choices == null || Choices.Count > Kinds.Length)
            throw new ArgumentException("Choose a valid resource scope.");
        foreach (var (kind, choice) in Choices)
        {
            if (!Kinds.Contains(kind) || choice == null || choice.Mode is not ("browser" or "native" or "ask"))
                throw new ArgumentException("Choose a valid opening method.");
            if (choice.PlayerId != null && (choice.Mode != "native" ||
                choice.PlayerId != "system-default" && !KnownPlayerDefinitions.All.Any(p => p.Id == choice.PlayerId)))
                throw new ArgumentException("Choose a recognized local player; executable paths cannot be supplied here.");
        }
    }
}

[Newtonsoft.Json.JsonObject(MissingMemberHandling = Newtonsoft.Json.MissingMemberHandling.Error)]
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DevicePlaybackOverrideChoice(
    [property: JsonRequired]
    [property: Newtonsoft.Json.JsonProperty(Required = Newtonsoft.Json.Required.Always)]
    string Mode, string? PlayerId = null);
public sealed record DevicePlaybackOpener(string Id, string Name, bool Installed, bool SupportsHttp,
    string[]? SupportedKinds = null);
public sealed record DevicePlaybackOverrideSnapshot(DevicePlaybackOverride[] Rules, DevicePlaybackOpener[] Openers);
