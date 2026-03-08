using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.Modules.ThirdParty.ThirdParties.DLsite;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Components.Orm;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Bakabase.InsideWorld.Business.Services;

public class DLsiteWorkService(
    FullMemoryCacheResourceService<BakabaseDbContext, DLsiteWorkDbModel, int> orm,
    DLsiteClient dlsiteClient,
    IBOptions<DLsiteOptions> dlsiteOptions,
    ILogger<DLsiteWorkService> logger)
    : IDLsiteWorkService
{
    public async Task<List<DLsiteWorkDbModel>> GetAll()
    {
        return await orm.GetAll();
    }

    public async Task<DLsiteWorkDbModel?> GetByWorkId(string workId)
    {
        return await orm.GetFirstOrDefault(x => x.WorkId == workId);
    }

    public async Task<List<DLsiteWorkDbModel>> GetByWorkIds(IEnumerable<string> workIds)
    {
        var ids = workIds.ToHashSet();
        return await orm.GetAll(x => ids.Contains(x.WorkId));
    }

    public async Task AddOrUpdate(DLsiteWorkDbModel work)
    {
        var existing = await orm.GetFirstOrDefault(x => x.WorkId == work.WorkId);
        if (existing != null)
        {
            work.Id = existing.Id;
            work.CreatedAt = existing.CreatedAt;
            work.UpdatedAt = DateTime.Now;
            await orm.Update(work);
        }
        else
        {
            work.CreatedAt = DateTime.Now;
            work.UpdatedAt = DateTime.Now;
            await orm.Add(work);
        }
    }

    public async Task AddOrUpdateRange(IEnumerable<DLsiteWorkDbModel> works)
    {
        foreach (var work in works)
        {
            await AddOrUpdate(work);
        }
    }

    public async Task DeleteByWorkId(string workId)
    {
        var existing = await orm.GetFirstOrDefault(x => x.WorkId == workId);
        if (existing != null)
        {
            await orm.RemoveByKey(existing.Id);
        }
    }

    public async Task SyncFromApi(Func<int, int, Task>? onProgress = null, CancellationToken ct = default)
    {
        var accounts = dlsiteOptions.Value.Accounts;
        if (accounts == null || accounts.Count == 0)
        {
            logger.LogWarning("No DLsite accounts configured, skipping sync");
            return;
        }

        var allWorks = new Dictionary<string, DLsiteWorkDbModel>();
        var processedAccounts = 0;

        foreach (var account in accounts)
        {
            ct.ThrowIfCancellationRequested();

            if (string.IsNullOrEmpty(account.Cookie))
            {
                logger.LogWarning("Skipping DLsite account '{Name}': no cookie configured", account.Name);
                processedAccounts++;
                continue;
            }

            try
            {
                logger.LogInformation("Fetching purchase list for DLsite account '{Name}'", account.Name);

                // Step 1: Get purchase count
                var count = await dlsiteClient.GetPurchaseCountAsync(account.Cookie, ct);
                logger.LogInformation("DLsite account '{Name}' has {Count} purchased works", account.Name, count);

                if (count == 0)
                {
                    processedAccounts++;
                    continue;
                }

                // Step 2: Get all sales (work IDs + dates)
                var sales = await dlsiteClient.GetPurchaseSalesAsync(account.Cookie, ct);
                logger.LogInformation("Fetched {Count} sales records from DLsite account '{Name}'",
                    sales.Count, account.Name);

                if (sales.Count == 0)
                {
                    processedAccounts++;
                    continue;
                }

                var salesDateMap = sales
                    .Where(s => !string.IsNullOrEmpty(s.Workno))
                    .ToDictionary(s => s.Workno, s => s.SalesDate);

                // Step 3: Fetch work details in batches
                var workIds = salesDateMap.Keys.ToList();
                var workDetails = await dlsiteClient.GetPurchaseWorksAsync(account.Cookie, workIds, ct);
                logger.LogInformation("Fetched {Count} work details from DLsite account '{Name}'",
                    workDetails.Count, account.Name);

                // Step 4: Map to DB models
                foreach (var work in workDetails)
                {
                    if (string.IsNullOrEmpty(work.Workno)) continue;
                    if (allWorks.ContainsKey(work.Workno)) continue;

                    var dbModel = new DLsiteWorkDbModel
                    {
                        WorkId = work.Workno,
                        Title = work.Name?.GetBestName(),
                        Circle = work.Maker?.Name?.GetBestName(),
                        WorkType = work.WorkType,
                        CoverUrl = work.WorkFiles?.Main,
                        IsPurchased = true,
                        MetadataJson = JsonConvert.SerializeObject(work),
                        MetadataFetchedAt = DateTime.Now,
                    };

                    allWorks[work.Workno] = dbModel;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to sync DLsite account '{Name}'", account.Name);
            }

            processedAccounts++;
            if (onProgress != null)
            {
                await onProgress(processedAccounts * 50 / accounts.Count, allWorks.Count);
            }
        }

        // Save all works to DB
        var total = allWorks.Count;
        if (total == 0)
        {
            if (onProgress != null)
            {
                await onProgress(100, 0);
            }

            return;
        }

        var saved = 0;
        foreach (var work in allWorks.Values)
        {
            ct.ThrowIfCancellationRequested();
            await AddOrUpdate(work);
            saved++;
            if (onProgress != null)
            {
                await onProgress(50 + saved * 50 / total, saved);
            }
        }

        logger.LogInformation("DLsite sync complete: {Count} works synced", total);
    }
}
