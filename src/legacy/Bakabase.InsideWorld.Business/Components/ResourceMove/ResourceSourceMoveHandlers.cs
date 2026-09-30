using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.ResourceMove;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bootstrap.Components.Orm;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace Bakabase.InsideWorld.Business.Components.ResourceMove;

public sealed class SteamResourceSourceMoveHandler(BakabaseDbContext db) : IResourceSourceMoveHandler
{
    public ResourceSource Source => ResourceSource.Steam;
    public bool ProtectsLocalTree => true;
    public async Task<ResourceSourceMoveEvaluation> EvaluateAsync(ResourceSourceMoveContext context,
        CancellationToken cancellationToken = default)
    {
        if (!context.IsSourceRestoration)
            return new ResourceSourceMoveEvaluation { ReasonCode = "steamManaged" };
        // A legacy operation may predate the Steam restriction. Acknowledging an already
        // restored installation only cancels that operation; it never permits moving it.
        if (!int.TryParse(context.Link.SourceKey, out var appId))
            return new ResourceSourceMoveEvaluation { ReasonCode = "sourceRecordMissing" };
        var app = await db.SteamApps.AsNoTracking().SingleOrDefaultAsync(a => a.AppId == appId, cancellationToken);
        return app == null
            ? new ResourceSourceMoveEvaluation { ReasonCode = "sourceRecordMissing" }
            : SourceMoveLocations.Evaluate(context, app.Id, app.ResourceId, app.InstallPath, app.IsInstalled);
    }
    public Task ApplyAsync(ResourceSourceMoveStep step, CancellationToken cancellationToken = default) =>
        throw new ResourceSourceMoveException("steamManaged");
    public Task ValidateRecordedStateAsync(ResourceSourceMoveStep step, CancellationToken cancellationToken = default) =>
        throw new ResourceSourceMoveException("steamManaged");
}

/// <summary>These sources identify content without maintaining a separate local installation path.
/// Every supported source opts in explicitly; unknown sources never inherit this behavior.</summary>
public sealed class LocalContentResourceSourceMoveHandler(ResourceSource source) : IResourceSourceMoveHandler
{
    public ResourceSource Source { get; } = source is ResourceSource.Aigc or ResourceSource.Pixiv
        ? source : throw new ArgumentOutOfRangeException(nameof(source));
    public Task<ResourceSourceMoveEvaluation> EvaluateAsync(ResourceSourceMoveContext context,
        CancellationToken cancellationToken = default) => Task.FromResult(new ResourceSourceMoveEvaluation());
    public Task ApplyAsync(ResourceSourceMoveStep step, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public sealed class PathMarkResourceSourceMoveHandler(BakabaseDbContext db,
    FullMemoryCacheResourceService<BakabaseDbContext, ResourceSourceLinkDbModel, int> cache)
    : IResourceSourceMoveHandler
{
    public ResourceSource Source => ResourceSource.PathMark;
    public Task<ResourceSourceMoveEvaluation> EvaluateAsync(ResourceSourceMoveContext context,
        CancellationToken cancellationToken = default) => Task.FromResult(
        SourceMoveLocations.Equal(context.Link.SourceKey, context.SourcePath)
            ? new ResourceSourceMoveEvaluation { PreviousLocation = context.Link.SourceKey, NewLocation = context.DestPath }
            : new ResourceSourceMoveEvaluation { ReasonCode = "sourceLocationChanged" });

    public async Task ValidateRecordedStateAsync(ResourceSourceMoveStep step, CancellationToken cancellationToken = default)
    {
        var current = await db.ResourceSourceLinks.AsNoTracking().SingleOrDefaultAsync(l => l.Id == step.LinkId &&
            l.ResourceId == step.ResourceId && l.Source == ResourceSource.PathMark, cancellationToken);
        if (current == null) throw new ResourceSourceMoveException("sourceRecordMissing");
        SourceMoveLocations.ValidateRecordedLocation(current.SourceKey, step);
    }

    public async Task ApplyAsync(ResourceSourceMoveStep step, CancellationToken cancellationToken = default)
    {
        var query = db.ResourceSourceLinks.Where(l => l.Id == step.LinkId &&
            l.ResourceId == step.ResourceId && l.Source == ResourceSource.PathMark);
        var current = await query.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (current == null) throw new ResourceSourceMoveException("sourceRecordMissing");
        if (SourceMoveLocations.Equal(current.SourceKey, step.NewLocation)) { cache.ClearCache(); return; }
        if (current.SourceKey != step.PreviousLocation || step.NewLocation == null)
            throw new ResourceSourceMoveException("sourceLocationChanged");
        var changed = await query.Where(l => l.SourceKey == step.PreviousLocation)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.SourceKey, step.NewLocation), cancellationToken);
        if (changed != 1) throw new ResourceSourceMoveException("sourceLocationChanged");
        cache.ClearCache();
    }
}

