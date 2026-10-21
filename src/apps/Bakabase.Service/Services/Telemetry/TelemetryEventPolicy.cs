using System;
using System.Collections.Generic;
using System.Linq;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Modules.Enhancer.Models.Domain.Constants;
using Bakabase.Modules.Subscription.Abstractions.Models.Domain.Constants;

namespace Bakabase.Service.Services.Telemetry;

/// <summary>Fail closed: only a finite vocabulary and bounded numbers may leave this process.</summary>
public static class TelemetryEventPolicy
{
    private static readonly HashSet<string> Events =
    [
        "runtime_started", "runtime_ready", "runtime_heartbeat", "runtime_stopped", "deployment_snapshot",
        "deployment_first_seen", "download_completed",
        "resource_search_completed", "library_configured", "resource_profile_saved", "property_edit_completed",
        "bulk_edit_completed", "collection_updated", "saved_search_created", "path_mark_configured", "resource_merged",
        "workflow_configured", "subscription_configured", "task_attempt_finished", "workflow_run_completed", "enhancement_completed", "ai_request_completed",
        "subscription_check_completed"
    ];
    private static readonly HashSet<string> Numbers =
    [
        "duration_ms", "uptime_seconds", "startup_duration_ms", "resource_count", "media_library_count",
        "result_count", "item_count", "success_count", "failure_count", "skipped_count", "processed_count",
        "bytes_count", "queue_dropped_count", "delivery_failed_count", "heartbeat_interval_minutes",
        "resource_profile_count", "collection_count", "configured_enhancer_count", "enabled_enhancer_count",
        "queue_duration_ms", "retry_count", "input_count", "output_count", "incomplete_count", "zero_result_count",
        "input_tokens", "output_tokens", "total_tokens", "new_item_count"
    ];
    private static readonly HashSet<string> Booleans = ["success", "ai_enabled", "has_media_library", "has_filters", "has_keyword", "zero_results", "token_usage_available", "is_streaming"];
    private static readonly Dictionary<string, HashSet<string>> Words = new()
    {
        ["status"] = ["success", "failed", "cancelled", "partial", "unknown", "completed", "interrupted"],
        ["outcome"] = ["success", "failed", "cancelled", "partial", "partial_success", "unknown", "completed", "interrupted", "waiting", "retry", "retry_scheduled", "skipped"],
        ["provider_type"] = ["openai", "claude", "ollama", "dashscope", "gemini", "stable_diffusion_webui", "comfyui", "http_custom", "unknown"],
        ["ai_feature"] = ["default", "enhancer", "translation", "file_processor", "post_parser", "chat", "aigc", "unknown"],
        ["request_kind"] = ["llm", "aigc"],
        ["download_source"] = ["bilibili", "exhentai", "pixiv", "fanbox", "fantia", "cien", "patreon", "other"],
        ["snapshot_status"] = ["ok", "partial", "failed"],
        ["readiness_stage"] = ["http_listening"],
        ["first_seen_kind"] = ["new", "existing_unknown"],
        ["previous_exit"] = ["clean", "unclean_or_unknown", "unknown"],
        ["trigger"] = ["manual", "scheduled", "startup", "automatic", "unknown"],
        ["feature_id"] = ["resource", "search", "playback", "import", "enhancer", "download", "sync", "workflow", "collection", "media_library", "resource_profile", "bulk_modification", "comparison", "health_score", "subscription", "acquisition", "unknown", "library", "saved_search", "properties", "custom_properties", "collections", "path_marks", "resources", "resource_profiles", "subscriptions", "cache", "file_management"],
        ["task_type"] = Enum.GetNames<BTaskType>().Append("unknown").ToHashSet(StringComparer.Ordinal),
        ["task_resource_type"] = Enum.GetNames<BTaskResourceType>().Append("unknown").ToHashSet(StringComparer.Ordinal),
        ["enhancer_id"] = Enum.GetNames<EnhancerId>().Append("unknown").ToHashSet(StringComparer.Ordinal),
        ["subscription_source_kind"] = Enum.GetNames<SubscriptionSourceKind>().Append("unknown").ToHashSet(StringComparer.Ordinal),
        ["error_category"] = ["network", "timeout", "authentication", "permission", "not_found", "invalid_input", "dependency", "cancelled", "internal", "unknown"],
        ["playback_mode"] = ["browser", "native", "remote", "unknown"],
        ["source"] = ["local", "managed", "federated", "mixed", "unknown"]
    };

    public static bool Allows(string eventName) => Events.Contains(eventName);

    public static Dictionary<string, object?> Sanitize(IReadOnlyDictionary<string, object?>? properties)
    {
        var result = new Dictionary<string, object?>();
        if (properties == null) return result;
        foreach (var (key, value) in properties.Take(64))
        {
            if (Numbers.Contains(key) && (value == null || IsNumber(value))) result[key] = value;
            else if (Booleans.Contains(key) && value is bool) result[key] = value;
            else if (Words.TryGetValue(key, out var allowed) && value is string word && allowed.Contains(word)) result[key] = word;
            else if (key == "attempt_id" && value is string uuid && TelemetryHttpContext.Uuid(uuid) is { } id) result[key] = id;
            else if (key == "enabled_enhancers" && value is IEnumerable<string> ids)
                result[key] = ids.Take(32).Where(id => Words["enhancer_id"].Contains(id)).Distinct().Order().ToArray();
        }
        return result;
    }

    private static bool IsNumber(object value) => value switch
    {
        int n => n >= 0, long n => n >= 0, uint or ulong => true,
        double n => double.IsFinite(n) && n >= 0 && n <= 1e15,
        float n => float.IsFinite(n) && n >= 0 && n <= 1e15,
        _ => false
    };
}
