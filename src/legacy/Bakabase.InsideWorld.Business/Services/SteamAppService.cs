using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.Modules.ThirdParty.ThirdParties.Steam;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Components.Orm;
using Bootstrap.Models.ResponseModels;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Services;

public class SteamAppService(
    FullMemoryCacheResourceService<BakabaseDbContext, SteamAppDbModel, int> orm,
    SteamClient steamClient,
    SteamLocalLibrary steamLocalLibrary,
    IBOptions<SteamOptions> steamOptions,
    ILogger<SteamAppService> logger)
    : ISteamAppService
{
    public async Task<List<SteamAppDbModel>> GetAll()
    {
        return await orm.GetAll();
    }

    public async Task<SearchResponse<SteamAppDbModel>> Search(string? keyword, int pageIndex, int pageSize)
    {
        Func<SteamAppDbModel, bool>? selector = null;
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword.ToLowerInvariant();
            selector = x =>
                (x.Name != null && x.Name.Contains(kw, StringComparison.OrdinalIgnoreCase)) ||
                x.AppId.ToString().Contains(kw);
        }

        return await orm.Search(selector, pageIndex, pageSize, x => (object)x.UpdatedAt, asc: false);
    }

    public async Task<SteamAppDbModel?> GetByAppId(int appId)
    {
        return await orm.GetFirstOrDefault(x => x.AppId == appId);
    }

    public async Task<List<SteamAppDbModel>> GetByAppIds(IEnumerable<int> appIds)
    {
        var ids = appIds.ToHashSet();
        return await orm.GetAll(x => ids.Contains(x.AppId));
    }

    public async Task AddOrUpdate(SteamAppDbModel app)
    {
        var existing = await orm.GetFirstOrDefault(x => x.AppId == app.AppId);
        if (existing != null)
        {
            // Overwrite API-sourced data
            existing.Name = app.Name;
            existing.PlaytimeForever = app.PlaytimeForever;
            existing.RtimeLastPlayed = app.RtimeLastPlayed;
            existing.ImgIconUrl = app.ImgIconUrl;
            existing.HasCommunityVisibleStats = app.HasCommunityVisibleStats;
            existing.MetadataJson = app.MetadataJson;
            existing.MetadataFetchedAt = app.MetadataFetchedAt;

            // Update account if changed
            if (existing.Account != app.Account && app.Account != null)
            {
                existing.Account = app.Account;
            }

            // Preserve: ResourceId, IsHidden, IsInstalled, InstallPath
            existing.UpdatedAt = DateTime.Now;
            await orm.Update(existing);
        }
        else
        {
            app.CreatedAt = DateTime.Now;
            app.UpdatedAt = DateTime.Now;
            await orm.Add(app);
        }
    }

    public async Task AddOrUpdateRange(IEnumerable<SteamAppDbModel> apps)
    {
        foreach (var app in apps)
        {
            await AddOrUpdate(app);
        }
    }

    public async Task DeleteByAppId(int appId)
    {
        var existing = await orm.GetFirstOrDefault(x => x.AppId == appId);
        if (existing != null)
        {
            await orm.RemoveByKey(existing.Id);
        }
    }

    public async Task SyncFromApi(Func<int, int, Task>? onProgress = null, CancellationToken ct = default)
    {
        var accounts = steamOptions.Value.Accounts;
        if (accounts == null || accounts.Count == 0)
        {
            logger.LogWarning("No Steam accounts configured, skipping sync");
            return;
        }

        var allGames = new List<SteamAppDbModel>();
        var processedAccounts = 0;

        foreach (var account in accounts)
        {
            ct.ThrowIfCancellationRequested();

            if (string.IsNullOrEmpty(account.ApiKey) || string.IsNullOrEmpty(account.SteamId))
            {
                logger.LogWarning("Skipping Steam account '{Name}': missing API key or Steam ID", account.Name);
                processedAccounts++;
                continue;
            }

            try
            {
                logger.LogInformation("Fetching owned games for Steam account '{Name}'", account.Name);
                var games = await steamClient.GetOwnedGames(account.ApiKey, account.SteamId, ct);

                foreach (var game in games)
                {
                    allGames.Add(new SteamAppDbModel
                    {
                        AppId = game.AppId,
                        Name = game.Name,
                        PlaytimeForever = game.PlaytimeForever,
                        RtimeLastPlayed = (int)game.RtimeLastPlayed,
                        ImgIconUrl = game.ImgIconUrl,
                        HasCommunityVisibleStats = game.HasCommunityVisibleStats,
                        Account = account.Name
                    });
                }

                logger.LogInformation("Fetched {Count} games from Steam account '{Name}'", games.Count, account.Name);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to fetch games from Steam account '{Name}'", account.Name);
            }

            processedAccounts++;
            if (onProgress != null)
            {
                await onProgress(processedAccounts * 50 / accounts.Count, allGames.Count);
            }
        }

        // Save all games to DB in batch
        var total = allGames.Count;
        if (total == 0) return;

        var existingApps = (await orm.GetAll()).ToDictionary(x => x.AppId);
        var toAdd = new List<SteamAppDbModel>();
        var toUpdate = new List<SteamAppDbModel>();
        var now = DateTime.Now;

        foreach (var game in allGames)
        {
            ct.ThrowIfCancellationRequested();

            if (existingApps.TryGetValue(game.AppId, out var existing))
            {
                existing.Name = game.Name;
                existing.PlaytimeForever = game.PlaytimeForever;
                existing.RtimeLastPlayed = game.RtimeLastPlayed;
                existing.ImgIconUrl = game.ImgIconUrl;
                existing.HasCommunityVisibleStats = game.HasCommunityVisibleStats;
                existing.MetadataJson = game.MetadataJson;
                if (existing.Account != game.Account && game.Account != null)
                {
                    existing.Account = game.Account;
                }
                existing.UpdatedAt = now;
                toUpdate.Add(existing);
            }
            else
            {
                game.CreatedAt = now;
                game.UpdatedAt = now;
                toAdd.Add(game);
            }
        }

        if (toUpdate.Count > 0)
        {
            await orm.UpdateRange(toUpdate);
        }

        if (toAdd.Count > 0)
        {
            await orm.AddRange(toAdd);
        }

        if (onProgress != null)
        {
            await onProgress(100, total);
        }

        logger.LogInformation("Steam sync complete: {Count} games synced ({Added} added, {Updated} updated)",
            total, toAdd.Count, toUpdate.Count);

        // Detect locally installed apps and update installation status
        await UpdateInstallationStatus();
    }

    public async Task UpdateInstallationStatus()
    {
        var installedApps = steamLocalLibrary.DetectInstalledApps();
        var allApps = await orm.GetAll();
        var toUpdate = new List<SteamAppDbModel>();
        var now = DateTime.Now;

        foreach (var app in allApps)
        {
            var wasInstalled = app.IsInstalled;
            var oldPath = app.InstallPath;

            if (installedApps.TryGetValue(app.AppId, out var installPath))
            {
                app.IsInstalled = true;
                app.InstallPath = installPath;
            }
            else
            {
                app.IsInstalled = false;
                app.InstallPath = null;
            }

            if (app.IsInstalled != wasInstalled || app.InstallPath != oldPath)
            {
                app.UpdatedAt = now;
                toUpdate.Add(app);
            }
        }

        if (toUpdate.Count > 0)
        {
            await orm.UpdateRange(toUpdate);
            logger.LogInformation(
                "Updated installation status for {Count} Steam app(s): {Installed} installed, {Uninstalled} not installed",
                toUpdate.Count,
                toUpdate.Count(a => a.IsInstalled),
                toUpdate.Count(a => !a.IsInstalled));
        }
    }

    public async Task SetHidden(int appId, bool isHidden)
    {
        var app = await GetByAppId(appId);
        if (app == null)
        {
            throw new Exception($"Steam app {appId} not found");
        }

        app.IsHidden = isHidden;
        app.UpdatedAt = DateTime.Now;
        await orm.Update(app);
    }
}
