namespace Bakabase.Modules.HealthScore.Components.Predicates;

public record MediaTypeFileCountParameters
{
    public PredicateMediaType MediaType { get; set; }
    public ComparisonOperator Operator { get; set; }
    public int Count { get; set; }
}

public sealed class MediaTypeFileCountPredicate : FilePredicate<MediaTypeFileCountParameters>
{
    public const string PredicateId = "MediaTypeFileCount";
    public override string Id => PredicateId;

    protected override bool EvaluateInternal(ResourceFsSnapshot snapshot, MediaTypeFileCountParameters? p)
    {
        if (p is null) return false;

        var internalType = p.MediaType.ToInternalMediaType();
        var count = internalType.HasValue
            ? snapshot.FilesOf(internalType.Value).Count()
            : snapshot.AllFiles.Count;

        return p.Operator.Apply(count, p.Count);
    }
}
