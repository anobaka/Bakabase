using System;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.ExHentai;

public sealed class ExHentaiOriginalImageSafetyException(string message) : Exception(message);

/// <summary>Conservative preflight rules; the site offers no atomic price or spending-limit API.</summary>
public static class ExHentaiOriginalImagePolicy
{
    public const long DefaultMinimumGpBalance = 10_000;
    public const long DefaultMaximumGpCostPerTask = 100_000;

    public static bool IsPubliclyFree(DateTime postedAt, DateTime utcNow)
    {
        // Deliberately stay inside the documented 3/12-month windows rather than guessing
        // whether the server interprets a month as a calendar month or a fixed day count.
        if (postedAt.Kind != DateTimeKind.Utc || utcNow.Kind != DateTimeKind.Utc || postedAt > utcNow)
            return false;
        var age = utcNow - postedAt;
        if (age < TimeSpan.FromDays(80)) return true;
        if (age >= TimeSpan.FromDays(330)) return false;
        // A 15-minute margin also prevents a request started at the boundary from crossing
        // into peak hours while it waits for the site's request queue/network.
        var peakStart = utcNow.DayOfWeek == DayOfWeek.Sunday ? 5 : 14;
        var time = utcNow.TimeOfDay;
        return time < TimeSpan.FromHours(peakStart) - TimeSpan.FromMinutes(15) ||
               time >= TimeSpan.FromHours(20) + TimeSpan.FromMinutes(15);
    }

    public static long EstimateGpReservation(long? originalSizeBytes)
    {
        if (originalSizeBytes is not > 0)
            throw new ExHentaiOriginalImageSafetyException("Original-image download stopped: the original file size could not be verified. No paid original-image request was sent.");
        // Originals cost 20 FIQ/MB; an empty FIQ bucket replenishes in 1,000 GP batches.
        // Never assume an unreadable/private FIQ bucket has free capacity. Using decimal
        // MB and reserving a whole replenishment batch for EACH request overestimates cost.
        var fiq = Math.Ceiling(originalSizeBytes.Value / 1_000_000m * 20m);
        var cost = Math.Ceiling(fiq / 1_000m) * 1_000m;
        if (cost > long.MaxValue)
            throw new ExHentaiOriginalImageSafetyException("Original-image download stopped: the estimated GP cost is too large.");
        return (long)cost;
    }

    public static void CheckBalance(long balance, long reservation, long minimumBalance)
    {
        if (minimumBalance < 0 || reservation <= 0 || balance < 0 ||
            balance < reservation || balance - reservation < minimumBalance)
            throw new ExHentaiOriginalImageSafetyException($"Original-image download stopped: {balance:N0} GP available; this request reserves {reservation:N0} GP and must retain {minimumBalance:N0} GP. No paid original-image request was sent.");
    }
}
