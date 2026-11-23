using Bakabase.Abstractions.Components.FileSystem;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.App;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Downloader.Abstractions;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Modules.Downloader.Models;

namespace Bakabase.Service.Components.CloudDownloads.WebProviders;

internal sealed record PublicShareFile(string Id, string Path, long Size, string? Version = null,
    string? Sha256 = null, string? Md5 = null);

internal sealed record PublicShareResolution(IReadOnlyList<PublicShareFile> Files,
    Func<PublicShareFile, bool, CancellationToken, Task<Uri>> OriginalUrl);

/// <summary>Resolves public shares without remote writes, then delegates transfers to the existing
/// HTTP engine. The durable manifest contains original file identity, never temporary URLs/tokens.
/// </summary>
public abstract class ResolvedShareCloudProvider : ICloudDownloadProvider, ICloudDownloadRecoveryProvider
{
    private readonly Func<string> _appData;
    private readonly IHttpDownloader _downloader;
    private readonly IHttpClientFactory _clients;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _operations = new();
    private static readonly JsonSerializerOptions Json = JsonSerializerOptions.Web;
    public abstract string ProviderId { get; }
    protected abstract string ClientName { get; }

    protected ResolvedShareCloudProvider(AppService app, IHttpDownloader downloader, IHttpClientFactory clients)
        : this(() => app.AppDataDirectory, downloader, clients) { }
    internal ResolvedShareCloudProvider(Func<string> appData, IHttpDownloader downloader, IHttpClientFactory clients)
    { _appData = appData; _downloader = downloader; _clients = clients; }

    public Task<CloudReadiness> CheckReadyAsync(string? accountId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(string.IsNullOrWhiteSpace(accountId) ? new CloudReadiness(true) :
            new CloudReadiness(false, "This adapter accepts public shares only; leave the account empty."));
    }

    public virtual bool CanRevalidateDownloadFailure(string exactCode) => false;

    // Opt-in for concrete adapters that only read from the provider. Source revision separately
    // checks the stopped task, operation ownership and the entire local output directory.
    protected async Task<CloudReadiness> CheckNoRetainedOperationStateAsync(CloudDownloadRequest request, CancellationToken ct)
    {
        var review = new CloudReadiness(false, "SOURCE_REVISION_PROVIDER_REVIEW_REQUIRED");
        ct.ThrowIfCancellationRequested();
        if (request.Source.ProviderId != ProviderId || request.Source.Kind != "share" || request.RemoteEntry != null ||
            !string.IsNullOrWhiteSpace(request.AccountId) || !string.IsNullOrWhiteSpace(request.Source.AccountId) ||
            string.IsNullOrWhiteSpace(request.OperationId) || !Path.IsPathFullyQualified(request.OutputDirectory)) return review;

        var key = Hash(request.OperationId);
        var gate = _operations.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        if (!await gate.WaitAsync(0, ct)) return review;
        try
        {
            ValidateSource(request.Source);
            var root = ArtifactManifestFiles.Absolute(_appData());
            ArtifactManifestFiles.RejectLinks(root);
            if (!File.GetAttributes(root).HasFlag(FileAttributes.Directory)) return review;
            var state = root;
            foreach (var segment in StateDirectorySegments(key))
            {
                ct.ThrowIfCancellationRequested();
                state = Path.Combine(state, segment);
                ArtifactManifestFiles.RejectLinks(state);
                try
                {
                    if (!File.GetAttributes(state).HasFlag(FileAttributes.Directory)) return review;
                }
                catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
                {
                    return ArtifactManifestFiles.IsMissingFileConfirmed(root, state, ct) ? new(true) : review;
                }
            }

            // A failed discovery can leave an empty lock file. A manifest, receipt, temporary
            // journal or other entry requires review even when the output directory is empty.
            foreach (var entry in Directory.EnumerateFileSystemEntries(state))
            {
                ct.ThrowIfCancellationRequested();
                if (Path.GetFileName(entry) != "operation.lock") return review;
                ArtifactManifestFiles.RejectLinks(entry);
                var file = new FileInfo(entry);
                if ((file.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint | FileAttributes.Device)) != 0 ||
                    file.Length != 0) return review;
            }
            return new(true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException)
        { return review; }
        finally { gate.Release(); }
    }

