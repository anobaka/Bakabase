using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Downloader.Abstractions;
using Bakabase.Modules.Downloader.Models;
using Bakabase.Modules.Downloader.Models.Db;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Downloader;

/// <summary>Reconciles capability identities without starting or changing any provider execution.</summary>
public sealed class DownloadTaskRegistry(BakabaseDbContext db) : IDownloadTaskRegistry
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public async Task<string> EnsureBindingAsync(DownloadTaskBinding binding, CancellationToken ct = default)
    {
        await EnsureBindingsAsync([binding], ct);
        return binding.Id;
    }

    public async Task EnsureBindingsAsync(IReadOnlyCollection<DownloadTaskBinding> bindings, CancellationToken ct = default)
    {
        if (bindings.Count == 0) return;
        await Gate.WaitAsync(ct);
        try
        {
            var ids = bindings.Select(b => b.Id).Distinct().ToArray();
            var existing = await db.ManagedDownloadTasks.Where(t => ids.Contains(t.Id)).ToDictionaryAsync(t => t.Id, ct);
            foreach (var binding in bindings)
            {
                if (binding.Category is not ("general" or "site") || binding.ParentId == binding.Id ||
                    (binding.Provider == "site") != (binding.Category == "site"))
                    throw new ArgumentException("Invalid download task binding.");
                if (!existing.TryGetValue(binding.Id, out var row))
                {
                    row = new ManagedDownloadTaskDbModel
                    {
                        Id = binding.Id, Provider = binding.Provider, ProviderTaskId = binding.ProviderTaskId,
                        Category = binding.Category, CreatedAt = binding.CreatedAt
                    };
                    db.ManagedDownloadTasks.Add(row);
                    existing.Add(row.Id, row);
                }
                row.ParentId = binding.ParentId;
                row.SiteTaskId = binding.SiteTaskId;
                row.OriginKind = binding.OriginKind;
                row.OriginId = binding.OriginId;
                row.WorkflowRunId = binding.WorkflowRunId;
                row.WorkflowDefinitionId = binding.WorkflowDefinitionId;
                row.DownloadResultId = binding.DownloadResultId;
            }
            await db.SaveChangesAsync(ct);
        }
        finally { Gate.Release(); }
    }

    public Task<bool> IsUserStoppedAsync(string id, CancellationToken ct = default) =>
        db.ManagedDownloadTasks.AsNoTracking().AnyAsync(t => t.Id == id && t.UserStopped, ct);

    public Task SetUserStoppedAsync(string id, bool stopped, CancellationToken ct = default) =>
        db.ManagedDownloadTasks.Where(t => t.Id == id).ExecuteUpdateAsync(s => s.SetProperty(t => t.UserStopped, stopped), ct);

    public Task RemoveAsync(string id, CancellationToken ct = default) =>
        db.ManagedDownloadTasks.Where(t => t.Id == id).ExecuteDeleteAsync(ct);
}
