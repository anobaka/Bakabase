using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Workflow.Artifacts;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.InsideWorld.Business.Components.Artifacts;

/// <summary>Freeze a caller's side-effect slot while allowing different callers to share one operation.</summary>
public sealed class ArtifactOperationBindingService(BakabaseDbContext db)
{
    public async Task<bool> BindAsync(string ownerKind, string ownerId, string slot, string operationId,
        CancellationToken ct = default)
    {
        if (new[] {ownerKind, ownerId, slot, operationId}.Any(s => string.IsNullOrWhiteSpace(s) || s.Length > 2048))
            throw new ArgumentException("A side-effect binding needs bounded owner, slot and operation identities.");
        var id = ArtifactManifestFiles.Hash(JsonSerializer.Serialize(new[] {ownerKind, ownerId, slot}));
        var bindings = db.Set<ArtifactOperationBindingDbModel>();
        var existing = await bindings.AsNoTracking().SingleOrDefaultAsync(b => b.Id == id, ct);
        if (existing != null) return existing.OperationId == operationId;
        var binding = new ArtifactOperationBindingDbModel {Id = id, OwnerKind = ownerKind, OwnerId = ownerId,
            Slot = slot, OperationId = operationId};
        bindings.Add(binding);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            db.Entry(binding).State = EntityState.Detached;
            existing = await bindings.AsNoTracking().SingleOrDefaultAsync(b => b.Id == id, ct);
            if (existing == null) throw;
            return existing.OperationId == operationId;
        }
        db.Entry(binding).State = EntityState.Detached;
        return true;
    }
}
