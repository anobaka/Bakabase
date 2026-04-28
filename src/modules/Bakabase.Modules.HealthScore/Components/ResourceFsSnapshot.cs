using System.Collections.Immutable;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.Modules.HealthScore.Components;

/// <summary>
/// Caches per-resource filesystem state for rule evaluation. Walks the directory
/// at most once and lazily classifies files. Shared by all predicates evaluating
/// a given resource so a single scoring pass scans each directory once.
/// </summary>
public sealed class ResourceFsSnapshot
{
    private static readonly ImmutableHashSet<string> CoverFileBaseNames =
        ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase,
            "cover", "folder", "poster", "thumb", "thumbnail", "default");

    private readonly Lazy<IReadOnlyList<FileInfo>> _allFiles;
    private readonly Lazy<ILookup<MediaType, FileInfo>> _byMediaType;
    private readonly Lazy<long> _totalBytes;
    private readonly Lazy<bool> _hasCoverImage;
    private readonly Lazy<IReadOnlyList<FileInfo>> _zeroByteFiles;
    private readonly Lazy<bool> _rootExists;

    public ResourceFsSnapshot(string rootPath, IReadOnlyList<string>? reservedCoverPaths = null)
    {
        RootPath = rootPath;

        _rootExists = new Lazy<bool>(() => Directory.Exists(rootPath) || File.Exists(rootPath));

        _allFiles = new Lazy<IReadOnlyList<FileInfo>>(() =>
        {
            try
            {
                if (Directory.Exists(rootPath))
                {
                    return new DirectoryInfo(rootPath)
                        .EnumerateFiles("*", SearchOption.AllDirectories)
                        .ToList();
                }

                if (File.Exists(rootPath))
                {
                    return new[] { new FileInfo(rootPath) };
                }
            }
            catch (DirectoryNotFoundException) { }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }

            return Array.Empty<FileInfo>();
        });

        _byMediaType = new Lazy<ILookup<MediaType, FileInfo>>(() =>
            _allFiles.Value.ToLookup(ClassifyMediaType));

        _totalBytes = new Lazy<long>(() => _allFiles.Value.Sum(f => SafeLength(f)));

        _zeroByteFiles = new Lazy<IReadOnlyList<FileInfo>>(() =>
            _allFiles.Value.Where(f => SafeLength(f) == 0).ToList());

        _hasCoverImage = new Lazy<bool>(() =>
        {
            if (reservedCoverPaths is { Count: > 0 } &&
                reservedCoverPaths.Any(p => !string.IsNullOrEmpty(p) && File.Exists(p)))
            {
                return true;
            }

            foreach (var f in _allFiles.Value)
            {
                if (!IsImageExtension(f.Extension)) continue;
                var stem = Path.GetFileNameWithoutExtension(f.Name);
                if (CoverFileBaseNames.Contains(stem))
                {
                    return true;
                }
            }

            return false;
        });
    }

    public string RootPath { get; }
    public bool RootExists => _rootExists.Value;
    public IReadOnlyList<FileInfo> AllFiles => _allFiles.Value;
    public ILookup<MediaType, FileInfo> ByMediaType => _byMediaType.Value;
    public long TotalBytes => _totalBytes.Value;
    public bool HasCoverImage => _hasCoverImage.Value;
    public IReadOnlyList<FileInfo> ZeroByteFiles => _zeroByteFiles.Value;

    public IEnumerable<FileInfo> FilesOf(MediaType type) => _byMediaType.Value[type];

    public long TotalBytesOf(MediaType type) => _byMediaType.Value[type].Sum(SafeLength);

    public static MediaType ClassifyMediaType(FileInfo f)
    {
        var ext = f.Extension;
        if (string.IsNullOrEmpty(ext)) return MediaType.Unknown;
        foreach (var (type, exts) in InternalOptions.MediaTypeExtensions)
        {
            if (exts.Contains(ext)) return type;
        }

        return MediaType.Unknown;
    }

    private static bool IsImageExtension(string ext) =>
        InternalOptions.ImageExtensions.Contains(ext);

    private static long SafeLength(FileInfo f)
    {
        try { return f.Length; }
        catch { return 0; }
    }
}
