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
        var ranges = (await dbContext.CollectionMemoRanges.AsNoTracking().ToListAsync())
            .OrderBy(r => r.StartAt).ThenBy(r => r.EndAt).ThenBy(r => r.Id).ToLookup(r => r.TargetId);
        return targets.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ThenBy(t => t.Id)
            .Select(t => new CollectionMemoTarget
            {
                Id = t.Id,
                Name = t.Name,
                Ranges = ranges[t.Id].Select(r => new CollectionMemoRange
                {
                    Id = r.Id, StartAt = r.StartAt, EndAt = r.EndAt
                }).ToList()
            }).ToList();
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
        if (!await dbContext.CollectionMemoTargets.AnyAsync(t => t.Id == targetId))
            return BaseResponseBuilder.NotFound;
        var error = ValidateRange(input, out var startAt, out var endAt);
        if (error != null) return error;

        dbContext.CollectionMemoRanges.Add(new CollectionMemoRangeDbModel
        {
            TargetId = targetId, StartAt = startAt, EndAt = endAt
        });
        await dbContext.SaveChangesAsync();
        return BaseResponseBuilder.Ok;
    }

    public async Task<BaseResponse> UpdateRange(int targetId, int id, CollectionMemoRangeInputModel input)
    {
        var range = await dbContext.CollectionMemoRanges.SingleOrDefaultAsync(r => r.TargetId == targetId && r.Id == id);
        if (range == null) return BaseResponseBuilder.NotFound;
        var error = ValidateRange(input, out var startAt, out var endAt);
        if (error != null) return error;

        range.StartAt = startAt;
        range.EndAt = endAt;
        await dbContext.SaveChangesAsync();
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
        var error = ValidateRange(input, out var startAt, out var endAt);
        if (error != null) return error;

        // SQLite begins an immediate write transaction: no other writer can change coverage
        // between reading its connected components and committing the consolidated result.
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        if (!await dbContext.CollectionMemoTargets.AnyAsync(t => t.Id == targetId))
            return BaseResponseBuilder.NotFound;
        var components = BuildComponents(await dbContext.CollectionMemoRanges.AsNoTracking()
            .Where(r => r.TargetId == targetId).ToListAsync());
        var connected = components.Where(c => c.StartAt <= endAt && c.EndAt >= startAt).ToList();
        var selected = connected.SelectMany(c => c.Ranges).ToList();
        if (selected.Count == 0)
        {
            dbContext.CollectionMemoRanges.Add(new CollectionMemoRangeDbModel
            {
                TargetId = targetId, StartAt = startAt, EndAt = endAt
            });
            await dbContext.SaveChangesAsync();
        }
        else
        {
            startAt = connected.Min(c => c.StartAt) < startAt ? connected.Min(c => c.StartAt) : startAt;
            endAt = connected.Max(c => c.EndAt) > endAt ? connected.Max(c => c.EndAt) : endAt;
            await Consolidate(targetId, selected, startAt, endAt);
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
        var snapshots = new Dictionary<int, (DateTime StartAt, DateTime EndAt)>();
        foreach (var range in input.Ranges)
        {
            var error = ValidateRange(range, out var startAt, out var endAt);
            if (error != null) return error;
            snapshots.Add(range.Id, (startAt, endAt));
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        if (!await dbContext.CollectionMemoTargets.AnyAsync(t => t.Id == targetId))
            return BaseResponseBuilder.NotFound;
        var current = await dbContext.CollectionMemoRanges.AsNoTracking().Where(r => r.TargetId == targetId).ToListAsync();
        var byId = current.ToDictionary(r => r.Id);
        foreach (var (id, snapshot) in snapshots)
        {
            if (!byId.TryGetValue(id, out var range) || range.StartAt != snapshot.StartAt || range.EndAt != snapshot.EndAt)
                return CoverageChanged();
        }
        var components = BuildComponents(current);
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
        await Consolidate(targetId, selected.Ranges, newStart, newEnd);
        await transaction.CommitAsync();
        DetachRanges(selected.Ranges);
        return BaseResponseBuilder.Ok;
    }

    private async Task Consolidate(int targetId, List<CollectionMemoRangeDbModel> ranges, DateTime startAt, DateTime endAt)
    {
        var survivorId = ranges.Min(r => r.Id);
        await dbContext.CollectionMemoRanges.Where(r => r.TargetId == targetId && r.Id == survivorId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.StartAt, startAt).SetProperty(r => r.EndAt, endAt));
        var removedIds = ranges.Where(r => r.Id != survivorId).Select(r => r.Id).ToList();
        if (removedIds.Count > 0)
            await dbContext.CollectionMemoRanges.Where(r => r.TargetId == targetId && removedIds.Contains(r.Id)).ExecuteDeleteAsync();
    }

    private void DetachRanges(List<CollectionMemoRangeDbModel> ranges)
    {
        var ids = ranges.Select(r => r.Id).ToHashSet();
        foreach (var entry in dbContext.ChangeTracker.Entries<CollectionMemoRangeDbModel>().Where(e => ids.Contains(e.Entity.Id)).ToList())
            entry.State = EntityState.Detached;
    }

    private static BaseResponse CoverageChanged() => BaseResponseBuilder.Build(
        Bootstrap.Models.Constants.ResponseCode.Conflict, "Collected ranges changed. Refresh the timeline and try again.");

    private static List<CoverageComponent> BuildComponents(IEnumerable<CollectionMemoRangeDbModel> ranges)
    {
        var components = new List<CoverageComponent>();
        foreach (var range in ranges.OrderBy(r => r.StartAt).ThenBy(r => r.EndAt).ThenBy(r => r.Id))
        {
            var component = components.LastOrDefault();
            if (component == null || range.StartAt > component.EndAt)
            {
                component = new CoverageComponent {EndAt = range.EndAt};
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
        public DateTime StartAt => Ranges[0].StartAt;
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

    private static BaseResponse? ValidateRange(CollectionMemoRangeInputModel input, out DateTime startAt,
        out DateTime endAt)
    {
        startAt = endAt = default;
        if (!TryParseTime(input.StartAt, out startAt) || !TryParseTime(input.EndAt, out endAt))
            return BaseResponseBuilder.BuildBadRequest("Start and end must be valid ISO date-times with a UTC offset.");
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
