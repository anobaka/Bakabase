using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.LocaleEmulator;
using Bakabase.Modules.ThirdParty.ThirdParties.DLsite;
using Bakabase.Modules.ThirdParty.ThirdParties.DLsite.Models;
using Bakabase.Infrastructures.Components.Configurations.App;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Components.Orm;
using Bootstrap.Models.ResponseModels;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Bakabase.InsideWorld.Business.Services;

public class DLsiteWorkService(
    FullMemoryCacheResourceService<BakabaseDbContext, DLsiteWorkDbModel, int> orm,
    DLsiteClient dlsiteClient,
    IBOptions<DLsiteOptions> dlsiteOptions,
    IBOptions<AppOptions> appOptions,
    DLsiteArchiveExtractor archiveExtractor,
    LocaleEmulatorService localeEmulatorService,
    ILogger<DLsiteWorkService> logger)
    : IDLsiteWorkService
{
    private DLsiteOptions DLsiteOptionsValue => dlsiteOptions.Value;

    /// <summary>
    /// Finds the cookie for the account associated with a work.
    /// Falls back to the first account with a cookie if the associated account is not found.
    /// </summary>
    private (string Cookie, string AccountKey) GetCookieForWork(DLsiteWorkDbModel work)
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
                return (account.Cookie!, account.Name);
            }
        }

        // Fallback to first account with cookie
        var fallback = accounts.FirstOrDefault(a => !string.IsNullOrEmpty(a.Cookie));
        if (fallback == null)
        {
            throw new Exception("No DLsite account with cookie configured");
        }
        return (fallback.Cookie!, fallback.Name);
    }
    private static readonly HashSet<string> ExecutableExtensions = [".exe"];
    private static readonly HashSet<string> ImageExtensions = [".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp"];
    private static readonly HashSet<string> AudioExtensions = [".mp3", ".wav", ".flac", ".ogg", ".aac", ".m4a", ".wma"];
    private static readonly HashSet<string> VideoExtensions = [".mp4", ".avi", ".mkv", ".wmv", ".mov", ".flv", ".webm"];

    /// <summary>
    /// Executable filenames (case-insensitive) that should never be selected as the main game executable.
    /// </summary>
    private static readonly HashSet<string> ExeBlacklist = new(StringComparer.OrdinalIgnoreCase)
    {
        "UnityCrashHandler.exe",
        "UnityCrashHandler32.exe",
        "UnityCrashHandler64.exe",
        "UnityBugReporter.exe",
        "unins000.exe",
        "unins001.exe",
        "uninstall.exe",
        "dxwebsetup.exe",
        "DXSETUP.exe",
        "setup.exe",
    };

    /// <summary>
    /// Subdirectory names (case-insensitive) that typically contain runtime/engine files rather than the main executable.
    /// </summary>
    private static readonly HashSet<string> LowPriorityDirNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "_CommonRedist",
        "__support",
        "lib",
        "engine",
        "redist",
        "DirectX",
        "vcredist",
        "dotnet",
        "Mono",
        "MonoBleedingEdge",
    };
    public async Task<List<DLsiteWorkDbModel>> GetAll()
    {
        return await orm.GetAll();
    }

    public async Task<SearchResponse<DLsiteWorkDbModel>> Search(string? keyword, bool showHidden, int pageIndex, int pageSize)
    {
        Func<DLsiteWorkDbModel, bool>? selector = null;
        var hasKeyword = !string.IsNullOrWhiteSpace(keyword);

        if (hasKeyword || !showHidden)
        {
            var kw = keyword?.ToLowerInvariant();
            selector = x =>
            {
                if (!showHidden && x.IsHidden) return false;
                if (hasKeyword && kw != null)
                {
                    return (x.Title != null && x.Title.Contains(kw, StringComparison.OrdinalIgnoreCase)) ||
                           x.WorkId.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                           (x.Circle != null && x.Circle.Contains(kw, StringComparison.OrdinalIgnoreCase));
                }
                return true;
            };
        }

        return await orm.Search(selector, pageIndex, pageSize, x => (object)x.UpdatedAt, asc: false);
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
            // Overwrite DLsite-sourced data
            existing.Title = work.Title;
            existing.Circle = work.Circle;
            existing.WorkType = work.WorkType;
            existing.CoverUrl = work.CoverUrl;
            existing.SalesDate = work.SalesDate;
            existing.PurchasedAt = work.PurchasedAt;
            existing.IsPurchased = work.IsPurchased;

            // If account changed, update account and reset DRM key
            if (existing.Account != work.Account)
            {
                existing.Account = work.Account;
                existing.DrmKey = null;
            }

            // Preserve: ResourceId, IsDownloaded, IsHidden, LocalPath, DrmKey (unless account changed)
            existing.UpdatedAt = DateTime.Now;
            await orm.Update(existing);
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

                var purchaseDateMap = sales
                    .Where(s => !string.IsNullOrEmpty(s.Workno))
                    .ToDictionary(s => s.Workno, s => s.SalesDate);

                // Step 3: Fetch work details in batches
                var workIds = purchaseDateMap.Keys.ToList();
                var workDetails = await dlsiteClient.GetPurchaseWorksAsync(cookie, workIds, ct);
                logger.LogInformation("Fetched {Count} work details from DLsite account '{Name}'",
                    workDetails.Count, account.Name);

                // Step 4: Map to DB models
                foreach (var work in workDetails)
                {
                    if (string.IsNullOrEmpty(work.Workno)) continue;
                    if (allWorks.ContainsKey(work.Workno)) continue;

                    // DLsite API returns dates in JST (Asia/Tokyo)
                    var jst = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo");
                    var userTz = appOptions.Value.EffectiveTimeZone;

                    // SalesDate = work release date (from work detail's sales_date or regist_date)
                    DateTime? salesDate = null;
                    var salesDateSource = work.SalesDate ?? work.RegistDate;
                    if (salesDateSource != null &&
                        DateTime.TryParse(salesDateSource, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsedSalesDate))
                    {
                        salesDate = ConvertFromJst(parsedSalesDate, jst, userTz);
                    }

                    // PurchasedAt = when the user bought it (from sales API)
                    DateTime? purchasedAt = null;
                    if (purchaseDateMap.TryGetValue(work.Workno, out var purchaseDateStr) &&
                        DateTime.TryParse(purchaseDateStr, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsedPurchaseDate))
                    {
                        purchasedAt = ConvertFromJst(parsedPurchaseDate, jst, userTz);
                    }

                    var dbModel = new DLsiteWorkDbModel
                    {
                        WorkId = work.Workno,
                        Title = work.Name?.GetBestName(),
                        Circle = work.Maker?.Name?.GetBestName(),
                        WorkType = work.WorkType,
                        CoverUrl = work.WorkFiles?.Main,
                        Account = account.Name,
                        SalesDate = salesDate,
                        PurchasedAt = purchasedAt,
                        IsPurchased = true,
                        UseLocaleEmulator = ShouldUseLocaleEmulator(work.WorkType),
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
                throw new BTaskException($"Failed to sync DLsite account '{account.Name}': {ex.Message}", ex.Message);
            }

            processedAccounts++;
            if (onProgress != null)
            {
                await onProgress(processedAccounts * 50 / accounts.Count, allWorks.Count);
            }
        }

        // Save all works to DB in batch
        var total = allWorks.Count;
        if (total == 0)
        {
            if (onProgress != null)
            {
                await onProgress(100, 0);
            }

            return;
        }

        var existingWorks = (await orm.GetAll()).ToDictionary(x => x.WorkId, StringComparer.OrdinalIgnoreCase);
        var toAdd = new List<DLsiteWorkDbModel>();
        var toUpdate = new List<DLsiteWorkDbModel>();
        var now = DateTime.Now;

        foreach (var work in allWorks.Values)
        {
            ct.ThrowIfCancellationRequested();

            if (existingWorks.TryGetValue(work.WorkId, out var existing))
            {
                existing.Title = work.Title;
                existing.Circle = work.Circle;
                existing.WorkType = work.WorkType;
                existing.CoverUrl = work.CoverUrl;
                existing.SalesDate = work.SalesDate;
                existing.PurchasedAt = work.PurchasedAt;
                existing.IsPurchased = work.IsPurchased;
                if (existing.Account != work.Account)
                {
                    existing.Account = work.Account;
                    existing.DrmKey = null;
                }
                existing.UpdatedAt = now;
                toUpdate.Add(existing);
            }
            else
            {
                work.CreatedAt = now;
                work.UpdatedAt = now;
                toAdd.Add(work);
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

        logger.LogInformation("DLsite sync complete: {Count} works synced ({Added} added, {Updated} updated)",
            total, toAdd.Count, toUpdate.Count);
    }

    public async Task<string> PrepareDownloadDirectory(string workId)
    {
        var work = await GetByWorkId(workId);
        if (work == null)
        {
            throw new Exception($"Work {workId} not found");
        }

        var defaultPath = DLsiteOptionsValue.DefaultPath;
        if (string.IsNullOrEmpty(defaultPath))
        {
            throw new Exception("Download path not configured. Please set the default download path in DLsite settings.");
        }

        var workDir = Path.Combine(defaultPath, workId);
        Directory.CreateDirectory(workDir);

        work.LocalPath = workDir;
        work.UpdatedAt = DateTime.Now;
        await orm.Update(work);

        return workDir;
    }

    private const int MaxLinkRefreshRetries = 3;

    public async Task DownloadWork(string workId, Func<int, string, Task>? onProgress = null, CancellationToken ct = default)
    {
        var work = await GetByWorkId(workId);
        if (work == null)
        {
            throw new Exception($"Work {workId} not found");
        }

        var (cookie, accountKey) = GetCookieForWork(work);

        var defaultPath = DLsiteOptionsValue.DefaultPath;
        if (string.IsNullOrEmpty(defaultPath))
        {
            throw new Exception("Download path not configured. Please set the default download path in DLsite settings.");
        }

        var workDir = Path.Combine(defaultPath, workId);
        Directory.CreateDirectory(workDir);

        if (onProgress != null)
        {
            await onProgress(0, $"[{workId}] 0%");
        }

        var downloadInfo = await ResolveDownloadWithRetry(cookie, accountKey, workId, ct);
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

        // Download all files with auto-retry on link expiration
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
            var downloaded = false;

            for (var retry = 0; retry <= MaxLinkRefreshRetries && !downloaded; retry++)
            {
                try
                {
                    await dlsiteClient.DownloadFileAsync(
                        cookie,
                        link.Url,
                        destPath,
                        accountKey,
                        (dl, total) =>
                        {
                            var fileProgress = total > 0 ? (int)(dl * 100 / total) : 0;
                            var overallProgress = (fileIndex * 100 + fileProgress) / links.Count;
                            onProgress?.Invoke(
                                Math.Min(overallProgress, 95),
                                $"{link.FileName} ({fileIndex + 1}/{links.Count}) {dl / 1024 / 1024}MB/{total / 1024 / 1024}MB");
                        },
                        ct);
                    downloaded = true;
                }
                catch (DLsiteDownloadLinkExpiredException) when (retry < MaxLinkRefreshRetries)
                {
                    logger.LogWarning("Download link expired for {WorkId} file {Index}, refreshing links (attempt {Retry})",
                        workId, i, retry + 1);

                    // Re-resolve to get fresh download URLs
                    downloadInfo = await ResolveDownloadWithRetry(cookie, accountKey, workId, ct);
                    links = downloadInfo.Links;

                    if (i < links.Count)
                    {
                        link = links[i];
                    }
                    else
                    {
                        throw new Exception($"Link refresh returned fewer links than expected for {workId}");
                    }
                }
            }

            downloadedFiles.Add(destPath);
            logger.LogInformation("Downloaded: {Path}", destPath);
        }

        // Extract archives
        if (onProgress != null)
        {
            await onProgress(96, "Extracting...");
        }

        await ExtractWork(workId, onProgress, ct);

        if (onProgress != null)
        {
            await onProgress(100, "100%");
        }

        logger.LogInformation("Download complete for work {WorkId} at {Path}", workId, (await GetByWorkId(workId))?.LocalPath);
    }

    public async Task ExtractWork(string workId, Func<int, string, Task>? onProgress = null, CancellationToken ct = default)
    {
        var work = await GetByWorkId(workId);
        if (work == null)
        {
            throw new Exception($"Work {workId} not found");
        }

        var defaultPath = DLsiteOptionsValue.DefaultPath;
        if (string.IsNullOrEmpty(defaultPath))
        {
            throw new Exception("Download path not configured.");
        }

        var workDir = Path.Combine(defaultPath, workId);
        if (!Directory.Exists(workDir))
        {
            throw new Exception($"Work directory not found: {workDir}");
        }

        // Find all archive files in the work directory (not in extracted subfolder)
        var downloadedFiles = Directory.EnumerateFiles(workDir)
            .Where(f => !f.EndsWith(".downloading", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (downloadedFiles.Count == 0)
        {
            throw new Exception($"No files found in work directory: {workDir}");
        }

        var contentDir = Path.Combine(workDir, "content");

        // Delete existing content folder if re-extracting
        if (Directory.Exists(contentDir))
        {
            Directory.Delete(contentDir, true);
        }

        var hasArchives = await archiveExtractor.ExtractAsync(downloadedFiles, contentDir, ct);

        // Delete archives after extraction if configured
        if (hasArchives && DLsiteOptionsValue.DeleteArchiveAfterExtraction)
        {
            foreach (var file in downloadedFiles)
            {
                try
                {
                    File.Delete(file);
                    logger.LogInformation("Deleted archive after extraction: {Path}", file);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to delete archive: {Path}", file);
                }
            }
        }

        // Update work record
        work.IsDownloaded = true;
        work.LocalPath = workDir;
        work.UpdatedAt = DateTime.Now;
        await orm.Update(work);

        logger.LogInformation("Extraction complete for work {WorkId} at {Path}", workId, work.LocalPath);
    }

    private async Task<DLsiteDownloadInfo> ResolveDownloadWithRetry(string cookie, string accountKey, string workId, CancellationToken ct)
    {
        try
        {
            return await dlsiteClient.ResolveDownloadAsync(cookie, workId, accountKey, ct);
        }
        catch (DLsiteAuthException)
        {
            throw; // Don't retry auth failures
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to resolve download links for {WorkId}, retrying once", workId);
            return await dlsiteClient.ResolveDownloadAsync(cookie, workId, accountKey, ct);
        }
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

        var (cookie, accountKey) = GetCookieForWork(work);

        var downloadInfo = await dlsiteClient.ResolveDownloadAsync(cookie, workId, accountKey, ct);

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
            // Launch executable with Locale Emulator if enabled for this work
            if (work.UseLocaleEmulator && localeEmulatorService.IsAvailableOnCurrentPlatform)
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
        var isExeOnly = extensions.SetEquals(ExecutableExtensions);

        var files = Directory.EnumerateFiles(localPath, "*.*", SearchOption.AllDirectories)
            .Where(f => extensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .ToList();

        if (isExeOnly)
        {
            // Filter out blacklisted executables
            var filtered = files
                .Where(f => !ExeBlacklist.Contains(Path.GetFileName(f)))
                .ToList();

            // Fall back to unfiltered list only if filtering removed everything
            if (filtered.Count > 0)
            {
                files = filtered;
            }
        }

        // Sort by priority: shallower depth first, then deprioritize known low-priority directories
        var normalizedRoot = NormalizeDirPath(localPath);
        files = files
            .OrderBy(f => IsInLowPriorityDir(f, normalizedRoot) ? 1 : 0)
            .ThenBy(f => GetRelativeDepth(f, normalizedRoot))
            .ThenBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return files;
    }

    private static string NormalizeDirPath(string path)
    {
        var full = Path.GetFullPath(path);
        return full.EndsWith(Path.DirectorySeparatorChar)
            ? full
            : full + Path.DirectorySeparatorChar;
    }

    private static int GetRelativeDepth(string filePath, string normalizedRoot)
    {
        var relativePath = Path.GetFullPath(filePath).Substring(normalizedRoot.Length);
        return relativePath.Count(c => c == Path.DirectorySeparatorChar);
    }

    private static bool IsInLowPriorityDir(string filePath, string normalizedRoot)
    {
        var relativePath = Path.GetFullPath(filePath).Substring(normalizedRoot.Length);
        var dirParts = Path.GetDirectoryName(relativePath)?
            .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        return dirParts != null && dirParts.Any(d => LowPriorityDirNames.Contains(d));
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

    /// <summary>
    /// Infers whether a work should be launched with Locale Emulator based on its work type.
    /// Currently always returns true for game/executable types.
    /// </summary>
    private static bool ShouldUseLocaleEmulator(string? workType)
    {
        return workType?.ToLowerInvariant() switch
        {
            "GCM" or "GAM" or "ACN" or "game" or "tool" => true,
            _ => true // Default to true for now; can be refined later
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

    public async Task DeleteLocalFiles(string workId)
    {
        var work = await GetByWorkId(workId);
        if (work == null)
        {
            throw new Exception($"Work {workId} not found");
        }

        if (!string.IsNullOrEmpty(work.LocalPath) && Directory.Exists(work.LocalPath))
        {
            Directory.Delete(work.LocalPath, true);
            logger.LogInformation("Deleted local files for work {WorkId} at {Path}", workId, work.LocalPath);
        }

        work.IsDownloaded = false;
        work.LocalPath = null;
        work.UpdatedAt = DateTime.Now;
        await orm.Update(work);
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

    public async Task SetUseLocaleEmulator(string workId, bool useLocaleEmulator)
    {
        var work = await GetByWorkId(workId);
        if (work == null)
        {
            throw new Exception($"Work {workId} not found");
        }

        work.UseLocaleEmulator = useLocaleEmulator;
        work.UpdatedAt = DateTime.Now;
        await orm.Update(work);
    }

    /// <summary>
    /// Converts a parsed DateTime from JST to the user's configured timezone.
    /// If the parsed value already has a specific Kind (Utc/Local), it is respected;
    /// otherwise it is assumed to be JST (Unspecified Kind from DLsite API).
    /// </summary>
    private static DateTime ConvertFromJst(DateTime parsed, TimeZoneInfo jst, TimeZoneInfo userTz)
    {
        if (parsed.Kind == DateTimeKind.Utc)
        {
            return TimeZoneInfo.ConvertTimeFromUtc(parsed, userTz);
        }

        // Treat Unspecified / Local as JST (DLsite is a Japanese service)
        var utc = TimeZoneInfo.ConvertTimeToUtc(parsed, jst);
        return TimeZoneInfo.ConvertTimeFromUtc(utc, userTz);
    }

}
