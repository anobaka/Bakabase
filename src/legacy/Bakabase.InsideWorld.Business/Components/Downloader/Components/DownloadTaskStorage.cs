using System;
using System.IO;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Infrastructures.Components.App;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.Downloader.Models.Db;
using Bakabase.InsideWorld.Models.Constants;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Components;

/// <summary>User destinations are restricted; queue entries owned by an acquisition have one exact internal destination.</summary>
public static class DownloadTaskStorage
{
    public static async Task EnsureAllowedAsync(IServiceProvider services, DownloadTask task)
    {
        var storage = services.GetRequiredService<IUserStoragePolicy>();
        if (storage.IsPathAllowed(task.DownloadPath)) return;
        if (task.Id > 0 && task.ThirdPartyId == ThirdPartyId.ExHentai)
        {
            await using var scope = services.CreateAsyncScope();
            var owner = await scope.ServiceProvider.GetRequiredService<BakabaseDbContext>()
                .Set<DownloadResultOwnerDbModel>().AsNoTracking()
                .SingleOrDefaultAsync(o => o.DownloadTaskId == task.Id);
            if (owner != null)
            {
                EnsureAcquisitionDirectory(task.DownloadPath,
                    services.GetRequiredService<AppService>().AppDataDirectory, owner.AcquisitionTaskId);
                return;
            }
        }
        storage.EnsurePathAllowed(task.DownloadPath);
    }

    public static void EnsureAcquisitionDirectory(string path, string appDataDirectory, int acquisitionTaskId)
    {
        var root = Path.GetFullPath(appDataDirectory);
        var expected = Path.Combine(root, "acquisition", acquisitionTaskId.ToString(), "platform");
        // Accept only the server-generated directory, not a task option claiming to be internal.
        if (acquisitionTaskId <= 0 || !string.Equals(path, expected, StringComparison.Ordinal))
            throw new IOException("The acquisition download directory no longer matches its owning task.");
        var current = root;
        foreach (var part in new[] {"acquisition", acquisitionTaskId.ToString(), "platform"})
        {
            current = Path.Combine(current, part);
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("The acquisition download directory cannot pass through a symbolic link.");
        }
    }
}
