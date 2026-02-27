using Bakabase.Modules.AI.Models.Db;
using Bakabase.Modules.AI.Models.Domain;
using Bakabase.Modules.AI.Models.Input;
using Microsoft.Extensions.AI;

namespace Bakabase.Modules.AI.Services;

public interface ILlmProviderService
{
    Task<IReadOnlyList<LlmProviderConfigDbModel>> GetAllProvidersAsync(CancellationToken ct = default);
    Task<LlmProviderConfigDbModel?> GetProviderAsync(int id, CancellationToken ct = default);
    Task<LlmProviderConfigDbModel> AddProviderAsync(LlmProviderConfigAddInputModel input, CancellationToken ct = default);
    Task<LlmProviderConfigDbModel> UpdateProviderAsync(int id, LlmProviderConfigUpdateInputModel input, CancellationToken ct = default);
    Task DeleteProviderAsync(int id, CancellationToken ct = default);
    Task<bool> TestConnectionAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<LlmModelInfo>> GetModelsAsync(int id, CancellationToken ct = default);
    IReadOnlyList<LlmProviderTypeInfo> GetProviderTypes();
    IChatClient CreateChatClient(LlmProviderConfigDbModel config, string modelId);
}