public sealed class DLsiteResourceSourceMoveHandler(BakabaseDbContext db,
    FullMemoryCacheResourceService<BakabaseDbContext, DLsiteWorkDbModel, int> cache)
    : IResourceSourceMoveHandler
{
    public ResourceSource Source => ResourceSource.DLsite;
    public async Task<ResourceSourceMoveEvaluation> EvaluateAsync(ResourceSourceMoveContext context,
        CancellationToken cancellationToken = default)
    {
        var work = await db.DLsiteWorks.AsNoTracking()
            .SingleOrDefaultAsync(w => w.WorkId == context.Link.SourceKey, cancellationToken);
        return work == null
            ? new ResourceSourceMoveEvaluation { ReasonCode = "sourceRecordMissing" }
            : SourceMoveLocations.Evaluate(context, work.Id, work.ResourceId, work.LocalPath, work.IsDownloaded);
    }

    public async Task ValidateRecordedStateAsync(ResourceSourceMoveStep step, CancellationToken cancellationToken = default)
    {
        var snapshot = SourceMoveLocations.ReadSnapshot(step);
        var work = await db.DLsiteWorks.AsNoTracking().SingleOrDefaultAsync(w => w.Id == snapshot.Id &&
            w.WorkId == step.SourceKey && w.ResourceId == snapshot.ResourceId &&
            w.IsDownloaded == snapshot.IsDownloaded, cancellationToken);
        if (work == null) throw new ResourceSourceMoveException("sourceLocationChanged");
        SourceMoveLocations.ValidateRecordedLocation(work.LocalPath, step);
    }

    public async Task ApplyAsync(ResourceSourceMoveStep step, CancellationToken cancellationToken = default)
    {
        var snapshot = SourceMoveLocations.ReadSnapshot(step);
        var query = db.DLsiteWorks.Where(w => w.Id == snapshot.Id && w.WorkId == step.SourceKey &&
            w.ResourceId == snapshot.ResourceId && w.IsDownloaded == snapshot.IsDownloaded);
        var work = await query.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (work == null) throw new ResourceSourceMoveException("sourceLocationChanged");
        if (SourceMoveLocations.Equal(work.LocalPath, step.NewLocation)) { cache.ClearCache(); return; }
        if (work.LocalPath != step.PreviousLocation)
            throw new ResourceSourceMoveException("sourceLocationChanged");
        // Compare and update only the owned location fields. A metadata refresh or another
        // operation must not be overwritten by a stale full-row object from the ORM cache.
        var changed = await query.Where(w => w.LocalPath == step.PreviousLocation)
            .ExecuteUpdateAsync(s => s.SetProperty(w => w.LocalPath, step.NewLocation)
                .SetProperty(w => w.UpdatedAt, DateTime.Now), cancellationToken);
        if (changed != 1) throw new ResourceSourceMoveException("sourceLocationChanged");
        cache.ClearCache();
    }
}

