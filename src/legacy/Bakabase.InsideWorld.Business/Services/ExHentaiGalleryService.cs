using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.Modules.ThirdParty.ThirdParties.ExHentai;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Components.Orm;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Services;

public class ExHentaiGalleryService(
    FullMemoryCacheResourceService<BakabaseDbContext, ExHentaiGalleryDbModel, int> orm,
    ExHentaiClient exHentaiClient,
    IBOptions<ExHentaiOptions> exHentaiOptions,
    ILogger<ExHentaiGalleryService> logger)
    : IExHentaiGalleryService
{
    private static readonly Regex GalleryIdTokenPattern = new(@"/g/(\d+)/([a-f0-9]+)", RegexOptions.Compiled);

    public async Task<List<ExHentaiGalleryDbModel>> GetAll()
    {
        return await orm.GetAll();
    }

    public async Task<ExHentaiGalleryDbModel?> GetByGalleryId(long galleryId, string galleryToken)
    {
        return await orm.GetFirstOrDefault(x => x.GalleryId == galleryId && x.GalleryToken == galleryToken);
    }

    public async Task AddOrUpdate(ExHentaiGalleryDbModel gallery)
    {
        var existing = await orm.GetFirstOrDefault(x =>
            x.GalleryId == gallery.GalleryId && x.GalleryToken == gallery.GalleryToken);
        if (existing != null)
        {
            gallery.Id = existing.Id;
            gallery.UpdatedAt = DateTime.Now;
            await orm.Update(gallery);
        }
        else
        {
            gallery.CreatedAt = DateTime.Now;
            gallery.UpdatedAt = DateTime.Now;
            await orm.Add(gallery);
        }
    }

    public async Task AddOrUpdateRange(IEnumerable<ExHentaiGalleryDbModel> galleries)
    {
        foreach (var gallery in galleries)
        {
            await AddOrUpdate(gallery);
        }
    }

    public async Task DeleteById(int id)
    {
        await orm.RemoveByKey(id);
    }

    public async Task SyncFromApi(Func<int, int, Task>? onProgress = null, CancellationToken ct = default)
    {
        var accounts = exHentaiOptions.Value.Accounts;
        if (accounts == null || accounts.Count == 0)
        {
            logger.LogWarning("No ExHentai accounts configured, skipping sync");
            return;
        }

        var allGalleries = new List<ExHentaiGalleryDbModel>();

        // Parse favorites pages for each favorite category (0-9)
        for (var favCat = 0; favCat < 10; favCat++)
        {
            ct.ThrowIfCancellationRequested();

            var pageUrl = $"https://exhentai.org/favorites.php?favcat={favCat}";
            try
            {
                while (!string.IsNullOrEmpty(pageUrl))
                {
                    ct.ThrowIfCancellationRequested();

                    var list = await exHentaiClient.ParseList(pageUrl);

                    foreach (var resource in list.Resources)
                    {
                        if (string.IsNullOrEmpty(resource.Url)) continue;

                        var match = GalleryIdTokenPattern.Match(resource.Url);
                        if (!match.Success) continue;

                        var galleryId = long.Parse(match.Groups[1].Value);
                        var galleryToken = match.Groups[2].Value;

                        allGalleries.Add(new ExHentaiGalleryDbModel
                        {
                            GalleryId = galleryId,
                            GalleryToken = galleryToken,
                            Title = resource.Name,
                            TitleJpn = resource.RawName != resource.Name ? resource.RawName : null,
                            Category = resource.Category.ToString(),
                            CoverUrl = resource.CoverUrl
                        });
                    }

                    pageUrl = list.NextListUrl;

                    if (onProgress != null)
                    {
                        await onProgress(favCat * 10 + 5, allGalleries.Count);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to parse ExHentai favorites page (favcat={FavCat})", favCat);
            }

            if (onProgress != null)
            {
                await onProgress((favCat + 1) * 10, allGalleries.Count);
            }
        }

        logger.LogInformation("Fetched {Count} galleries from ExHentai favorites", allGalleries.Count);

        // Deduplicate by gallery ID
        var uniqueGalleries = allGalleries
            .GroupBy(g => g.GalleryId)
            .Select(g => g.First())
            .ToList();

        // Save all galleries to DB
        var total = uniqueGalleries.Count;
        if (total == 0) return;

        var saved = 0;
        foreach (var gallery in uniqueGalleries)
        {
            ct.ThrowIfCancellationRequested();
            await AddOrUpdate(gallery);
            saved++;
        }

        logger.LogInformation("ExHentai sync complete: {Count} galleries synced", total);
    }
}
