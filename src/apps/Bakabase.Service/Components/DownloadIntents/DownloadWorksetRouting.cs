using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Service.Components.CloudDownloads;

namespace Bakabase.Service.Components.DownloadIntents;

public static class DownloadWorksetRouting
{
    public static bool HasDownloadCheckpoint(CloudDownloadTarget target) =>
        target.DownloadTaskId != null || target.TransportTaskId != null || target.DownloadArtifactId != null ||
        target.ProcessedArtifactId != null || target.PublishedArtifactId != null;

    public static void ValidateOutputDirectory(DownloadWorkset item, string? output)
    {
        if (string.IsNullOrWhiteSpace(output) || string.IsNullOrWhiteSpace(item.OutputDirectory) ||
            !item.Targets.Any(HasDownloadCheckpoint)) return;
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(item.OutputDirectory)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(output)), comparison))
            throw new InvalidOperationException("This target already has a download or artifact in the original directory. Start a new run to change the download directory.");
    }

    public static DownloadWorkset ApplySelections(DownloadWorkset item, CloudSelectionSignal? signal)
    {
        if (signal == null) return item;
        if (signal.SourceMethods == null || signal.SourceAccounts == null ||
            signal.SourceMethods.Keys.Concat(signal.SourceAccounts.Keys).Any(id => !item.Targets.Any(t => t.Sources.Any(s => s.Id == id))))
            throw new ArgumentException("Choose methods and accounts from the saved sources.");
        return item with {Targets = item.Targets.Select(target =>
        {
            var sources = target.Sources.Select(source =>
            {
                var method = signal.SourceMethods.GetValueOrDefault(source.Id, source.MethodId);
                var account = signal.SourceAccounts.GetValueOrDefault(source.Id, source.AccountId);
                if (method == source.MethodId && account == source.AccountId) return source;
                if (target.RemoteEntry != null || target.DownloadTaskId != null || target.TransportTaskId != null ||
                    target.DownloadArtifactId != null || target.ProcessedArtifactId != null || target.PublishedArtifactId != null)
                    throw new InvalidOperationException("This target already has an operation or artifact. Start a new run to change its method or account.");
                return source with {MethodId = method, AccountId = string.IsNullOrWhiteSpace(account) ? null : account, AccountVersion = null};
            }).ToList();
            var selected = sources.FirstOrDefault(s => s.Id == target.SelectedSourceId);
            return target with {Sources = sources, AccountId = selected?.AccountId ?? target.AccountId};
        }).ToList()};
    }

    /// <summary>Pin effective account identity before the generic artifact planner may skip a download.</summary>
    public static async Task<DownloadWorkset> BindAccountsAsync(DownloadWorkset item, DownloadMethodRegistry registry,
        string? legacyBaiduAccount, CancellationToken ct)
    {
        var targets = new List<CloudDownloadTarget>();
        foreach (var target in item.Targets)
        {
            var source = target.SelectedSource;
            if (target.Excluded || source == null || DownloadMethodCatalog.Select(source) is not {EngineId: "rclone", AccountProvider: not null})
            {targets.Add(target); continue;}
            var bound = await registry.BindAccountAsync(source, source.AccountId ?? target.AccountId ??
                (source.ProviderId == "baidu" ? legacyBaiduAccount : null), ct);
            targets.Add(target with {Sources = target.Sources.Select(s => s.Id == bound.Id ? bound : s).ToList(), AccountId = bound.AccountId});
        }
        return item with {Targets = targets};
    }
}
