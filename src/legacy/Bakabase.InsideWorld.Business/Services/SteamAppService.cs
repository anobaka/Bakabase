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
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Services;

public class SteamAppService(
    FullMemoryCacheResourceService<BakabaseDbContext, SteamAppDbModel, int> orm,
    SteamClient steamClient,
    IBOptions<SteamOptions> steamOptions,
    ILogger<SteamAppService> logger)
    : ISteamAppService
{
    public async Task<List<SteamAppDbModel>> GetAll()
    {
        return await orm.GetAll();
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

        // Save all games to DB
        var total = allGames.Count;
        if (total == 0) return;

        var saved = 0;
        foreach (var game in allGames)
        {
            ct.ThrowIfCancellationRequested();
            await AddOrUpdate(game);
            saved++;
            if (onProgress != null)
            {
                await onProgress(50 + saved * 50 / total, saved);
            }
        }

        logger.LogInformation("Steam sync complete: {Count} games synced", total);
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
