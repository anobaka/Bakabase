using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Bakabase.Service.Components.CloudDownloads.PikPakCli;

/// <summary>The official CLI exposes rounded binary sizes. Only disjoint bounds prove a
/// shortage; missing data or overlapping bounds must not masquerade as an exact quota.</summary>
internal static class PikPakCliCapacity
{
    internal sealed record Bounds(decimal Minimum, decimal Maximum);
    internal sealed record Check(DateTimeOffset CheckedAt, string Result, Bounds? Used, Bounds? Total,
        decimal? RequiredMinimum)
    {
        public bool IsInsufficient => Result == "insufficient";
    }

    internal static Bounds? Parse(string? display)
    {
        if (display == null || display.Length > 64) return null;
        var match = Regex.Match(display, @"\A([0-9]+(?:\.[0-9]+)?)\s*(B|KB|MB|GB|TB|PB)\z", RegexOptions.CultureInvariant);
        if (!match.Success || !decimal.TryParse(match.Groups[1].Value, NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var value)) return null;
        var power = Array.IndexOf(new[] {"B", "KB", "MB", "GB", "TB", "PB"}, match.Groups[2].Value);
        var digits = match.Groups[1].Value.Split('.').Skip(1).FirstOrDefault()?.Length ?? 0;
        if (digits > 12 || power == 0 && value != decimal.Truncate(value)) return null;
        try
        {
            var factor = (decimal)Math.Pow(1024, power);
            var center = checked(value * factor);
            var tolerance = power == 0 ? 0 : factor / (decimal)Math.Pow(10, digits) / 2;
            if (center + tolerance > long.MaxValue) return null;
            return new(Math.Max(0, center - tolerance), center + tolerance);
        }
        catch (OverflowException) { return null; }
    }

    internal static Check Evaluate(JsonElement status, IEnumerable<string?> fileSizes)
    {
        Bounds? used = null, total = null;
        if (status.TryGetProperty("storage", out var storage) && storage.ValueKind == JsonValueKind.Object)
        {
            used = Parse(PikPakCliJson.Text(storage, "used"));
            total = Parse(PikPakCliJson.Text(storage, "total"));
        }
        decimal? required = 0;
        foreach (var size in fileSizes)
        {
            var bounds = Parse(size);
            if (bounds == null) { required = null; break; }
            try { required = checked(required + bounds.Minimum); }
            catch (OverflowException) { required = null; break; }
        }
        var known = used != null && total is {Minimum: > 0};
        // A zero/unavailable total may mean an unsupported account type. Also do not
        // subtract trash: the provider's reported used value already defines its quota.
        var insufficient = known && used!.Minimum + (required ?? 0) > total!.Maximum;
        return new(DateTimeOffset.UtcNow, insufficient ? "insufficient" : known && required != null
            ? "notProvenInsufficient" : "unknown", used, total, required);
    }
}
