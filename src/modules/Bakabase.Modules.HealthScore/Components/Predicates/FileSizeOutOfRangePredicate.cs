using Bakabase.Modules.HealthScore.Components;

namespace Bakabase.Modules.HealthScore.Components.Predicates;

public record FileSizeOutOfRangeParameters
{
    /// <summary>Inclusive lower bound. Null = no lower bound.</summary>
    public long? Min { get; set; }

    /// <summary>Inclusive upper bound. Null = no upper bound.</summary>
    public long? Max { get; set; }
}

/// <summary>
/// Matches when ANY single file in the resource has a size outside [Min, Max].
/// Default Min=1 catches 0-byte files; default Max=null doesn't cap.
/// </summary>
public sealed class FileSizeOutOfRangePredicate : FilePredicate<FileSizeOutOfRangeParameters>
{
    public const string PredicateId = "FileSizeOutOfRange";
    public override string Id => PredicateId;

    protected override bool EvaluateInternal(ResourceFsSnapshot snapshot, FileSizeOutOfRangeParameters? p)
    {
        if (p is null) return false;
        if (!p.Min.HasValue && !p.Max.HasValue) return false;

        foreach (var f in snapshot.AllFiles)
        {
            long size;
            try { size = f.Length; }
            catch { continue; }

            if (p.Min.HasValue && size < p.Min.Value) return true;
            if (p.Max.HasValue && size > p.Max.Value) return true;
        }

        return false;
    }
}
