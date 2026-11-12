using System;
using System.Linq;
using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Db;
using Bakabase.Modules.PostParser.Models.Domain;
using Bakabase.Modules.PostParser.Services;
using Bakabase.InsideWorld.Business.Components.PostParser.Extensions;

namespace Bakabase.InsideWorld.Business.Components.PostParser.Services;

/// <summary>Saved parsing output remains authoritative after optional workflow execution history is removed.</summary>
public static class PostParserSavedResults
{
    public const string MissingWorkflowRunError = "The parsing workflow run no longer exists. Parse this task again.";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static bool HasCompletedDownloadResult(PostParserTaskDbModel task)
    {
        if (task.IsDeleted || task.ParsingState != "complete" || string.IsNullOrWhiteSpace(task.Results)) return false;
        try { ReadDownloadInfo(task); return true; }
        catch (ArgumentException) { return false; }
    }

    public static PostDownloadInfo ReadDownloadInfo(PostParserTaskDbModel task)
    {
        var state = Models.Domain.PostParserContentReadinessState.Read(task.ContentReadinessJson);
        if (state.ParsedResultMayBeStale)
            throw new ArgumentException("The post evidence changed after this result was extracted. Complete parsing again before using this result.");
        var info = ReadDownloadInfo(task.Results ?? "null");
        if (info.ContentReadiness != null)
        {
            var content = task.ToDomainModel().ContentSnapshot;
            var proof = content == null ? null : state.RebindAssessment(content, info.ContentReadiness);
            if (content == null || !PostContentReadinessPolicy.IsCurrent(content, proof) ||
                info.ContentReadiness.Basis == "manual" && content.Readiness?.Basis != "manual" ||
                PostContentReadinessPolicy.HasBlockingLocks(content))
                throw new ArgumentException("The content-readiness confirmation changed. Review and complete parsing before using this result.");
            info = info with {ContentReadiness = proof};
        }
        return info;
    }

    public static PostDownloadInfo ReadDownloadInfo(string json)
    {
        const string invalid = "The stored parsing result has an invalid format. Parse the post again.";
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new ArgumentException(invalid);
            if (!root.TryGetProperty("DownloadInfo", out var data) && !root.TryGetProperty("1", out data))
                throw new ArgumentException("The post has no download-information result. Parse the post again.");
            if (data.ValueKind != JsonValueKind.Object) throw new ArgumentException(invalid);
            RejectResultError(data);
            if (data.TryGetProperty("data", out var nested) || data.TryGetProperty("Data", out nested)) data = nested;
            if (data.ValueKind != JsonValueKind.Object ||
                !data.TryGetProperty("resources", out var resources) && !data.TryGetProperty("Resources", out resources) ||
                resources.ValueKind != JsonValueKind.Array) throw new ArgumentException(invalid);
            RejectResultError(data);
            var info = data.Deserialize<PostDownloadInfo>(Json) ?? throw new ArgumentException(invalid);
            if (!info.IsComplete) throw new ArgumentException("Complete parsing before using this result.");
            if (info.Resources == null || info.Resources.Any(resource => resource == null)) throw new ArgumentException(invalid);
            return info;
        }
        catch (JsonException error) { throw new ArgumentException(invalid, error); }
    }

    private static void RejectResultError(JsonElement data)
    {
        foreach (var key in new[] { "error", "Error" })
            if (data.TryGetProperty(key, out var error) && error.ValueKind != JsonValueKind.Null &&
                (error.ValueKind != JsonValueKind.String || !string.IsNullOrEmpty(error.GetString())))
                throw new ArgumentException("The stored parsing result contains an error. Parse the post again.");
    }
}
