using System.Linq.Expressions;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Input;
using Bakabase.Abstractions.Models.View;
using Bootstrap.Components.Tasks;

namespace Bakabase.Abstractions.Services;

public interface IMediaLibraryV2Service
{
    Task Add(MediaLibraryV2AddOrPutInputModel model);
    Task Put(int id, MediaLibraryV2AddOrPutInputModel model);
    Task Put(IEnumerable<MediaLibraryV2> data);
    Task Patch(int id, MediaLibraryV2PatchInputModel model);
    /// <summary>
    /// Warning: this will replace all existing media libraries with the provided ones.
    /// </summary>
    /// <param name="models"></param>
    /// <returns></returns>
    Task ReplaceAll(MediaLibraryV2[] models);
    Task<MediaLibraryV2> Get(int id, MediaLibraryV2AdditionalItem additionalItems = MediaLibraryV2AdditionalItem.None);

    Task<List<MediaLibraryV2>> GetByKeys(int[] ids,
        MediaLibraryV2AdditionalItem additionalItems = MediaLibraryV2AdditionalItem.None);

    Task<List<MediaLibraryV2>> GetAll(Expression<Func<MediaLibraryV2DbModel, bool>>? filter = null,
        MediaLibraryV2AdditionalItem additionalItems = MediaLibraryV2AdditionalItem.None);

    Task Delete(int id);

    Task<MediaLibrarySyncResultViewModel> Sync(int id, Func<int, Task>? onProgressChange,
        Func<string?, Task>? onProcessChange, PauseToken pt,
        CancellationToken ct);

    Task<MediaLibrarySyncResultViewModel> SyncAll(int[]? ids, Func<int, Task>? onProgressChange,
        Func<string?, Task>? onProcessChange, PauseToken pt,
        CancellationToken ct);

    Task StartSyncAll(int[]? ids = null);

    Task MarkAsSynced(int[] ids);
    Task MarkAsSynced(int id, int resourceCount, string? syncVersion);

    Task<IEnumerable<MediaLibraryV2>> GetAllSyncMayBeOutdated();
}