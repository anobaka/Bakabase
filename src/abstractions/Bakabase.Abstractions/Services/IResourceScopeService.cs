using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;

namespace Bakabase.Abstractions.Services;

public interface IResourceScopeService
{
    Task<List<ResourceScopeDbModel>> GetAll();
    Task<ResourceScopeDbModel> Add(string name, string? searchJson, string? sourceDeviceId, string? sourceLibraryEpoch);
    Task<ResourceScopeDbModel> Update(int id, int expectedRevision, string name, string? searchJson);
    Task Delete(int id, int expectedRevision);
    Task<ResourceProfile> Bind(int profileId, int? scopeId, DateTime expectedUpdatedAt);
    Task<ResourceProfile> UpdateRule(int profileId, ResourceProfileRuleKind kind, string? valueJson, DateTime expectedUpdatedAt);
}

public sealed class ResourceScopeConflictException(string message) : InvalidOperationException(message);
