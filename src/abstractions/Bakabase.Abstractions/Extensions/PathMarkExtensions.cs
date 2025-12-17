using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;

namespace Bakabase.Abstractions.Extensions;

public static class PathMarkExtensions
{
    public static PathMarkDbModel ToDbModel(this PathMark model)
    {
        return new PathMarkDbModel
        {
            Id = model.Id,
            Path = model.Path,
            Type = model.Type,
            Priority = model.Priority,
            ConfigJson = model.ConfigJson,
            SyncStatus = model.SyncStatus,
            SyncedAt = model.SyncedAt,
            SyncError = model.SyncError,
            CreatedAt = model.CreatedAt,
            UpdatedAt = model.UpdatedAt,
            IsDeleted = model.IsDeleted,
            DeletedAt = model.DeletedAt,
            ExpiresInSeconds = model.ExpiresInSeconds
        };
    }

    public static PathMark ToDomainModel(this PathMarkDbModel dbModel)
    {
        return new PathMark
        {
            Id = dbModel.Id,
            Path = dbModel.Path,
            Type = dbModel.Type,
            Priority = dbModel.Priority,
            ConfigJson = dbModel.ConfigJson,
            SyncStatus = dbModel.SyncStatus,
            SyncedAt = dbModel.SyncedAt,
            SyncError = dbModel.SyncError,
            CreatedAt = dbModel.CreatedAt,
            UpdatedAt = dbModel.UpdatedAt,
            IsDeleted = dbModel.IsDeleted,
            DeletedAt = dbModel.DeletedAt,
            ExpiresInSeconds = dbModel.ExpiresInSeconds
        };
    }

    public static List<PathMark> ToDomainModels(this IEnumerable<PathMarkDbModel> dbModels)
    {
        return dbModels.Select(d => d.ToDomainModel()).ToList();
    }
}
