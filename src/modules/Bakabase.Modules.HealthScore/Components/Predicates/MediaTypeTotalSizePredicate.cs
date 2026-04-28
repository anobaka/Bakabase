namespace Bakabase.Modules.HealthScore.Components.Predicates;

public record MediaTypeTotalSizeParameters
{
    public PredicateMediaType MediaType { get; set; }
    public ComparisonOperator Operator { get; set; }
    public long Bytes { get; set; }
}

public sealed class MediaTypeTotalSizePredicate : FilePredicate<MediaTypeTotalSizeParameters>
{
    public const string PredicateId = "MediaTypeTotalSize";
    public override string Id => PredicateId;

    protected override bool EvaluateInternal(ResourceFsSnapshot snapshot, MediaTypeTotalSizeParameters? p)
    {
        if (p is null) return false;

        var internalType = p.MediaType.ToInternalMediaType();
        var total = internalType.HasValue
            ? snapshot.TotalBytesOf(internalType.Value)
            : snapshot.TotalBytes;

        return p.Operator.Apply(total, p.Bytes);
    }
}
