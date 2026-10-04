using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.CollectionMemo.Models.Db;
using Bakabase.InsideWorld.Business.Components.CollectionMemo.Models.Domain;
using Bakabase.InsideWorld.Business.Components.CollectionMemo.Models.Input;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.InsideWorld.Business.Components.CollectionMemo;

public class CollectionMemoService(BakabaseDbContext dbContext)
{
    private const string DuplicateNameMessage = "A collection target with this name already exists.";
    private static readonly Regex IsoDateTime = new(
        @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|[+-]\d{2}:\d{2})$",
        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    public async Task<List<CollectionMemoTarget>> GetTargets()
    {
        var targets = await dbContext.CollectionMemoTargets.AsNoTracking().ToListAsync();
        var rawRanges = await dbContext.CollectionMemoRanges.AsNoTracking().ToListAsync();
        var globalStart = await dbContext.CollectionMemoSettings.AsNoTracking().Where(s => s.Id == CollectionMemoSettingsDbModel.SingletonId)
            .Select(s => (DateTime?) s.StartAt).SingleOrDefaultAsync()
            ?? rawRanges.Where(r => r.StartAt.HasValue).Select(r => r.StartAt).Min() ?? DateTime.UtcNow;
        var ranges = rawRanges.OrderBy(r => r.StartAt ?? globalStart).ThenBy(r => r.EndAt).ThenBy(r => r.Id).ToLookup(r => r.TargetId);
        return targets.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ThenBy(t => t.Id)
            .Select(t => new CollectionMemoTarget
            {
                Id = t.Id,
                Name = t.Name,
                Ranges = ranges[t.Id].Select(r => new CollectionMemoRange
                {
                    Id = r.Id, StartAt = r.StartAt, EndAt = r.EndAt, Url = r.Url, Note = r.Note
                }).ToList()
            }).ToList();
    }

