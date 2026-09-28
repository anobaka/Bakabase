using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.ExHentai;

/// <summary>
/// Assigns a gallery directory according to what is currently on disk. The marker travels with
/// the directory when a user moves it; no historical reservation keeps a missing name occupied.
/// </summary>
public static class ExHentaiGalleryDirectoryClaim
{
    public const string MarkerFileName = ".bakabase-exhentai-gallery.json";

    private const int MarkerVersion = 1;
    private const string HintDirectoryName = ".bakabase-exhentai-gallery-index";
    private static readonly JsonSerializerOptions JsonOptions = JsonSerializerOptions.Web;
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    /// <summary>
    /// Reuses an existing directory marked for this gallery, or claims the requested name. A
    /// directory with someone else's marker (or unmarked contents) is never used for this gallery.
    /// </summary>
    public static string Claim(string downloadRoot, string relativeGalleryDirectory, string sourceKey)
    {
        var identity = ParseIdentity(sourceKey);
        var requested = ValidateDirectory(downloadRoot, relativeGalleryDirectory, out var root);
        var parent = Path.GetDirectoryName(requested)!;
        var prettyName = Path.GetFileName(requested);

        // Callers prepare the configured root. Allocation itself must not create that root
        // if it disappears between preparation and claiming a gallery directory.
        RequireAccessibleRoot(root);
        EnsureNoLinksBelowRoot(root, parent);
        var hintDirectory = Path.Combine(root, HintDirectoryName);
        Directory.CreateDirectory(hintDirectory);
        // Different galleries can contend for the same pretty name. Lock the allocation for
        // this root, not just one source's hint: a no-overwrite move alone is not a reliable
        // cross-thread claim on every filesystem.
        using var allocationLock = AcquireAllocationLock(Path.Combine(hintDirectory, ".allocation.lock"));
        var hintPath = Path.Combine(hintDirectory, HashIdentity(identity) + ".json");

        // The hint is only a lookup aid when the title changed. The directory marker remains
        // authoritative: moved/deleted directories make their old names immediately reusable.
        var hinted = ReadValidHint(root, hintPath, identity);
        if (hinted != null) return hinted;

        Directory.CreateDirectory(parent);
        EnsureNoLinksBelowRoot(root, parent);

        // A previous collision may have put this gallery in a suffixed directory. If the plain
        // name was subsequently moved away, keep using that existing directory on a retry.
        foreach (var sibling in Directory.EnumerateDirectories(parent)
                     .Where(d => IsCandidateName(Path.GetFileName(d), prettyName))
                     .OrderBy(d => string.Equals(Path.GetFileName(d), prettyName, PathComparison) ? 0 : 1)
                     .ThenBy(d => d, StringComparer.Ordinal))
        {
            if (!IsLink(sibling) && ReadMarkerState(sibling, identity) == MarkerState.Own)
            {
                WriteHint(root, hintPath, sibling, identity);
                return sibling;
            }
        }

        for (var attempt = 0;; attempt++)
        {
            var name = CandidateName(prettyName, identity, attempt);
            var directory = Path.Combine(parent, name);
            if (TryClaim(directory, identity))
            {
                WriteHint(root, hintPath, directory, identity);
                return directory;
            }
        }
    }

    /// <summary>Checks ownership immediately before using a previously claimed output path.</summary>
    public static void EnsureOwned(string directory, string sourceKey)
    {
        var identity = ParseIdentity(sourceKey);
        if (ReadMarkerState(Path.GetFullPath(directory), identity) != MarkerState.Own)
            throw new IOException($"ExHentai gallery directory is no longer owned by {identity.Id}/{identity.Token}: {directory}");
    }

    /// <summary>Rejects links below the download root before reading or writing a gallery file.</summary>
    public static void EnsureOwnedOutputPath(string downloadRoot, string directory, string sourceKey, string file)
    {
        var root = Path.GetFullPath(downloadRoot);
        var gallery = Path.GetFullPath(directory);
        var output = Path.GetFullPath(file);
        if (!IsDescendant(root, gallery) || !IsDescendant(gallery, output))
            throw new IOException($"ExHentai gallery file path escaped its owned directory: {file}");

        EnsureOwned(gallery, sourceKey);
        EnsureNoLinksBelowRoot(root, output);
    }