    /// <summary>Recheck an immutable stopped operation without creating directories, receipts,
    /// download tasks, or opening payload URLs. Listing visibility alone is not download readiness.</summary>
    public async Task<CloudReadiness> RevalidateDownloadAsync(CloudDownloadRequest request, CancellationToken ct)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(request.AccountId) || request.RemoteEntry != null ||
                request.Source.ProviderId != ProviderId || request.Source.Kind != "share" ||
                string.IsNullOrWhiteSpace(request.OperationId) || !Path.IsPathFullyQualified(request.OutputDirectory))
                return new(false, "The stopped operation is not an anonymous public-share download.");
            ValidateSource(request.Source);
            var output = Path.GetFullPath(request.OutputDirectory);
            NoLinks(output);
            using var client = _clients.CreateClient(ClientName);
            var resolution = await ResolveAsync(request.Source, client, ct);
            var files = ValidateManifest(resolution.Files);
            var manifestPath = Path.Combine([_appData(), .. StateDirectorySegments(Hash(request.OperationId)), "manifest.json"]);
            NoLinks(manifestPath);
            if (File.Exists(manifestPath))
            {
                var previous = JsonSerializer.Deserialize<SavedManifest>(await File.ReadAllTextAsync(manifestPath, ct), Json);
                var identity = ManifestIdentity(request.Source, output);
                if (previous?.Identity != identity || JsonSerializer.Serialize(previous.Files, Json) != JsonSerializer.Serialize(files, Json))
                    return new(false, "The original share manifest or destination changed. Review the source before starting a new operation.");
            }
            // Bound an interactive diagnosis independently of the regular recursive download.
            // Every file must have an original URL; one available preview is not enough.
            if (files.Count > 1000) return new(false, "This share is too large for a complete interactive download check. Review it manually.");
            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                var uri = await resolution.OriginalUrl(file, false, ct);
                if (!PublicShareDownloadClients.IsProviderHost(ProviderId, uri))
                    return new(false, "The original file uses an unsupported download host. Download it manually.");
            }
            return new(true);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is CloudProviderException or InvalidDataException or ArgumentException or IOException or HttpRequestException or OperationCanceledException or JsonException or TimeoutException)
        { return new(false, error.Message); }
    }

    internal abstract void ValidateSource(CloudSource source);
    internal abstract Task<PublicShareResolution> ResolveAsync(CloudSource source, HttpClient client, CancellationToken ct);

    public async Task<CloudDownloadResult> DownloadAsync(CloudDownloadRequest request,
        Func<CloudDownloadProgress, Task> progress, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(request.AccountId) || request.RemoteEntry != null)
            throw new NotSupportedException("This adapter accepts public shares without an account or transfer receipt.");
        if (string.IsNullOrWhiteSpace(request.OperationId) || !Path.IsPathFullyQualified(request.OutputDirectory))
            throw new ArgumentException("An operation ID and absolute output directory are required.");
        ValidateSource(request.Source);
        var key = Hash(request.OperationId);
        var gate = _operations.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var state = PrivateDirectory(_appData(), StateDirectorySegments(key));
            await using var lease = await LeaseAsync(Path.Combine(state, "operation.lock"), ct);
            var output = Path.GetFullPath(request.OutputDirectory);
            NoLinks(output);
            Directory.CreateDirectory(output);
            using var client = _clients.CreateClient(ClientName);
            await progress(new(null, Stage: "Discovering original files"));
            var resolution = await ResolveAsync(request.Source, client, ct);
            var files = ValidateManifest(resolution.Files);
            var manifestPath = Path.Combine(state, "manifest.json");
            var identity = ManifestIdentity(request.Source, output);
            var manifest = new SavedManifest(identity, files);
            if (File.Exists(manifestPath))
            {
                NoLinks(manifestPath);
                var previous = JsonSerializer.Deserialize<SavedManifest>(await File.ReadAllTextAsync(manifestPath, ct), Json);
                if (previous?.Identity != identity || JsonSerializer.Serialize(previous.Files, Json) != JsonSerializer.Serialize(files, Json))
                    throw new CloudProviderException("The original share manifest or destination changed. Start a new download operation.");
            }
            else await AtomicJsonAsync(manifestPath, manifest, ct);
            var receiptsPath = Path.Combine(state, "completed.json");
            NoLinks(receiptsPath);
            var receipts = File.Exists(receiptsPath)
                ? JsonSerializer.Deserialize<Dictionary<string, FileReceipt>>(await File.ReadAllTextAsync(receiptsPath, ct), Json)
                    ?? throw new InvalidDataException("The public-share completion receipt is invalid.")
                : new Dictionary<string, FileReceipt>(StringComparer.Ordinal);
            var stagingName = ".bakabase-share-" + key;
            if (files.Any(f => f.Path.Split('/')[0].Equals(stagingName, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("The share conflicts with its private download directory.");
            var staging = PrivateDirectory(output, stagingName);
            long total;
            try { total = files.Aggregate(0L, (sum, file) => checked(sum + file.Size)); }
            catch (OverflowException) { throw new InvalidDataException("The public share is too large."); }
            long completedBytes = 0;
            var completed = new List<string>();
            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                var destination = Destination(output, file.Path);
                receipts.TryGetValue(file.Path, out var receipt);
                if (File.Exists(destination))
                {
                    if (receipt == null || !await MatchesReceipt(destination, file, receipt, ct))
                        throw new CloudProviderException("A destination file already exists and is not a verified result of this operation.");
                }
                else
                {
                    if (Directory.Exists(destination)) throw new CloudProviderException("A destination file conflicts with an existing directory.");
                    var name = Hash(file.Path) + ".payload";
                    var part = Path.Combine(staging, name);
                    NoLinks(part);
                    var stagedComplete = receipt != null && File.Exists(part) && await MatchesReceipt(part, file, receipt, ct);
                    if (!stagedComplete)
                    {
                        if (File.Exists(part)) File.Delete(part); // This operation's private staging file only.
                        for (var attempt = 0; ; attempt++)
                        {
                            try
                            {
                                var uri = await resolution.OriginalUrl(file, attempt != 0, ct);
                                if (!PublicShareDownloadClients.IsProviderHost(ProviderId, uri))
                                    throw new CloudProviderException("The original file uses an unsupported download host. Download it manually.");
                                var downloaded = await _downloader.DownloadWithProgressAsync(new(uri.AbsoluteUri, staging)
                                {
                                    FileName = name, HttpClientName = ClientName, AllowOverwrite = false,
                                    ParallelConnections = 4, MaxRetries = 2
                                }, update => progress(new(total == 0 ? 0 : Math.Min(100, 100d *
                                        (completedBytes + Math.Min(file.Size, update.CompletedBytes)) / total),
                                    completedBytes + Math.Min(file.Size, update.CompletedBytes), total, "Downloading original files")), ct);
                                if (Path.GetFullPath(downloaded) != part) throw new InvalidDataException("The HTTP engine returned an unexpected file.");
                                break;
                            }
                            catch (Exception error) when (attempt == 0 && !ct.IsCancellationRequested && LocalStorageFailures.Code(error) == null &&
                                error is HttpRequestException or IOException or TimeoutException)
                            {
                                // Resolve a fresh original-file URL; never retry a video derivative,
                                // or silently keep bytes from the expired representation.
                                if (File.Exists(part)) File.Delete(part);
                            }
                        }
                        await ValidateOriginal(part, file, ct);
                        receipt = new(file.Size, await FileSha256(part, ct));
                        receipts[file.Path] = receipt;
                        await AtomicJsonAsync(receiptsPath, receipts, ct);
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    Destination(output, file.Path);
                    File.Move(part, destination, overwrite: false);
                }
                completed.Add(destination);
                completedBytes += file.Size;
                await progress(new(total == 0 ? 100d * completed.Count / files.Count : 100d * completedBytes / total,
                    completedBytes, total, "Downloading original files"));
            }
            if (!Directory.EnumerateFileSystemEntries(staging).Any()) Directory.Delete(staging);
            CloudDownloadManifest.Validate(output, completed);
            return new(completed);
        }
        finally { gate.Release(); }
    }

    internal static IReadOnlyList<PublicShareFile> ValidateManifest(IReadOnlyList<PublicShareFile> files)
    {
        if (files.Count is 0 or > 100000) throw new InvalidDataException("The public share has no downloadable files or exceeds the file limit; empty folders are not supported.");
        var sorted = files.OrderBy(f => f.Path, StringComparer.Ordinal).ToArray();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in sorted)
        {
            if (string.IsNullOrWhiteSpace(file.Id) || file.Id.Length > 2048 || file.Size < 0 || !ids.Add(file.Id) ||
                file.Path != string.Join('/', file.Path.Split('/').Select(SafeSegment)) || !paths.Add(file.Path))
                throw new InvalidDataException("The public share contains duplicate or invalid original file identities or paths.");
            if (file.Sha256 != null && !IsHex(file.Sha256, 64) || file.Md5 != null && !IsHex(file.Md5, 32))
                throw new InvalidDataException("The public share contains an invalid checksum.");
        }
        foreach (var path in paths)
        {
            var segments = path.Split('/');
            if (Enumerable.Range(1, segments.Length - 1).Any(i => paths.Contains(string.Join('/', segments.Take(i)))))
                throw new InvalidDataException("The public share contains conflicting file and directory paths.");
        }
        return sorted;
    }

    internal static string SafeSegment(string value)
    {
        var reserved = value.Split('.')[0].ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(value) || value is "." or ".." || value.Length > 255 ||
            value.Any(c => c < 32 || "/\\\0<>:\"|?*".Contains(c)) || value.EndsWith('.') || value.EndsWith(' ') ||
            reserved is "CON" or "PRN" or "AUX" or "NUL" ||
            reserved.Length == 4 && (reserved.StartsWith("COM") || reserved.StartsWith("LPT")) && reserved[3] is >= '1' and <= '9')
            throw new InvalidDataException("The public share contains an unsafe file or folder name.");
        return value;
    }

    internal static void SameOriginal(PublicShareFile expected, PublicShareFile actual)
    {
        if (expected != actual) throw new CloudProviderException("The original remote file changed while downloading. Start a new download operation.");
    }

    internal static async Task<JsonDocument> GetJson(HttpClient client, Uri uri, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        return await JsonResponse(client, request, ct);
    }

    internal static async Task<JsonDocument> JsonResponse(HttpClient client, HttpRequestMessage request, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromMinutes(2));
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        if (!response.IsSuccessStatusCode) throw new CloudProviderException($"The public-share API returned HTTP {(int)response.StatusCode}; it may require access, login, or a later retry.");
        var bytes = await ReadBounded(response.Content, 32 * 1024 * 1024, deadline.Token);
        try { return JsonDocument.Parse(bytes); }
        catch (JsonException) { throw new CloudProviderException("The public-share API did not return a supported file listing; login or manual download may be required."); }
    }

    internal static async Task<byte[]> ReadBounded(HttpContent content, int maximum, CancellationToken ct)
    {
        if (content.Headers.ContentLength > maximum) throw new InvalidDataException("The public-share response exceeds its safety limit.");
        await using var input = await content.ReadAsStreamAsync(ct);
        using var output = new MemoryStream();
        var buffer = new byte[65536];
        while (await input.ReadAsync(buffer, ct) is var count && count > 0)
        {
            if (output.Length + count > maximum) throw new InvalidDataException("The public-share response exceeds its safety limit.");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }

    internal static string RequiredString(JsonElement data, string key) => data.TryGetProperty(key, out var value) &&
        value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()) && value.GetString()!.Length <= 8192
        ? value.GetString()! : throw new InvalidDataException("The public-share listing is missing required metadata.");
    internal static string? Text(JsonElement data, string key) => data.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    internal static long Size(JsonElement data, string key)
    {
        if (data.TryGetProperty(key, out var size) &&
            (size.ValueKind == JsonValueKind.Number && size.TryGetInt64(out var number) && number >= 0 ||
             size.ValueKind == JsonValueKind.String && long.TryParse(size.GetString(), out number) && number >= 0)) return number;
        throw new InvalidDataException("The original file size is missing or invalid.");
    }

    private static bool IsHex(string value, int length) => value.Length == length && value.All(Uri.IsHexDigit);
    private string ManifestIdentity(CloudSource source, string output) => Hash(JsonSerializer.Serialize(new[]
        {ProviderId, source.Url, Hash(source.AccessCode ?? ""), output}));
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private string[] StateDirectorySegments(string key) => ["cloud-downloads", "resolved", ProviderId, key];
    private static string Destination(string root, string path)
    {
        var result = Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));
        if (!Path.GetFullPath(result).StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidDataException("The public-share file escapes its destination.");
        NoLinks(result);
        return result;
    }
    private static void NoLinks(string path)
    {
        for (FileSystemInfo? current = new FileInfo(path); current != null;
             current = current is FileInfo file ? file.Directory : ((DirectoryInfo)current).Parent)
            if (current.LinkTarget != null) throw new InvalidDataException("Symbolic links are not supported in public-share state or destinations.");
    }
    private static string PrivateDirectory(string root, params string[] parts)
    {
        NoLinks(root);
        foreach (var part in parts)
        {
            root = Path.Combine(root, part); NoLinks(root); Directory.CreateDirectory(root);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        return root;
    }
    private static async Task<FileStream> LeaseAsync(string file, CancellationToken ct)
    {
        NoLinks(file);
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try { return new FileStream(file, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { await Task.Delay(100, ct); }
        }
    }
    private static async Task AtomicJsonAsync<T>(string path, T value, CancellationToken ct)
    {
        NoLinks(path);
        var staged = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(staged, JsonSerializer.Serialize(value, Json), ct);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(staged, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.Move(staged, path, true);
        }
        finally { if (File.Exists(staged)) File.Delete(staged); }
    }
    private static async Task<string> FileSha256(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).ToLowerInvariant();
    }
    private static async Task ValidateOriginal(string path, PublicShareFile file, CancellationToken ct)
    {
        NoLinks(path);
        if (!File.Exists(path) || new FileInfo(path).Length != file.Size) throw new InvalidDataException("The original file download is incomplete.");
        if (file.Sha256 != null && !file.Sha256.Equals(await FileSha256(path, ct), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The original file checksum did not match.");
        if (file.Md5 != null)
        {
            await using var stream = File.OpenRead(path);
            if (!file.Md5.Equals(Convert.ToHexString(await MD5.HashDataAsync(stream, ct)), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The original file checksum did not match.");
        }
    }
    private static async Task<bool> MatchesReceipt(string path, PublicShareFile file, FileReceipt receipt, CancellationToken ct) =>
        receipt.Size == file.Size && new FileInfo(path).Length == receipt.Size &&
        receipt.Sha256.Equals(await FileSha256(path, ct), StringComparison.OrdinalIgnoreCase);
    private sealed record SavedManifest(string Identity, IReadOnlyList<PublicShareFile> Files);
    private sealed record FileReceipt(long Size, string Sha256);
}
