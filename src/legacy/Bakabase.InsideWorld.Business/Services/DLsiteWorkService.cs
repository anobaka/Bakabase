using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.LocaleEmulator;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.SevenZip;
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
    SevenZipService sevenZipService,
    LocaleEmulatorService localeEmulatorService,
    ILogger<DLsiteWorkService> logger)
    : IDLsiteWorkService
{
    private DLsiteOptions DLsiteOptionsValue => dlsiteOptions.Value;

    /// <summary>
    /// Finds the cookie for the account associated with a work.
    /// Falls back to the first account with a cookie if the associated account is not found.
    /// </summary>
    private string GetCookieForWork(DLsiteWorkDbModel work)
    {
        var accounts = DLsiteOptionsValue.Accounts;
        if (accounts == null || accounts.Count == 0)
        {
            throw new Exception("No DLsite accounts configured");
        }

        // Try the associated account first
        if (!string.IsNullOrEmpty(work.Account))
        {
            var account = accounts.FirstOrDefault(a =>
                a.Name == work.Account && !string.IsNullOrEmpty(a.Cookie));
            if (account != null)
            {
                return account.Cookie!;
            }
        }

        // Fallback to first account with cookie
        var fallback = accounts.FirstOrDefault(a => !string.IsNullOrEmpty(a.Cookie));
        return fallback?.Cookie ?? throw new Exception("No DLsite account with cookie configured");
    }
    private static readonly HashSet<string> ExecutableExtensions = [".exe"];
    private static readonly HashSet<string> ImageExtensions = [".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp"];
    private static readonly HashSet<string> AudioExtensions = [".mp3", ".wav", ".flac", ".ogg", ".aac", ".m4a", ".wma"];
    private static readonly HashSet<string> VideoExtensions = [".mp4", ".avi", ".mkv", ".wmv", ".mov", ".flv", ".webm"];
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
        var accounts = DLsiteOptionsValue.Accounts;
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
                var cookie = account.Cookie;

                // Step 1: Get purchase count
                var count = await dlsiteClient.GetPurchaseCountAsync(cookie, ct);
                logger.LogInformation("DLsite account '{Name}' has {Count} purchased works", account.Name, count);

                if (count == 0)
                {
                    processedAccounts++;
                    continue;
                }

                // Step 2: Get all sales (work IDs + dates)
                var sales = await dlsiteClient.GetPurchaseSalesAsync(cookie, ct);
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
                var workDetails = await dlsiteClient.GetPurchaseWorksAsync(cookie, workIds, ct);
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
                        Account = account.Name,
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

    public async Task DownloadWork(string workId, Func<int, string, Task>? onProgress = null, CancellationToken ct = default)
    {
        var work = await GetByWorkId(workId);
        if (work == null)
        {
            throw new Exception($"Work {workId} not found");
        }

        var cookie = GetCookieForWork(work);

        var defaultPath = DLsiteOptionsValue.DefaultPath;
        if (string.IsNullOrEmpty(defaultPath))
        {
            throw new Exception("Download path not configured. Please set the default download path in DLsite settings.");
        }

        var workDir = Path.Combine(defaultPath, workId);
        Directory.CreateDirectory(workDir);

        // Get download links (follows redirects manually to handle both direct and serial page flows)
        if (onProgress != null)
        {
            await onProgress(0, $"[{workId}] 0%");
        }

        var downloadInfo = await dlsiteClient.ResolveDownloadAsync(cookie, workId, ct);
        var links = downloadInfo.Links;
        if (links.Count == 0)
        {
            throw new Exception($"No download links found for work {workId}");
        }

        // Save DRM key if found
        if (downloadInfo.DrmKey != null && work.DrmKey != downloadInfo.DrmKey)
        {
            work.DrmKey = downloadInfo.DrmKey;
            work.UpdatedAt = DateTime.Now;
            await orm.Update(work);
        }

        logger.LogInformation("Found {Count} download links for work {WorkId}", links.Count, workId);

        // Download all files
        var downloadedFiles = new List<string>();
        for (var i = 0; i < links.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var link = links[i];
            var destPath = Path.Combine(workDir, link.FileName);

            if (DLsiteOptionsValue.SkipExisting && File.Exists(destPath))
            {
                logger.LogInformation("Skipping existing file: {Path}", destPath);
                downloadedFiles.Add(destPath);
                continue;
            }

            var fileIndex = i;
            await dlsiteClient.DownloadFileAsync(
                cookie,
                link.Url,
                destPath,
                (downloaded, total) =>
                {
                    var fileProgress = total > 0 ? (int)(downloaded * 100 / total) : 0;
                    var overallProgress = (fileIndex * 100 + fileProgress) / links.Count;
                    onProgress?.Invoke(
                        Math.Min(overallProgress, 95),
                        $"{link.FileName} ({fileIndex + 1}/{links.Count}) {downloaded / 1024 / 1024}MB/{total / 1024 / 1024}MB");
                },
                ct);

            downloadedFiles.Add(destPath);
            logger.LogInformation("Downloaded: {Path}", destPath);
        }

        // Extract archives
        var extractDir = Path.Combine(workDir, "extracted");
        var hasArchives = false;

        foreach (var file in downloadedFiles)
        {
            ct.ThrowIfCancellationRequested();
            var ext = Path.GetExtension(file).ToLowerInvariant();
            if (ext is ".zip" or ".rar" or ".7z" or ".lzh")
            {
                hasArchives = true;
                if (onProgress != null)
                {
                    await onProgress(96, Path.GetFileName(file));
                }

                // Use codepage 932 (Shift-JIS) for Japanese filenames
                await sevenZipService.Extract(file, extractDir, ct, codePage: 932);
                logger.LogInformation("Extracted: {Path}", file);
            }
        }

        // Update work record
        work.IsDownloaded = true;
        work.LocalPath = hasArchives ? extractDir : workDir;
        work.UpdatedAt = DateTime.Now;
        await orm.Update(work);

        if (onProgress != null)
        {
            await onProgress(100, "100%");
        }

        logger.LogInformation("Download complete for work {WorkId} at {Path}", workId, work.LocalPath);
    }

    public async Task<string?> FetchDrmKey(string workId, CancellationToken ct = default)
    {
        var work = await GetByWorkId(workId);
        if (work == null)
        {
            throw new Exception($"Work {workId} not found");
        }

        // Return cached DRM key if already fetched
        if (work.DrmKey != null)
        {
            return work.DrmKey;
        }

        var cookie = GetCookieForWork(work);

        var downloadInfo = await dlsiteClient.ResolveDownloadAsync(cookie, workId, ct);

        // Save to DB (empty string means no DRM, non-empty means DRM key found)
        work.DrmKey = downloadInfo.DrmKey ?? string.Empty;
        work.UpdatedAt = DateTime.Now;
        await orm.Update(work);

        return work.DrmKey;
    }

    public async Task LaunchWork(string workId, CancellationToken ct = default)
    {
        var work = await GetByWorkId(workId);
        if (work == null)
        {
            throw new Exception($"Work {workId} not found");
        }

        if (string.IsNullOrEmpty(work.LocalPath) || !Directory.Exists(work.LocalPath))
        {
            throw new Exception($"Local path not found for work {workId}. Please download the work first.");
        }

        var playableFiles = FindPlayableFiles(work.LocalPath, work.WorkType);
        if (playableFiles.Count == 0)
        {
            throw new Exception($"No playable files found for work {workId}");
        }

        var targetFile = playableFiles[0];
        var ext = Path.GetExtension(targetFile).ToLowerInvariant();

        if (ExecutableExtensions.Contains(ext))
        {
            // Launch executable with Locale Emulator (for Japanese locale)
            if (localeEmulatorService.IsAvailableOnCurrentPlatform)
            {
                try
                {
                    await localeEmulatorService.LaunchWithLocaleEmulator(targetFile, ct);
                    return;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to launch with Locale Emulator, falling back to direct launch");
                }
            }

            // Fallback: direct launch
            Process.Start(new ProcessStartInfo
            {
                FileName = targetFile,
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(targetFile)
            });
        }
        else
        {
            // Non-executable files: open with system default application
            Process.Start(new ProcessStartInfo
            {
                FileName = targetFile,
                UseShellExecute = true
            });
        }
    }

    public List<string> FindPlayableFiles(string localPath, string? workType)
    {
        if (!Directory.Exists(localPath))
        {
            return [];
        }

        var extensions = GetPlayableExtensions(workType);
        var files = Directory.EnumerateFiles(localPath, "*.*", SearchOption.AllDirectories)
            .Where(f => extensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .OrderBy(f => f)
            .ToList();

        return files;
    }

    private static HashSet<string> GetPlayableExtensions(string? workType)
    {
        return workType?.ToLowerInvariant() switch
        {
            "GCM" or "GAM" or "ACN" or "game" or "tool" => ExecutableExtensions,
            "MNG" or "manga" or "comic" => ImageExtensions,
            "SOU" or "voice" or "asmr" or "audio" => AudioExtensions,
            "MOV" or "video" or "anime" => VideoExtensions,
            _ => [..ExecutableExtensions, ..ImageExtensions, ..AudioExtensions, ..VideoExtensions]
        };
    }

    private static readonly System.Text.RegularExpressions.Regex WorkIdPattern =
        new(@"^[Rr][JjEeBbVv]\d{6,8}$", System.Text.RegularExpressions.RegexOptions.Compiled);

    public async Task<int> ScanFolder(string folderPath, Func<int, int, Task>? onProgress = null, CancellationToken ct = default)
    {
        if (!Directory.Exists(folderPath))
        {
            logger.LogWarning("Scan folder does not exist: {Path}", folderPath);
            return 0;
        }

        // Get all known work IDs (only match works the account owns)
        var knownWorks = (await orm.GetAll()).ToDictionary(w => w.WorkId, StringComparer.OrdinalIgnoreCase);

        // Find all subdirectories (including nested) whose name matches a DLsite work ID pattern
        var dirs = Directory.EnumerateDirectories(folderPath, "*", SearchOption.AllDirectories)
            .Select(d => (Path: d, Name: Path.GetFileName(d)))
            .Where(d => WorkIdPattern.IsMatch(d.Name))
            .ToList();

        logger.LogInformation("Found {Count} directories matching DLsite work ID pattern in {Path}", dirs.Count, folderPath);

        var matched = 0;
        for (var i = 0; i < dirs.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var (dirPath, dirName) = dirs[i];
            var workId = dirName.ToUpperInvariant();

            if (knownWorks.TryGetValue(workId, out var existing))
            {
                if (!existing.IsDownloaded || string.IsNullOrEmpty(existing.LocalPath))
                {
                    existing.IsDownloaded = true;
                    existing.LocalPath = dirPath;
                    existing.UpdatedAt = DateTime.Now;
                    await orm.Update(existing);
                    matched++;
                }
            }
            // Only match works the account owns - skip unknown work IDs

            if (onProgress != null)
            {
                await onProgress((i + 1) * 100 / dirs.Count, matched);
            }
        }

        logger.LogInformation("Scan complete: {Matched} works matched out of {Total} directories", matched, dirs.Count);
        return matched;
    }

    public async Task<int> ScanConfiguredFolders(Func<int, int, Task>? onProgress = null, CancellationToken ct = default)
    {
        var folders = DLsiteOptionsValue.ScanFolders;
        if (folders == null || folders.Count == 0)
        {
            logger.LogWarning("No scan folders configured");
            return 0;
        }

        var totalMatched = 0;
        for (var i = 0; i < folders.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var folder = folders[i];
            var folderMatched = await ScanFolder(folder, null, ct);
            totalMatched += folderMatched;

            if (onProgress != null)
            {
                await onProgress((i + 1) * 100 / folders.Count, totalMatched);
            }
        }

        return totalMatched;
    }

    public async Task SetHidden(string workId, bool isHidden)
    {
        var work = await GetByWorkId(workId);
        if (work == null)
        {
            throw new Exception($"Work {workId} not found");
        }

        work.IsHidden = isHidden;
        work.UpdatedAt = DateTime.Now;
        await orm.Update(work);
    }
}
