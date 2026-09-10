using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Bakabase.Service.Components.Acquisition;

/// <summary>What aria2 says about one download.</summary>
/// <param name="Status">aria2's own word: active, waiting, paused, error, complete, removed.</param>
/// <param name="CompletedLength">Bytes fetched so far.</param>
/// <param name="TotalLength">Bytes in total, or 0 while the metadata is still being resolved.</param>
/// <param name="FollowedBy">
/// The download this one turned into. A magnet is fetched in two parts: the first download is the
/// torrent's metadata and completes almost at once, and the files arrive under the id it names
/// here. Following it is not optional — the first id reads as "complete" with nothing on disk.
/// </param>
/// <param name="Files">Absolute paths aria2 says it wrote.</param>
/// <param name="ErrorMessage">Why it stopped, when it stopped badly.</param>
public record Aria2Status(
    string Status,
    long CompletedLength,
    long TotalLength,
    string? FollowedBy,
    IReadOnlyList<string> Files,
    string? ErrorMessage)
{
    public bool IsComplete => string.Equals(Status, "complete", StringComparison.OrdinalIgnoreCase);

    public bool IsFailed => Status is "error" or "removed";

    /// <summary>How far along, as a percentage, or 0 while the total is still unknown.</summary>
    public int Percentage => TotalLength <= 0
        ? 0
        : (int) Math.Clamp(CompletedLength * 100 / TotalLength, 0, 100);
}

/// <summary>
/// The shape of aria2's JSON-RPC, kept apart from the sending of it.
/// <para>
/// Everything here is a pure function of strings, which is the point: a magnet handed to the wrong
/// place downloads gigabytes into somewhere nobody looks, and that is not a failure any integration
/// test against a live daemon would catch either. The request bodies and the replies are what can
/// be pinned down, so they are what this exposes.
/// </para>
/// </summary>
public static class Aria2Rpc
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// The body that asks aria2 to fetch <paramref name="uri"/> into <paramref name="directory"/>.
    /// </summary>
    /// <param name="secret">
    /// aria2's RPC token. It goes in as the first positional parameter prefixed with
    /// <c>token:</c> — a daemon started with <c>--rpc-secret</c> rejects anything else, and one
    /// started without it ignores the parameter, so passing it when set is always right.
    /// </param>
    public static string BuildAddUri(string uri, string directory, string? secret, string requestId)
    {
        var parameters = new JsonArray();

        if (!string.IsNullOrWhiteSpace(secret)) parameters.Add($"token:{secret}");

        parameters.Add(new JsonArray(JsonValue.Create(uri)));
        parameters.Add(new JsonObject {["dir"] = directory});

        return Request("aria2.addUri", parameters, requestId);
    }

    /// <summary>The body that asks how one download is going.</summary>
    public static string BuildTellStatus(string gid, string? secret, string requestId)
    {
        var parameters = new JsonArray();

        if (!string.IsNullOrWhiteSpace(secret)) parameters.Add($"token:{secret}");

        parameters.Add(gid);

        return Request("aria2.tellStatus", parameters, requestId);
    }

    /// <summary>The id aria2 gave the download it just accepted.</summary>
    /// <exception cref="InvalidOperationException">aria2 answered with an error, or with nothing usable.</exception>
    public static string ReadGid(string responseJson)
    {
        var root = Parse(responseJson);

        return root["result"]?.GetValue<string>()
               ?? throw new InvalidOperationException("aria2 accepted nothing: the reply carried no download id.");
    }

    /// <summary>What aria2 said about a download.</summary>
    /// <exception cref="InvalidOperationException">aria2 answered with an error.</exception>
    public static Aria2Status ReadStatus(string responseJson)
    {
        var root = Parse(responseJson);
        var result = root["result"]?.AsObject()
                     ?? throw new InvalidOperationException("aria2 said nothing about that download.");

        // followedBy is a list because one download can turn into several; for a magnet it is the
        // single torrent that the metadata described.
        var followedBy = result["followedBy"]?.AsArray()
            .Select(n => n?.GetValue<string>())
            .FirstOrDefault(x => !string.IsNullOrEmpty(x));

        var files = result["files"]?.AsArray()
            .Select(f => f?["path"]?.GetValue<string>())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!)
            .ToList() ?? [];

        return new Aria2Status(
            result["status"]?.GetValue<string>() ?? "unknown",
            ReadLong(result, "completedLength"),
            ReadLong(result, "totalLength"),
            followedBy,
            files,
            result["errorMessage"]?.GetValue<string>());
    }

    private static string Request(string method, JsonArray parameters, string requestId) =>
        new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = requestId,
            ["method"] = method,
            ["params"] = parameters
        }.ToJsonString(Json);

    private static JsonObject Parse(string responseJson)
    {
        var root = JsonNode.Parse(responseJson)?.AsObject()
                   ?? throw new InvalidOperationException("aria2 answered with something that is not an object.");

        if (root["error"] is JsonObject error)
        {
            var message = error["message"]?.GetValue<string>() ?? "no reason given";

            throw new InvalidOperationException($"aria2 refused: {message}");
        }

        return root;
    }

    /// <summary>
    /// aria2 writes byte counts as decimal strings — they outgrow a 32-bit number and it does not
    /// trust JSON numbers with them.
    /// </summary>
    private static long ReadLong(JsonObject result, string name) =>
        long.TryParse(result[name]?.GetValue<string>(), out var value) ? value : 0;
}
