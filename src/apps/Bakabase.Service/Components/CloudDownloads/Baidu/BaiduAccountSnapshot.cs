using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Bakabase.Service.Components.CloudDownloads.Baidu;

/// <summary>Status projection reads only this immutable snapshot, without opening credentials or requiring a native dependency.</summary>
public sealed class BaiduAccountSnapshot
{
    private BaiduPcsAccountViewModel[] _rows = [];
    internal void Update(IEnumerable<BaiduPcsAccountViewModel> rows) => Volatile.Write(ref _rows, rows.ToArray());
    public BaiduPcsDownloadLimits GetCachedDownloadLimits(string? accountId)
    {
        var rows = Volatile.Read(ref _rows);
        var row = string.IsNullOrWhiteSpace(accountId)
            ? rows.FirstOrDefault(a => a.IsDefault && a.IsLoggedIn)
            : rows.FirstOrDefault(a => a.Id == accountId);
        return row == null ? new(accountId, 1, 1) : BaiduDownloadPolicy.Evaluate(row, DateTimeOffset.UtcNow);
    }
}