    private static void EnsureNoLinksBelowRoot(string root, string path)
    {
        var current = root;
        foreach (var segment in Path.GetRelativePath(root, path).Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException($"ExHentai gallery output path contains a link: {current}");
            }
            catch (FileNotFoundException)
            {
                break;
            }
            catch (DirectoryNotFoundException)
            {
                break;
            }
        }
    }

    private static bool IsLink(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
    }

    private static bool IsDescendant(string parent, string child) =>
        child.StartsWith(parent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                         Path.DirectorySeparatorChar, PathComparison);

    private static bool TryClaim(string directory, GalleryIdentity identity)
    {
        if (File.Exists(directory) || IsLink(directory)) return false;

        try
        {
            Directory.CreateDirectory(directory);
        }
        catch (IOException) when (File.Exists(directory))
        {
            return false;
        }

        if (IsLink(directory)) return false;

        var state = ReadMarkerState(directory, identity);
        if (state == MarkerState.Own) return true;
        if (state == MarkerState.Other) return false;

        // An old, nonempty directory has no provable owner. Claim only an empty one.
        if (Directory.EnumerateFileSystemEntries(directory).Any()) return false;

        var markerPath = Path.Combine(directory, MarkerFileName);
        var parent = Path.GetDirectoryName(directory)!;
        var temporary = Path.Combine(parent, ".bakabase-exhentai-gallery-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, new OwnershipMarker
                {
                    Version = MarkerVersion,
                    GalleryId = identity.Id,
                    GalleryToken = identity.Token
                }, JsonOptions);
                stream.Flush(true);
            }

            try
            {
                // The final marker appears only after its JSON is complete, and never replaces
                // another process's claim. A crashed writer can leave only an expendable temp file.
                File.Move(temporary, markerPath, false);
                return true;
            }
            catch (IOException) when (File.Exists(markerPath))
            {
                return ReadMarkerState(directory, identity) == MarkerState.Own;
            }
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static void RequireAccessibleRoot(string root)
    {
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"ExHentai download root is unavailable: {root}");

        // Directory.Exists alone cannot distinguish an unreadable folder from a readable one.
        using var entries = Directory.EnumerateFileSystemEntries(root).GetEnumerator();
        _ = entries.MoveNext();
    }

    private static FileStream AcquireAllocationLock(string path)
    {
        // Keep the lock file itself after closing it. Deleting it would let a new process lock a
        // different inode while an older process still holds the original file open.
        for (var attempt = 0; attempt < 1200; attempt++)
        {
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (File.Exists(path) && attempt < 1199)
            {
                Thread.Sleep(25);
            }
        }

        throw new IOException($"Cannot acquire ExHentai gallery allocation lock: {path}");
    }

    private static string? ReadValidHint(string root, string path, GalleryIdentity identity)
    {
        if (!File.Exists(path)) return null;

        DirectoryHint? hint;
        try
        {
            using var stream = File.OpenRead(path);
            hint = JsonSerializer.Deserialize<DirectoryHint>(stream, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }

        if (hint is not {Version: MarkerVersion} || hint.GalleryId != identity.Id ||
            !string.Equals(hint.GalleryToken, identity.Token, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(hint.RelativeDirectory))
            return null;

        string directory;
        try
        {
            directory = ValidateDirectory(root, hint.RelativeDirectory, out _);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        try
        {
            EnsureNoLinksBelowRoot(root, directory);
        }
        catch (IOException)
        {
            return null;
        }

        return ReadMarkerState(directory, identity) == MarkerState.Own ? directory : null;
    }

    private static void WriteHint(string root, string path, string directory, GalleryIdentity identity)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, new DirectoryHint
                {
                    Version = MarkerVersion,
                    GalleryId = identity.Id,
                    GalleryToken = identity.Token,
                    RelativeDirectory = Path.GetRelativePath(root, directory)
                }, JsonOptions);
                stream.Flush(true);
            }

            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static string HashIdentity(GalleryIdentity identity)
    {
        var key = identity.Id.ToString(CultureInfo.InvariantCulture) + "/" + identity.Token;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();
    }

    private static MarkerState ReadMarkerState(string directory, GalleryIdentity identity)
    {
        var markerPath = Path.Combine(directory, MarkerFileName);
        if (!File.Exists(markerPath)) return MarkerState.Missing;

        // A contender may have created the marker and still be writing it with FileShare.None.
        // Wait for the complete document instead of mistaking an in-progress claim for legacy data.
        for (var attempt = 0; attempt < 40; attempt++)
        {
            try
            {
                using var stream = new FileStream(markerPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                var marker = JsonSerializer.Deserialize<OwnershipMarker>(stream, JsonOptions);
                return marker is {Version: MarkerVersion} && marker.GalleryId == identity.Id &&
                       string.Equals(marker.GalleryToken, identity.Token, StringComparison.OrdinalIgnoreCase)
                    ? MarkerState.Own
                    : MarkerState.Other;
            }
            catch (FileNotFoundException)
            {
                return MarkerState.Missing;
            }
            catch (DirectoryNotFoundException)
            {
                return MarkerState.Missing;
            }
            catch (JsonException)
            {
                // A truncated or malformed marker cannot establish ownership.
                return MarkerState.Other;
            }
            catch (IOException) when (attempt < 39 && File.Exists(markerPath))
            {
                Thread.Sleep(25);
            }
        }

        throw new IOException($"Cannot read ExHentai gallery ownership marker: {markerPath}");
    }

    private static bool IsCandidateName(string name, string prettyName) =>
        string.Equals(name, prettyName, PathComparison) ||
        (name.StartsWith(prettyName + " [g", PathComparison) && name.EndsWith(']'));

    private static string CandidateName(string prettyName, GalleryIdentity identity, int attempt) => attempt switch
    {
        0 => prettyName,
        1 => $"{prettyName} [g{identity.Id}]",
        2 => $"{prettyName} [g{identity.Id}-{identity.Token}]",
        _ => $"{prettyName} [g{identity.Id}-{identity.Token}-{attempt - 1}]"
    };

    private static string ValidateDirectory(string downloadRoot, string relativeGalleryDirectory, out string root)
    {
        if (string.IsNullOrWhiteSpace(downloadRoot))
            throw new ArgumentException("A download root is required.", nameof(downloadRoot));
        if (string.IsNullOrWhiteSpace(relativeGalleryDirectory) || Path.IsPathRooted(relativeGalleryDirectory))
            throw new ArgumentException("A relative gallery directory is required.", nameof(relativeGalleryDirectory));

        root = Path.GetFullPath(downloadRoot);
        var requested = Path.GetFullPath(Path.Combine(root, relativeGalleryDirectory));
        var relative = Path.GetRelativePath(root, requested);
        if (relative == "." || relative == ".." ||
            relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            Path.IsPathRooted(relative))
            throw new ArgumentException("The gallery directory must be inside the download root.",
                nameof(relativeGalleryDirectory));

        var firstSeparator = relative.IndexOf(Path.DirectorySeparatorChar);
        var firstSegment = firstSeparator < 0 ? relative : relative[..firstSeparator];
        if (string.Equals(firstSegment, HintDirectoryName, PathComparison))
            throw new ArgumentException("The gallery directory uses a reserved internal path.",
                nameof(relativeGalleryDirectory));

        return requested;
    }

    private static GalleryIdentity ParseIdentity(string sourceKey)
    {
        var slash = sourceKey?.IndexOf('/') ?? -1;
        if (slash <= 0 || slash == sourceKey!.Length - 1 || sourceKey.IndexOf('/', slash + 1) >= 0 ||
            !long.TryParse(sourceKey[..slash], NumberStyles.None, CultureInfo.InvariantCulture, out var id) ||
            id <= 0 || !sourceKey[(slash + 1)..].All(char.IsAsciiLetterOrDigit))
            throw new ArgumentException("A normalized ExHentai gallery ID and token are required.", nameof(sourceKey));

        return new GalleryIdentity(id, sourceKey[(slash + 1)..].ToLowerInvariant());
    }

    private enum MarkerState { Missing, Own, Other }

    private readonly record struct GalleryIdentity(long Id, string Token);

    private sealed class OwnershipMarker
    {
        public OwnershipMarker() { }

        public int Version { get; set; }
        public long GalleryId { get; set; }
        public string? GalleryToken { get; set; }
    }

    private sealed class DirectoryHint
    {
        public DirectoryHint() { }

        public int Version { get; set; }
        public long GalleryId { get; set; }
        public string? GalleryToken { get; set; }
        public string? RelativeDirectory { get; set; }
    }
}
