using System;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Abstractions.Components.Network;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.InsideWorld.Business.Components.Dependency.Abstractions;
using Bakabase.InsideWorld.Business.Components.Dependency.Discovery;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.CloudTools;
using Bakabase.InsideWorld.Business.Components.Dependency.Models;
using Bakabase.InsideWorld.Models.Constants;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Components.Dependency.Implementations.BaiduPcsGo;

/// <summary>
/// Builds the reviewed JSON bridge using the exact upstream source and its go.sum.
/// No fictitious binary release or globally installed compiler is required.
/// Installation is optional and explicit; all compiler caches stay under AppData.
/// </summary>
public sealed class BaiduPcsGoService(ILoggerFactory loggerFactory, AppService appService,
    IHttpClientFactory clients, IServiceProvider services)
    : DependentComponentService(loggerFactory, appService, "baidu-pcs-go", services)
{
    public const string BridgeVersion = "4.0.2+bakabase.29";
    public const string UpstreamCommit = "225bdd3b6cb298601c4d5ef7104c3e08cd1d692d";
    private const string SourceSha256 = "6da5fbee22d3747a8dbc9e78d833f775de839c1ed77f6526642c79f4f6d189cf";
    private const string UpstreamPatchName = "upstream-patch.json";
    public override string Id => "baidu-pcs-go-component-service";
    protected override string KeyInLocalizer => "BaiduPcsGo";
    public override bool IsRequired => false;
    public override System.Collections.Generic.IReadOnlyList<DependentComponentTarget> SupportedTargets => DependentComponentPlatforms.BaiduPcsGo;
    protected override IDiscoverer Discoverer { get; } = new BaiduPcsGoDiscoverer(loggerFactory);
    public string Executable => Path.Combine(Context.Location ?? DefaultLocation,
        OperatingSystem.IsWindows() ? "bakabase-baidu-pcs.exe" : "bakabase-baidu-pcs");

    public override async Task<DependentComponentVersion> GetLatestVersion(CancellationToken ct)
    {
        using var client = clients.CreateClient(InternalOptions.HttpClientNames.Default);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Bakabase/1.0");
        using var latest = JsonDocument.Parse(await client.GetStringAsync(
            "https://api.github.com/repos/qjfoidnh/BaiduPCS-Go/releases/latest", ct));
        var tag = latest.RootElement.GetProperty("tag_name").GetString();
        return new DependentComponentVersion
        {
            Version = BridgeVersion,
            Description = $"BaiduPCS-Go v4.0.2 / Bakabase JSON bridge {BridgeVersion}. Upstream latest: {tag}. " +
                          "Installation downloads a verified Go compiler and builds the pinned source locally."
        };
    }

    protected override async Task InstallCore(CancellationToken ct)
    {
        var build = Path.Combine(TempDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(build);
        try
        {
            using var client = clients.CreateClient(InternalOptions.HttpClientNames.Default);
            client.Timeout = TimeSpan.FromMinutes(15);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Bakabase/1.0");
            var archive = Path.Combine(build, "source.zip");
            await DownloadVerified(client,
                $"https://codeload.github.com/qjfoidnh/BaiduPCS-Go/zip/{UpstreamCommit}", archive, SourceSha256, ct);
            var sourceParent = Path.Combine(build, "source");
            ZipFile.ExtractToDirectory(archive, sourceParent);
            var source = Directory.GetDirectories(sourceParent).Single();
            var patchManifest = Path.Combine(build, UpstreamPatchName);
            await using (var resource = typeof(BaiduPcsGoService).Assembly
                             .GetManifestResourceStream($"Bakabase.BaiduPcsBridge.{UpstreamPatchName}")!)
            await using (var output = File.Create(patchManifest))
                await resource.CopyToAsync(output, ct);
            await using (var patch = File.OpenRead(patchManifest))
                await ApplyUpstreamPatchAsync(source, patch, ct);
            var cmd = Path.Combine(source, "cmd", "bakabase-bridge");
            Directory.CreateDirectory(cmd);
            const string bridgeSourcePrefix = "Bakabase.BaiduPcsBridge.";
            var assembly = typeof(BaiduPcsGoService).Assembly;
            var bridgeSources = assembly.GetManifestResourceNames()
                .Where(name => name.StartsWith(bridgeSourcePrefix, StringComparison.Ordinal) && name.EndsWith(".go", StringComparison.Ordinal))
                .Select(name => name[bridgeSourcePrefix.Length..]).ToArray();
            foreach (var name in bridgeSources)
            {
                await using var resource = assembly.GetManifestResourceStream(bridgeSourcePrefix + name)!;
                await using var output = File.Create(Path.Combine(cmd, name));
                await resource.CopyToAsync(output, ct);
            }
            await UpdateContext(c => c.InstallationProgress = 15);

            var os = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "darwin" : "linux";
            var arch = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "amd64";
            using var manifest = JsonDocument.Parse(await client.GetStringAsync("https://go.dev/dl/?mode=json", ct));
            var compiler = manifest.RootElement.EnumerateArray().Where(v => v.GetProperty("stable").GetBoolean())
                .SelectMany(v => v.GetProperty("files").EnumerateArray())
                .First(f => f.GetProperty("os").GetString() == os && f.GetProperty("arch").GetString() == arch &&
                            f.GetProperty("kind").GetString() == "archive");
            var compilerFile = compiler.GetProperty("filename").GetString()!;
            var compilerArchive = Path.Combine(build, compilerFile);
            await DownloadVerified(client, $"https://go.dev/dl/{compilerFile}", compilerArchive,
                compiler.GetProperty("sha256").GetString()!, ct);
            if (compilerFile.EndsWith(".zip")) ZipFile.ExtractToDirectory(compilerArchive, build);
            else
            {
                await using var compressed = File.OpenRead(compilerArchive);
                await using var tar = new GZipStream(compressed, CompressionMode.Decompress);
                await ExtractTar(tar, build, ct);
            }
            await UpdateContext(c => c.InstallationProgress = 50);
            var go = Path.Combine(build, "go", "bin", OperatingSystem.IsWindows() ? "go.exe" : "go");
            var binary = Path.Combine(build, Path.GetFileName(Executable));
            var start = new ProcessStartInfo(go) { WorkingDirectory = source, UseShellExecute = false,
                RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            foreach (var arg in new[] { "build", "-trimpath", "-mod=readonly", "-o", binary, "./cmd/bakabase-bridge" })
                start.ArgumentList.Add(arg);
            start.Environment["GOCACHE"] = Path.Combine(build, "go-cache");
            start.Environment["GOMODCACHE"] = Path.Combine(build, "go-modules");
            start.Environment["GOPATH"] = Path.Combine(build, "go-path");
            start.Environment["GOTOOLCHAIN"] = "local";
            start.Environment["CGO_ENABLED"] = "0";
            start.Environment["TMPDIR"] = start.Environment["TEMP"] = start.Environment["TMP"] = build;
            CloudToolProxy.ApplyToEnvironment(start, services.GetRequiredService<BakabaseWebProxy>(),
                new Uri("https://proxy.golang.org"));
            using var process = Process.Start(start) ?? throw new IOException("Could not start the bridge compiler.");
            using var cancel = ct.Register(() => { try { process.Kill(true); } catch { /* exited */ } });
            var stdout = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);
            try { await process.WaitForExitAsync(ct); }
            finally
            {
                if (!process.HasExited) { try { process.Kill(true); } catch { } }
                await process.WaitForExitAsync(CancellationToken.None);
            }
            await stdout;
            await stderr;
            if (process.ExitCode != 0) throw new IOException("BaiduPCS-Go bridge compilation failed. Check the network/proxy connection to the Go module registry.");
            if (await new BaiduPcsGoDiscoverer(loggerFactory).TryDiscoverAt(build, ct) == null)
                throw new IOException("The compiled BaiduPCS-Go bridge did not pass its protocol check.");
            Directory.CreateDirectory(DefaultLocation);
            var sources = Path.Combine(DefaultLocation, "source");
            Directory.CreateDirectory(sources);
            // Keep the exact corresponding upstream archive and bridge source beside the
            // optional executable, including in offline/container installations.
            File.Copy(archive, Path.Combine(sources, $"BaiduPCS-Go-{UpstreamCommit}.zip"), true);
            foreach (var name in bridgeSources) File.Copy(Path.Combine(cmd, name), Path.Combine(sources, name), true);
            File.Copy(patchManifest, Path.Combine(sources, UpstreamPatchName), true);
            File.Copy(Path.Combine(source, "LICENSE"), Path.Combine(DefaultLocation, "LICENSE.BaiduPCS-Go"), true);
            await File.WriteAllTextAsync(Path.Combine(sources, "BUILD.txt"),
                $"BaiduPCS-Go {UpstreamCommit}, Bakabase bridge {BridgeVersion}\n" +
                $"Compiler: {compiler.GetProperty("version").GetString()}\n" +
                $"Extract the source ZIP. For each file in {UpstreamPatchName}, verify its original SHA-256 and apply " +
                "each before/after edit in order exactly once using UTF-8. Copy the supplied Go source files into cmd/bakabase-bridge/, then run:\n" +
                "CGO_ENABLED=0 go build -trimpath -mod=readonly -o bakabase-baidu-pcs ./cmd/bakabase-bridge\n" +
                "The upstream Apache-2.0 license is retained in LICENSE.BaiduPCS-Go.\n", ct);
            // Move replaces the executable only after download, build and protocol checks succeed.
            File.Move(binary, Path.Combine(DefaultLocation, Path.GetFileName(binary)), true);
            await File.WriteAllTextAsync(Path.Combine(DefaultLocation, "source.txt"),
                $"https://github.com/qjfoidnh/BaiduPCS-Go/tree/{UpstreamCommit}\nBakabase bridge source: src/tools/BaiduPcsBridge\n", ct);
        }
        finally
        {
            try { Directory.Delete(build, true); }
            catch (Exception e) { Logger.LogWarning(e, "Could not remove BaiduPCS-Go build cache"); }
        }
    }

    internal static async Task ApplyUpstreamPatchAsync(string directory, Stream manifest, CancellationToken ct)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)) + Path.DirectorySeparatorChar;
        using var edits = await JsonDocument.ParseAsync(manifest, cancellationToken: ct);
        foreach (var patch in edits.RootElement.EnumerateArray())
        {
            var file = Path.GetFullPath(Path.Combine(root, patch.GetProperty("path").GetString()!));
            if (!file.StartsWith(root, StringComparison.Ordinal)) throw new IOException("Unsafe upstream patch path.");
            var bytes = await File.ReadAllBytesAsync(file, ct);
            var expected = patch.GetProperty("sha256").GetString();
            if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(expected, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Upstream patch input checksum mismatch.");
            var text = Encoding.UTF8.GetString(bytes);
            foreach (var edit in patch.GetProperty("edits").EnumerateArray())
            {
                var before = edit.GetProperty("before").GetString()!;
                var after = edit.GetProperty("after").GetString()!;
                var start = text.IndexOf(before, StringComparison.Ordinal);
                if (before.Length == 0 || start < 0 ||
                    text.IndexOf(before, start + before.Length, StringComparison.Ordinal) >= 0)
                    throw new IOException("Upstream patch input is ambiguous.");
                text = string.Concat(text.AsSpan(0, start), after, text.AsSpan(start + before.Length));
            }
            await File.WriteAllTextAsync(file, text, ct);
        }
    }

    private static async Task DownloadVerified(HttpClient client, string url, string file, string sha256, CancellationToken ct)
    {
        await using (var input = await client.GetStreamAsync(url, ct))
        await using (var output = File.Create(file)) await input.CopyToAsync(output, ct);
        await using var downloaded = File.OpenRead(file);
        var digest = Convert.ToHexString(await SHA256.HashDataAsync(downloaded, ct));
        if (!digest.Equals(sha256, StringComparison.OrdinalIgnoreCase)) throw new IOException("Dependency download checksum mismatch.");
    }

    private static async Task ExtractTar(Stream input, string target, CancellationToken ct)
    {
        var root = Path.GetFullPath(target) + Path.DirectorySeparatorChar;
        using var reader = new TarReader(input, leaveOpen: true);
        while (await reader.GetNextEntryAsync(cancellationToken: ct) is { } entry)
        {
            var file = Path.GetFullPath(Path.Combine(root, entry.Name));
            if (!file.StartsWith(root, StringComparison.Ordinal)) throw new IOException("Unsafe compiler archive path.");
            if (entry.EntryType == TarEntryType.Directory) Directory.CreateDirectory(file);
            else if (entry.EntryType is TarEntryType.RegularFile or TarEntryType.V7RegularFile)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                await using (var output = File.Create(file))
                    if (entry.DataStream != null) await entry.DataStream.CopyToAsync(output, ct);
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(file, entry.Mode & (UnixFileMode)0x1ff);
            }
            else throw new IOException("Unsupported compiler archive entry.");
        }
    }
}
