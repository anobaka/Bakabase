using System.Text.Json;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;

namespace Bakabase.Modules.Acquisition.Components;

/// <summary>
/// Marks the actual resource directory, while keeping category directories out of an ancestor's
/// broad resource mark. Existing marks are never rewritten or removed.
/// </summary>
public static class AcquisitionLibraryMarks
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task PrepareAsync(IPathMarkService marks, string libraryRoot, string target,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var relative = Path.GetRelativePath(libraryRoot, target);
        var parts = relative.Split(Path.DirectorySeparatorChar);

        if (parts.Length > 1)
        {
            var branch = Path.Combine(libraryRoot, parts[0]);
            var existing = await marks.GetByPath(branch);
            if (!existing.Any(mark => mark.Type == PathMarkType.Resource &&
                                      ConfigOf(mark)?.IsResourceBoundary == true))
            {
                await marks.Add(new PathMark
                {
                    Path = branch,
                    Type = PathMarkType.Resource,
                    ConfigJson = JsonSerializer.Serialize(new ResourceMarkConfig
                    {
                        MatchMode = PathMatchMode.Regex,
                        Regex = "(?!)",
                        FsTypeFilter = PathFilterFsType.Directory,
                        IsResourceBoundary = true,
                    }, Json),
                });
            }
        }

        ct.ThrowIfCancellationRequested();
        var targetMarks = await marks.GetByPath(target);
        if (targetMarks.Any(mark => mark.Type == PathMarkType.Resource &&
                                    ConfigOf(mark) is
                                    {
                                        MatchMode: PathMatchMode.Layer,
                                        Layer: 0,
                                        FsTypeFilter: null or PathFilterFsType.Directory,
                                    })) return;

        await marks.Add(new PathMark
        {
            Path = target,
            Type = PathMarkType.Resource,
            ConfigJson = JsonSerializer.Serialize(new ResourceMarkConfig
            {
                MatchMode = PathMatchMode.Layer,
                Layer = 0,
                FsTypeFilter = PathFilterFsType.Directory,
            }, Json),
        });
    }

    private static ResourceMarkConfig? ConfigOf(PathMark mark)
    {
        try { return JsonSerializer.Deserialize<ResourceMarkConfig>(mark.ConfigJson, Json); }
        catch (JsonException) { return null; }
    }
}
