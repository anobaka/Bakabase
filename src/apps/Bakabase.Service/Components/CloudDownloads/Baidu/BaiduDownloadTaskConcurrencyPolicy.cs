using System;
using System.Collections.Concurrent;
using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Components;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.Downloader.Cloud;

namespace Bakabase.Service.Components.CloudDownloads.Baidu;

public sealed class BaiduDownloadTaskConcurrencyPolicy(BaiduAccountSnapshot accounts) : IDownloadTaskConcurrencyPolicy
{
    private readonly ConcurrentDictionary<int, string> _taskAccounts = new();
    public ThirdPartyId ThirdPartyId => ThirdPartyId.BaiduNetdisk;

    public DownloadTaskConcurrencyLimits GetLimits(int taskId, string? options)
    {
        string? accountId;
        if (!string.IsNullOrWhiteSpace(options))
        {
            try
            {
                var request = JsonSerializer.Deserialize<CloudDownloadRequest>(options, JsonSerializerOptions.Web);
                if (request?.Source?.ProviderId != "baidu" || request.RemoteEntry is { ProviderId: not "baidu" } ||
                    request.RemoteEntry is { } entry && request.AccountId != null && request.AccountId != entry.AccountId)
                    return new();
                accountId = accounts.GetCachedDownloadLimits(request.RemoteEntry?.AccountId ?? request.AccountId).AccountId;
                if (!Guid.TryParseExact(accountId, "N", out _)) return new();
                _taskAccounts[taskId] = accountId!;
            }
            catch (JsonException) { return new(); }
        }
        else if (!_taskAccounts.TryGetValue(taskId, out accountId)) return new();
        var limits = accounts.GetCachedDownloadLimits(accountId);
        // Apply the same bounded source capacity across accounts; each account also keeps its own effective limit.
        return new(BaiduDownloadOptions.MaximumConcurrentTasks, "baidu-account:" + accountId, limits.MaxConcurrentTasks);
    }
}
