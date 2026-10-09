using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Bakabase.Service.Models.View;

namespace Bakabase.Service.Services;

/// <summary>
/// Reads deployment metadata, never the host filesystem or Docker socket. Missing or malformed
/// metadata means unknown, not a guessed host path. This is not a path used for I/O or authorization.
/// </summary>
public sealed class DeploymentPathDisplay
{
    public const string EnvironmentVariable = "BAKABASE_DEPLOYMENT_MOUNTS";
    private const int MaxMetadataLength = 64 * 1024;
    private readonly bool _isContainer;
    private readonly Manifest? _manifest;

    public DeploymentPathDisplay() : this(
        string.Equals(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"), "true", StringComparison.OrdinalIgnoreCase),
        Environment.GetEnvironmentVariable(EnvironmentVariable)) { }

    internal DeploymentPathDisplay(bool isContainer, string? metadata)
    {
        _isContainer = isContainer;
        if (!isContainer || string.IsNullOrWhiteSpace(metadata) || metadata.Length > MaxMetadataLength) return;
        try
        {
            var manifest = JsonSerializer.Deserialize<Manifest>(metadata, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (manifest?.SchemaVersion != 1 || manifest.Mounts == null || manifest.Mounts.Length > 256) return;
            var targets = new HashSet<string>(StringComparer.Ordinal);
            foreach (var mount in manifest.Mounts)
            {
                if (mount == null || mount.Type is not ("bind" or "volume" or "tmpfs") || Normalize(mount.Target) is not { } target ||
                    !targets.Add(target) || mount.Type == "bind" && !IsHostAbsolute(mount.Source)) return;
                mount.Target = target;
            }
            manifest.Mounts = manifest.Mounts.OrderByDescending(m => m.Target!.Length).ToArray();
            _manifest = manifest;
        }
        catch (JsonException) { /* Plain docker compose and older/custom deployment tools need no metadata. */ }
    }

    public DeploymentPathsViewModel Describe(IEnumerable<string?> paths) => new()
    {
        IsContainer = _isContainer,
        Paths = paths.Where(path => !string.IsNullOrEmpty(path)).Distinct(StringComparer.Ordinal)
            .Select(path => Describe(path!)).ToArray()
    };

    internal DeploymentPathViewModel Describe(string path)
    {
        if (!_isContainer) return new() { ServerPath = path, StorageKind = "local" };
        if (_manifest == null || Normalize(path) is not { } normalized)
            return new() { ServerPath = path, StorageKind = "unknown" };
        var mount = _manifest.Mounts!.FirstOrDefault(m => m.Target == normalized || m.Target == "/" ||
            normalized.StartsWith(m.Target + "/", StringComparison.Ordinal));
        if (mount == null) return new() { ServerPath = path, StorageKind = "container", ReadOnly = _manifest.ReadOnlyRoot };
        return new()
        {
            ServerPath = path,
            HostPath = mount.Type == "bind" ? JoinHost(mount.Source!, normalized[mount.Target!.Length..].TrimStart('/')) : null,
            StorageKind = mount.Type == "tmpfs" ? "container" : mount.Type!,
            ReadOnly = mount.ReadOnly
        };
    }

    private static string JoinHost(string root, string relative)
    {
        if (relative.Length == 0) return root;
        var windows = root.StartsWith("\\\\", StringComparison.Ordinal) || root.Length > 2 && root[1] == ':';
        var separator = windows ? '\\' : '/';
        if (windows) root = root.Replace('/', '\\');
        return root.TrimEnd(separator) + separator + (windows ? relative.Replace('/', '\\') : relative);
    }

    private static bool IsHostAbsolute(string? path) => path is { Length: > 0 and <= 4096 } &&
        !path.Any(char.IsControl) && (path.StartsWith('/') || path.StartsWith("\\\\", StringComparison.Ordinal) ||
            path.Length > 2 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] is '/' or '\\');

    // POSIX mount destinations are normalized independently of the computer running the tests.
    private static string? Normalize(string? path)
    {
        if (path is not { Length: > 0 and <= 4096 } || !path.StartsWith('/') || path.Any(char.IsControl)) return null;
        var parts = new List<string>();
        foreach (var part in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".") continue;
            if (part == "..") { if (parts.Count > 0) parts.RemoveAt(parts.Count - 1); }
            else parts.Add(part);
        }
        return "/" + string.Join('/', parts);
    }

    private sealed class Manifest
    {
        public int SchemaVersion { get; set; }
        public bool ReadOnlyRoot { get; set; }
        public Mount[]? Mounts { get; set; }
    }

    private sealed class Mount
    {
        public string? Type { get; set; }
        public string? Source { get; set; }
        public string? Target { get; set; }
        public bool ReadOnly { get; set; }
    }
}
