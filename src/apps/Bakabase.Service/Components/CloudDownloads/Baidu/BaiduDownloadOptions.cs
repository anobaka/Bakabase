using System;
using System.ComponentModel.DataAnnotations;

namespace Bakabase.Service.Components.CloudDownloads.Baidu;

public sealed record BaiduDownloadOptions
{
    public const int MaximumConcurrentTasks = 5;
    public const int MaximumConnectionsPerFile = 20;
    [Range(1, MaximumConcurrentTasks)] public int MaxConcurrentTasks { get; init; } = 1;
    [Range(1, MaximumConnectionsPerFile)] public int ConnectionsPerFile { get; init; } = 1;
    /// <summary>Only a fallback for unknown or stale observations; a fresh non-SVIP result takes precedence.</summary>
    public bool UserConfirmedSvip { get; init; }
}

public sealed record BaiduMembershipViewModel
{
    public string Kind { get; init; } = "unknown";
    public DateTimeOffset? CheckedAt { get; init; }
    public DateTimeOffset? LastAttemptAt { get; init; }
    public string ProbeMethod { get; init; } = "baidu-xpan-uinfo";
    public string? FailureCode { get; init; }

    internal bool IsFresh(DateTimeOffset now) => CheckedAt is { } checkedAt && checkedAt <= now &&
        now - checkedAt < TimeSpan.FromHours(6) && Kind is "free" or "vip" or "svip";
}

public sealed record BaiduPcsDownloadLimits(string? AccountId, int MaxConcurrentTasks, int ConnectionsPerFile);

internal static class BaiduDownloadPolicy
{
    public static BaiduPcsDownloadLimits Evaluate(BaiduPcsAccountViewModel account, DateTimeOffset now)
    {
        var options = account.DownloadOptions;
        var membership = account.Membership;
        var fresh = membership.IsFresh(now);
        var allowed = account.IsLoggedIn && !(fresh && membership.Kind is "free" or "vip") &&
                      (fresh && membership.Kind == "svip" && membership.FailureCode == null || options.UserConfirmedSvip);
        return new(account.Id, allowed ? Math.Clamp(options.MaxConcurrentTasks, 1, BaiduDownloadOptions.MaximumConcurrentTasks) : 1,
            allowed ? Math.Clamp(options.ConnectionsPerFile, 1, BaiduDownloadOptions.MaximumConnectionsPerFile) : 1);
    }
}