    public async Task<CollectionMemoSettings> GetSettings()
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        var settings = await EnsureSettings();
        await transaction.CommitAsync();
        return new CollectionMemoSettings {StartAt = settings.StartAt, Reverse = settings.Reverse};
    }

    public async Task<BaseResponse> UpdateSettings(CollectionMemoSettingsInputModel input)
    {
        if (!TryParseTime(input.StartAt, out var startAt))
            return BaseResponseBuilder.BuildBadRequest("The global start must be a valid ISO date-time with a UTC offset.");
        if (startAt > DateTime.UtcNow)
            return BaseResponseBuilder.BuildBadRequest("The global start cannot be in the future.");

        // The same immediate transaction used by range mutations prevents a settings change
        // from invalidating an inherited range inserted or resized by another request.
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        if (await dbContext.CollectionMemoRanges.AnyAsync(r => r.StartAt == null && r.EndAt < startAt))
            return BaseResponseBuilder.BuildBadRequest("The global start must be at or before every inherited range's end.");
        var updated = await dbContext.CollectionMemoSettings.Where(s => s.Id == CollectionMemoSettingsDbModel.SingletonId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.StartAt, startAt).SetProperty(r => r.Reverse, input.Reverse));
        if (updated == 0)
        {
            var settings = new CollectionMemoSettingsDbModel {StartAt = startAt, Reverse = input.Reverse};
            dbContext.CollectionMemoSettings.Add(settings);
            await dbContext.SaveChangesAsync();
            dbContext.Entry(settings).State = EntityState.Detached;
        }
        await transaction.CommitAsync();
        return BaseResponseBuilder.Ok;
    }

    private async Task<CollectionMemoSettingsDbModel> EnsureSettings()
    {
        var settings = await dbContext.CollectionMemoSettings.AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == CollectionMemoSettingsDbModel.SingletonId);
        if (settings != null) return settings;
        // Initialize in the service, not a data backfill in the schema migration. Once
        // persisted this start stays fixed when the clock or individual ranges change.
        var starts = await dbContext.CollectionMemoRanges.AsNoTracking().Where(r => r.StartAt != null)
            .Select(r => r.StartAt).ToListAsync();
        settings = new CollectionMemoSettingsDbModel {StartAt = starts.Min() ?? DateTime.UtcNow};
        dbContext.CollectionMemoSettings.Add(settings);
        await dbContext.SaveChangesAsync();
        dbContext.Entry(settings).State = EntityState.Detached;
        return settings;
    }

    public async Task<BaseResponse> CreateTarget(CollectionMemoTargetInputModel input)
    {
        var error = ValidateName(input.Name, out var name, out var normalizedName);
        if (error != null) return error;
        if (await NameExists(normalizedName)) return BaseResponseBuilder.BuildBadRequest(DuplicateNameMessage);

        var target = new CollectionMemoTargetDbModel {Name = name, NormalizedName = normalizedName};
        dbContext.CollectionMemoTargets.Add(target);
        return await SaveTarget(target);
    }

    public async Task<BaseResponse> UpdateTarget(int targetId, CollectionMemoTargetInputModel input)
    {
        var target = await dbContext.CollectionMemoTargets.FindAsync(targetId);
        if (target == null) return BaseResponseBuilder.NotFound;
        var error = ValidateName(input.Name, out var name, out var normalizedName);
        if (error != null) return error;
        if (await NameExists(normalizedName, targetId))
            return BaseResponseBuilder.BuildBadRequest(DuplicateNameMessage);

        target.Name = name;
        target.NormalizedName = normalizedName;
        return await SaveTarget(target);
    }

    public async Task<BaseResponse> DeleteTarget(int targetId)
    {
        var target = await dbContext.CollectionMemoTargets.FindAsync(targetId);
        if (target == null) return BaseResponseBuilder.NotFound;
        dbContext.CollectionMemoTargets.Remove(target);
        await dbContext.SaveChangesAsync();
        return BaseResponseBuilder.Ok;
    }

    public async Task<BaseResponse> CreateRange(int targetId, CollectionMemoRangeInputModel input)
    {
        var error = ValidateRange(input, out var startAt, out var endAt, out var url, out var note);
        if (error != null) return error;
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        if (!await dbContext.CollectionMemoTargets.AnyAsync(t => t.Id == targetId))
            return BaseResponseBuilder.NotFound;
        if (startAt == null && endAt < (await EnsureSettings()).StartAt)
            return BaseResponseBuilder.BuildBadRequest("The end must be at or after the global start.");

        dbContext.CollectionMemoRanges.Add(new CollectionMemoRangeDbModel
        {
            TargetId = targetId, StartAt = startAt, EndAt = endAt, Url = url, Note = note
        });
        await dbContext.SaveChangesAsync();
        await transaction.CommitAsync();
        return BaseResponseBuilder.Ok;
    }

    public async Task<BaseResponse> UpdateRange(int targetId, int id, CollectionMemoRangeInputModel input)
    {
        var error = ValidateRange(input, out var startAt, out var endAt, out var url, out var note);
        if (error != null) return error;
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        var range = await dbContext.CollectionMemoRanges.AsNoTracking().SingleOrDefaultAsync(r => r.TargetId == targetId && r.Id == id);
        if (range == null) return BaseResponseBuilder.NotFound;
        if (startAt == null && endAt < (await EnsureSettings()).StartAt)
            return BaseResponseBuilder.BuildBadRequest("The end must be at or after the global start.");

        await dbContext.CollectionMemoRanges.Where(r => r.TargetId == targetId && r.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.StartAt, startAt).SetProperty(r => r.EndAt, endAt)
                .SetProperty(r => r.Url, url).SetProperty(r => r.Note, note));
        await transaction.CommitAsync();
        DetachRanges([range]);
        return BaseResponseBuilder.Ok;
    }

    public async Task<BaseResponse> DeleteRange(int targetId, int id)
    {
        var range = await dbContext.CollectionMemoRanges.SingleOrDefaultAsync(r => r.TargetId == targetId && r.Id == id);
        if (range == null) return BaseResponseBuilder.NotFound;
        dbContext.CollectionMemoRanges.Remove(range);
        await dbContext.SaveChangesAsync();
        return BaseResponseBuilder.Ok;
    }

    public async Task<BaseResponse> FillGap(int targetId, CollectionMemoRangeInputModel input)
    {
        var error = ValidateRange(input, out var startAt, out var endAt, out var url, out var note);
        if (error != null) return error;

        // SQLite begins an immediate write transaction: no other writer can change coverage
        // between reading its connected components and committing the consolidated result.
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        if (!await dbContext.CollectionMemoTargets.AnyAsync(t => t.Id == targetId))
            return BaseResponseBuilder.NotFound;
        var globalStart = (await EnsureSettings()).StartAt;
        var effectiveStart = startAt ?? globalStart;
        if (endAt < effectiveStart)
            return BaseResponseBuilder.BuildBadRequest("The end must be at or after the global start.");
        var components = BuildComponents(await dbContext.CollectionMemoRanges.AsNoTracking()
            .Where(r => r.TargetId == targetId).ToListAsync(), globalStart);
        var connected = components.Where(c => c.StartAt <= endAt && c.EndAt >= effectiveStart).ToList();
        var selected = connected.SelectMany(c => c.Ranges).ToList();
        // Metadata belongs to its original record, not the whole connected timeline.
        // Fill only the requested gap when consolidation would remove links or notes.
        if (selected.Count == 0 || selected.Any(HasMetadata) || url != null || note != null)
        {
            dbContext.CollectionMemoRanges.Add(new CollectionMemoRangeDbModel
            {
                TargetId = targetId, StartAt = startAt, EndAt = endAt, Url = url, Note = note
            });
            await dbContext.SaveChangesAsync();
        }
        else
        {
            effectiveStart = connected.Min(c => c.StartAt) < effectiveStart ? connected.Min(c => c.StartAt) : effectiveStart;
            endAt = connected.Max(c => c.EndAt) > endAt ? connected.Max(c => c.EndAt) : endAt;
            var inherited = (startAt == null || selected.Any(r => r.StartAt == null)) && effectiveStart == globalStart;
            await Consolidate(targetId, selected, inherited ? null : effectiveStart, endAt);
        }
        await transaction.CommitAsync();
        DetachRanges(selected);
        return BaseResponseBuilder.Ok;
    }

    public async Task<BaseResponse> ResizeCoverage(int targetId, CollectionMemoCoverageResizeInputModel input)
    {
        if (input.Edge is not ("start" or "end") || !TryParseTime(input.At, out var at))
            return BaseResponseBuilder.BuildBadRequest("Choose a start or end boundary and a valid ISO date-time with a UTC offset.");
        if (at > DateTime.UtcNow)
            return BaseResponseBuilder.BuildBadRequest("Collected times cannot be in the future.");
        if (input.Ranges == null || input.Ranges.Count == 0 || input.Ranges.Any(r => r == null || r.Id <= 0) ||
            input.Ranges.Select(r => r.Id).Distinct().Count() != input.Ranges.Count)
            return BaseResponseBuilder.BuildBadRequest("Provide each collected range exactly once.");
        var snapshots = new Dictionary<int, (DateTime? StartAt, DateTime EndAt, string? Url, string? Note)>();
        foreach (var range in input.Ranges)
        {
            var error = ValidateRange(range, out var startAt, out var endAt, out var url, out var note);
            if (error != null) return error;
            snapshots.Add(range.Id, (startAt, endAt, url, note));
        }
        var hasInheritedStart = snapshots.Values.Any(r => r.StartAt == null);
        DateTime? expectedGlobalStart = null;
        if (hasInheritedStart || input.ExpectedGlobalStartAt != null)
        {
            if (!TryParseTime(input.ExpectedGlobalStartAt, out var expected))
                return BaseResponseBuilder.BuildBadRequest("Provide the unchanged global start when resizing inherited ranges.");
            expectedGlobalStart = expected;
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        if (!await dbContext.CollectionMemoTargets.AnyAsync(t => t.Id == targetId))
            return BaseResponseBuilder.NotFound;
        var globalStart = (await EnsureSettings()).StartAt;
        if (hasInheritedStart && expectedGlobalStart != globalStart) return CoverageChanged();
        var current = await dbContext.CollectionMemoRanges.AsNoTracking().Where(r => r.TargetId == targetId).ToListAsync();
        var byId = current.ToDictionary(r => r.Id);
        foreach (var (id, snapshot) in snapshots)
        {
            if (!byId.TryGetValue(id, out var range) || range.StartAt != snapshot.StartAt || range.EndAt != snapshot.EndAt ||
                range.Url != snapshot.Url || range.Note != snapshot.Note)
                return CoverageChanged();
        }
        var components = BuildComponents(current, globalStart);
        var selectedIndex = components.FindIndex(c => c.Ranges.Any(r => r.Id == input.Ranges[0].Id));
        var selected = components[selectedIndex];
        if (selected.Ranges.Count != snapshots.Count || selected.Ranges.Any(r => !snapshots.ContainsKey(r.Id)))
            return CoverageChanged();

        var newStart = input.Edge == "start" ? at : selected.StartAt;
        var newEnd = input.Edge == "end" ? at : selected.EndAt;
        if (newEnd < newStart)
            return BaseResponseBuilder.BuildBadRequest("The end must be at or after the start.");
        if ((selectedIndex > 0 && newStart < components[selectedIndex - 1].EndAt) ||
            (selectedIndex + 1 < components.Count && newEnd > components[selectedIndex + 1].StartAt))
            return BaseResponseBuilder.BuildBadRequest("The boundary cannot cross another collected range.");

        // Preserve the untouched endpoint at full DateTime tick precision; the client need only
        // choose the moved boundary. Neighboring components remain separate stored records.
        var keepInherited = input.Edge == "end" && selected.StartAt == globalStart &&
                            selected.Ranges.Any(r => r.StartAt == null);
        if (selected.Ranges.Any(HasMetadata))
            await ResizeAnnotatedCoverage(targetId, selected, input.Edge, newStart, newEnd, globalStart);
        else
            await Consolidate(targetId, selected.Ranges, keepInherited ? null : newStart, newEnd);
        await transaction.CommitAsync();
        DetachRanges(selected.Ranges);
        return BaseResponseBuilder.Ok;
    }

    private async Task Consolidate(int targetId, List<CollectionMemoRangeDbModel> ranges, DateTime? startAt, DateTime endAt)
    {
        var survivorId = ranges.Min(r => r.Id);
        await dbContext.CollectionMemoRanges.Where(r => r.TargetId == targetId && r.Id == survivorId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.StartAt, startAt).SetProperty(r => r.EndAt, endAt));
        var removedIds = ranges.Where(r => r.Id != survivorId).Select(r => r.Id).ToList();
        if (removedIds.Count > 0)
            await dbContext.CollectionMemoRanges.Where(r => r.TargetId == targetId && removedIds.Contains(r.Id)).ExecuteDeleteAsync();
    }

    private static bool HasMetadata(CollectionMemoRangeDbModel range) => range.Url != null || range.Note != null;

    private async Task ResizeAnnotatedCoverage(int targetId, CoverageComponent component, string edge,
        DateTime startAt, DateTime endAt, DateTime globalStart)
    {
        foreach (var range in component.Ranges)
        {
            var originalStart = range.StartAt ?? globalStart;
            // Keep every source record. A record outside the resized coverage becomes
            // a boundary point so its URL and note remain editable and discoverable.
            var newStart = originalStart < startAt ? startAt : originalStart > endAt ? endAt : originalStart;
            var newEnd = range.EndAt < startAt ? startAt : range.EndAt > endAt ? endAt : range.EndAt;
            if (edge == "start" && originalStart == component.StartAt) newStart = startAt;
            if (edge == "end" && range.EndAt == component.EndAt) newEnd = endAt;
            DateTime? storedStart = range.StartAt == null && edge == "end" && newStart == globalStart
                ? null : newStart;
            await dbContext.CollectionMemoRanges.Where(r => r.TargetId == targetId && r.Id == range.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.StartAt, storedStart).SetProperty(r => r.EndAt, newEnd));
        }
    }

    private void DetachRanges(List<CollectionMemoRangeDbModel> ranges)
    {
        var ids = ranges.Select(r => r.Id).ToHashSet();
        foreach (var entry in dbContext.ChangeTracker.Entries<CollectionMemoRangeDbModel>().Where(e => ids.Contains(e.Entity.Id)).ToList())
            entry.State = EntityState.Detached;
    }

    private static BaseResponse CoverageChanged() => BaseResponseBuilder.Build(
        Bootstrap.Models.Constants.ResponseCode.Conflict, "Collected ranges changed. Refresh the timeline and try again.");

    private static List<CoverageComponent> BuildComponents(IEnumerable<CollectionMemoRangeDbModel> ranges, DateTime globalStart)
    {
        var components = new List<CoverageComponent>();
        foreach (var range in ranges.OrderBy(r => r.StartAt ?? globalStart).ThenBy(r => r.EndAt).ThenBy(r => r.Id))
        {
            var startAt = range.StartAt ?? globalStart;
            var component = components.LastOrDefault();
            if (component == null || startAt > component.EndAt)
            {
                component = new CoverageComponent {StartAt = startAt, EndAt = range.EndAt};
                components.Add(component);
            }
            component.Ranges.Add(range);
            if (range.EndAt > component.EndAt) component.EndAt = range.EndAt;
        }
        return components;
    }

    private sealed class CoverageComponent
    {
        public List<CollectionMemoRangeDbModel> Ranges { get; } = [];
        public DateTime StartAt { get; set; }
        public DateTime EndAt { get; set; }
    }

    private Task<bool> NameExists(string normalizedName, int? exceptId = null) =>
        dbContext.CollectionMemoTargets.AnyAsync(t => t.NormalizedName == normalizedName && t.Id != exceptId);

    private async Task<BaseResponse> SaveTarget(CollectionMemoTargetDbModel target)
    {
        try
        {
            await dbContext.SaveChangesAsync();
            return BaseResponseBuilder.Ok;
        }
        catch (DbUpdateException e) when (e.InnerException is SqliteException {SqliteExtendedErrorCode: 2067})
        {
            // The unique index also protects simultaneous requests that passed NameExists.
            dbContext.Entry(target).State = EntityState.Detached;
            return BaseResponseBuilder.BuildBadRequest(DuplicateNameMessage);
        }
    }

    private static BaseResponse? ValidateName(string? value, out string name, out string normalizedName)
    {
        name = value?.Trim() ?? "";
        normalizedName = name.ToUpperInvariant();
        return name.Length is 0 or > 200
            ? BaseResponseBuilder.BuildBadRequest("Collection target name must contain 1 to 200 characters.")
            : null;
    }

    private static BaseResponse? ValidateRange(CollectionMemoRangeInputModel input, out DateTime? startAt,
        out DateTime endAt, out string? url, out string? note)
    {
        startAt = null;
        endAt = default;
        url = string.IsNullOrWhiteSpace(input.Url) ? null : input.Url.Trim();
        note = string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim();
        if (url != null && (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                            !uri.IsWellFormedOriginalString() ||
                            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
                            string.IsNullOrEmpty(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo)))
            return BaseResponseBuilder.BuildBadRequest("The link must be an absolute HTTP or HTTPS URL without credentials.");
        if ((input.StartAt != null && !TryParseTime(input.StartAt, out var _)) || !TryParseTime(input.EndAt, out endAt))
            return BaseResponseBuilder.BuildBadRequest("Start and end must be valid ISO date-times with a UTC offset.");
        if (input.StartAt != null)
        {
            TryParseTime(input.StartAt, out var parsedStart);
            startAt = parsedStart;
        }
        if (endAt < startAt)
            return BaseResponseBuilder.BuildBadRequest("The end must be at or after the start.");
        if (endAt > DateTime.UtcNow)
            return BaseResponseBuilder.BuildBadRequest("Collected times cannot be in the future.");
        return null;
    }

    private static bool TryParseTime(string? value, out DateTime utcTime)
    {
        utcTime = default;
        if (value == null || value.Length > 40 || !IsoDateTime.IsMatch(value) ||
            !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            return false;
        utcTime = time.UtcDateTime;
        return true;
    }
}