public sealed class ExHentaiResourceSourceMoveHandler(BakabaseDbContext db,
    FullMemoryCacheResourceService<BakabaseDbContext, ExHentaiGalleryDbModel, int> cache)
    : IResourceSourceMoveHandler
{
    public ResourceSource Source => ResourceSource.ExHentai;
    public async Task<ResourceSourceMoveEvaluation> EvaluateAsync(ResourceSourceMoveContext context,
        CancellationToken cancellationToken = default)
    {
        var parts = context.Link.SourceKey.Split('/');
        if (parts.Length != 2 || !long.TryParse(parts[0], out var galleryId))
            return new ResourceSourceMoveEvaluation { ReasonCode = "sourceRecordMissing" };
        var token = parts[1];
        var gallery = await db.ExHentaiGalleries.AsNoTracking().SingleOrDefaultAsync(
            g => g.GalleryId == galleryId && g.GalleryToken == token, cancellationToken);
        return gallery == null
            ? new ResourceSourceMoveEvaluation { ReasonCode = "sourceRecordMissing" }
            : SourceMoveLocations.Evaluate(context, gallery.Id, gallery.ResourceId, gallery.LocalPath, gallery.IsDownloaded);
    }

    public async Task ValidateRecordedStateAsync(ResourceSourceMoveStep step, CancellationToken cancellationToken = default)
    {
        var snapshot = SourceMoveLocations.ReadSnapshot(step);
        var parts = step.SourceKey.Split('/');
        if (parts.Length != 2 || !long.TryParse(parts[0], out var galleryId))
            throw new ResourceSourceMoveException("sourceRecordMissing");
        var token = parts[1];
        var gallery = await db.ExHentaiGalleries.AsNoTracking().SingleOrDefaultAsync(g => g.Id == snapshot.Id &&
            g.GalleryId == galleryId && g.GalleryToken == token && g.ResourceId == snapshot.ResourceId &&
            g.IsDownloaded == snapshot.IsDownloaded, cancellationToken);
        if (gallery == null) throw new ResourceSourceMoveException("sourceLocationChanged");
        SourceMoveLocations.ValidateRecordedLocation(gallery.LocalPath, step);
    }

    public async Task ApplyAsync(ResourceSourceMoveStep step, CancellationToken cancellationToken = default)
    {
        var snapshot = SourceMoveLocations.ReadSnapshot(step);
        var parts = step.SourceKey.Split('/');
        if (parts.Length != 2 || !long.TryParse(parts[0], out var galleryId))
            throw new ResourceSourceMoveException("sourceRecordMissing");
        var token = parts[1];
        var query = db.ExHentaiGalleries.Where(g => g.Id == snapshot.Id && g.GalleryId == galleryId &&
            g.GalleryToken == token && g.ResourceId == snapshot.ResourceId && g.IsDownloaded == snapshot.IsDownloaded);
        var gallery = await query.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (gallery == null) throw new ResourceSourceMoveException("sourceLocationChanged");
        if (SourceMoveLocations.Equal(gallery.LocalPath, step.NewLocation)) { cache.ClearCache(); return; }
        if (gallery.LocalPath != step.PreviousLocation)
            throw new ResourceSourceMoveException("sourceLocationChanged");
        var changed = await query.Where(g => g.LocalPath == step.PreviousLocation)
            .ExecuteUpdateAsync(s => s.SetProperty(g => g.LocalPath, step.NewLocation)
                .SetProperty(g => g.UpdatedAt, DateTime.Now), cancellationToken);
        if (changed != 1) throw new ResourceSourceMoveException("sourceLocationChanged");
        cache.ClearCache();
    }
}

internal static class SourceMoveLocations
{
    internal sealed record Snapshot(int Id, int? ResourceId, bool IsDownloaded);
    internal static bool Equal(string? a, string? b) => a != null && b != null &&
        string.Equals(a.StandardizePath(), b.StandardizePath(), StringComparison.OrdinalIgnoreCase);

    internal static void ValidateRecordedLocation(string? current, ResourceSourceMoveStep step)
    {
        if (step.PreviousLocation == null || step.NewLocation == null ||
            current != step.PreviousLocation && !Equal(current, step.NewLocation))
            throw new ResourceSourceMoveException("sourceLocationChanged");
    }

    internal static ResourceSourceMoveEvaluation Evaluate(ResourceSourceMoveContext context, int rowId,
        int? resourceId, string? localPath, bool isDownloaded)
    {
        if (!isDownloaded || resourceId.HasValue && resourceId != context.ResourceId ||
            !localPath.IsPathEqualOrUnder(context.SourcePath))
            return new ResourceSourceMoveEvaluation { ReasonCode = "sourceLocationChanged" };
        var source = context.SourcePath.StandardizePath()!;
        var location = localPath.StandardizePath()!;
        return new ResourceSourceMoveEvaluation
        {
            PreviousLocation = localPath,
            NewLocation = context.DestPath.StandardizePath()! + location[source.Length..],
            StateJson = JsonConvert.SerializeObject(new Snapshot(rowId, resourceId, isDownloaded))
        };
    }

    internal static Snapshot ReadSnapshot(ResourceSourceMoveStep step)
    {
        if (step.PreviousLocation == null || step.NewLocation == null || step.StateJson == null)
            throw new ResourceSourceMoveException("sourceMoveUnsupported");
        try
        {
            return JsonConvert.DeserializeObject<Snapshot>(step.StateJson) ??
                   throw new ResourceSourceMoveException("sourceMoveUnsupported");
        }
        catch (JsonException) { throw new ResourceSourceMoveException("sourceMoveUnsupported"); }
    }
}
