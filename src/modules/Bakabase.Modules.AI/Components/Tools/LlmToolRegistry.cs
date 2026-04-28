using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.AI.Models.Db;
using Bootstrap.Components.Orm.Infrastructures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;

namespace Bakabase.Modules.AI.Components.Tools;

/// <summary>
/// Registry for all available LLM tools. Collects AIFunctions from all registered ILlmTool implementations.
/// Supports filtering by enabled state via persistent configuration.
/// </summary>
public class LlmToolRegistry<TDbContext>(
    IEnumerable<ILlmTool> tools,
    ResourceService<TDbContext, LlmToolConfigDbModel, int> toolConfigOrm
) : LlmToolRegistry where TDbContext : DbContext
{
    public override IReadOnlyList<LlmToolMetadata> GetAllMetadata()
    {
        return tools.SelectMany(t => t.GetMetadata()).ToList();
    }

    public override IReadOnlyList<AITool> GetAllTools()
    {
        return tools.SelectMany(t => t.GetFunctions()).Cast<AITool>().ToList();
    }

    public override async Task<IReadOnlyList<AITool>> GetEnabledToolsAsync(CancellationToken ct = default)
    {
        var configs = await toolConfigOrm.GetAll();
        var disabledSet = configs.Where(c => !c.IsEnabled).Select(c => c.ToolName).ToHashSet();
        var allMetadata = GetAllMetadata();

        // A tool is enabled if:
        // - It has an explicit config with IsEnabled=true, OR
        // - It has no config AND IsReadOnly=true (default enabled for read-only tools)
        var enabledNames = new HashSet<string>();
        foreach (var meta in allMetadata)
        {
            var config = configs.FirstOrDefault(c => c.ToolName == meta.Name);
            if (config != null)
            {
                if (config.IsEnabled) enabledNames.Add(meta.Name);
            }
            else
            {
                // No config: default to enabled for read-only, disabled for write
                if (meta.IsReadOnly) enabledNames.Add(meta.Name);
            }
        }

        return tools.SelectMany(t => t.GetFunctions())
            .Where(f => enabledNames.Contains(f.Name))
            .Cast<AITool>()
            .ToList();
    }

    public override async Task<ChatOptions> WithEnabledToolsAsync(ChatOptions? options = null,
        CancellationToken ct = default)
    {
        options ??= new ChatOptions();
        var enabledTools = await GetEnabledToolsAsync(ct);
        options.Tools = [..enabledTools];
        return options;
    }

    public override async Task<List<LlmToolConfigDbModel>> GetToolConfigsAsync(CancellationToken ct = default)
    {
        return (await toolConfigOrm.GetAll()).ToList();
    }

    public override async Task SaveToolConfigAsync(string toolName, bool isEnabled, CancellationToken ct = default)
    {
        var existing = (await toolConfigOrm.GetAll(x => x.ToolName == toolName)).FirstOrDefault();
        if (existing != null)
        {
            existing.IsEnabled = isEnabled;
            await toolConfigOrm.Update(existing);
        }
        else
        {
            await toolConfigOrm.Add(new LlmToolConfigDbModel { ToolName = toolName, IsEnabled = isEnabled });
        }
    }
}

/// <summary>
/// Base class for DI registration (non-generic).
/// </summary>
public abstract class LlmToolRegistry
{
    public abstract IReadOnlyList<LlmToolMetadata> GetAllMetadata();
    public abstract IReadOnlyList<AITool> GetAllTools();
    public abstract Task<IReadOnlyList<AITool>> GetEnabledToolsAsync(CancellationToken ct = default);
    public abstract Task<ChatOptions> WithEnabledToolsAsync(ChatOptions? options = null, CancellationToken ct = default);
    public abstract Task<List<LlmToolConfigDbModel>> GetToolConfigsAsync(CancellationToken ct = default);
    public abstract Task SaveToolConfigAsync(string toolName, bool isEnabled, CancellationToken ct = default);

    // Legacy compat
    public ChatOptions WithTools(ChatOptions? options = null)
    {
        options ??= new ChatOptions();
        options.Tools = [..GetAllTools()];
        return options;
    }
}
