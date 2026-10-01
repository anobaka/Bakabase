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
