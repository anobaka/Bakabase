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
using Bootstrap.Models.ResponseModels;
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

    public async Task<SearchResponse<ExHentaiGalleryDbModel>> Search(string? keyword, int pageIndex, int pageSize)
    {
        Func<ExHentaiGalleryDbModel, bool>? selector = null;
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword.ToLowerInvariant();
            selector = x =>
                (x.Title != null && x.Title.Contains(kw, StringComparison.OrdinalIgnoreCase)) ||
                (x.TitleJpn != null && x.TitleJpn.Contains(kw, StringComparison.OrdinalIgnoreCase)) ||
                x.GalleryId.ToString().Contains(kw) ||
                (x.Category != null && x.Category.Contains(kw, StringComparison.OrdinalIgnoreCase));
        }

        return await orm.Search(selector, pageIndex, pageSize, x => (object)x.UpdatedAt, asc: false);
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
            // Overwrite API-sourced data
            existing.Title = gallery.Title;
            existing.TitleJpn = gallery.TitleJpn;
            existing.Category = gallery.Category;
            existing.CoverUrl = gallery.CoverUrl;
            existing.MetadataJson = gallery.MetadataJson;
            existing.MetadataFetchedAt = gallery.MetadataFetchedAt;

            // Update account if changed
            if (existing.Account != gallery.Account && gallery.Account != null)
            {
                existing.Account = gallery.Account;
            }

            // Preserve: ResourceId, IsHidden, IsDownloaded, LocalPath
            existing.UpdatedAt = DateTime.Now;
            await orm.Update(existing);
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

    public async Task DeleteLocalFiles(long galleryId, string galleryToken)
    {
        var gallery = await GetByGalleryId(galleryId, galleryToken);
        if (gallery == null)
        {
            throw new Exception($"Gallery {galleryId}/{galleryToken} not found");
        }

        if (!string.IsNullOrEmpty(gallery.LocalPath) && System.IO.Directory.Exists(gallery.LocalPath))
        {
            System.IO.Directory.Delete(gallery.LocalPath, true);
            logger.LogInformation("Deleted local files for gallery {GalleryId} at {Path}", galleryId, gallery.LocalPath);
        }

        gallery.IsDownloaded = false;
        gallery.LocalPath = null;
        gallery.UpdatedAt = DateTime.Now;
        await orm.Update(gallery);
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

        // Determine the active account name (the one whose cookie is used by the HTTP client)
        var activeAccount = accounts.FirstOrDefault(a => !string.IsNullOrEmpty(a.Cookie));
        var accountName = activeAccount?.Name;

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
                            CoverUrl = resource.CoverUrl,
                            Account = accountName
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

        // Save all galleries to DB in batch
        var total = uniqueGalleries.Count;
        if (total == 0) return;

        var existingGalleries = (await orm.GetAll())
            .ToDictionary(x => (x.GalleryId, x.GalleryToken));
        var toAdd = new List<ExHentaiGalleryDbModel>();
        var toUpdate = new List<ExHentaiGalleryDbModel>();
        var now = DateTime.Now;

        foreach (var gallery in uniqueGalleries)
        {
            ct.ThrowIfCancellationRequested();

            if (existingGalleries.TryGetValue((gallery.GalleryId, gallery.GalleryToken), out var existing))
            {
                existing.Title = gallery.Title;
                existing.TitleJpn = gallery.TitleJpn;
                existing.Category = gallery.Category;
                existing.CoverUrl = gallery.CoverUrl;
                existing.MetadataJson = gallery.MetadataJson;
                existing.MetadataFetchedAt = gallery.MetadataFetchedAt;
                if (existing.Account != gallery.Account && gallery.Account != null)
                {
                    existing.Account = gallery.Account;
                }
                existing.UpdatedAt = now;
                toUpdate.Add(existing);
            }
            else
            {
                gallery.CreatedAt = now;
                gallery.UpdatedAt = now;
                toAdd.Add(gallery);
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

        logger.LogInformation("ExHentai sync complete: {Count} galleries synced ({Added} added, {Updated} updated)",
            total, toAdd.Count, toUpdate.Count);
    }

    public async Task SetHidden(long galleryId, string galleryToken, bool isHidden)
    {
        var gallery = await GetByGalleryId(galleryId, galleryToken);
        if (gallery == null)
        {
            throw new Exception($"Gallery {galleryId}/{galleryToken} not found");
        }

        gallery.IsHidden = isHidden;
        gallery.UpdatedAt = DateTime.Now;
        await orm.Update(gallery);
    }

    public async Task ClearAllMetadata()
    {
        var galleries = await orm.GetAll(g => g.MetadataJson != null);
        foreach (var gallery in galleries)
        {
            gallery.MetadataJson = null;
            gallery.MetadataFetchedAt = null;
        }

        if (galleries.Count > 0)
        {
            await orm.UpdateRange(galleries);
        }
    }
}
