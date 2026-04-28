namespace Bakabase.Modules.HealthScore.Components;

public interface IFilePredicateRegistry
{
    IFilePredicate? Find(string id);
    IReadOnlyList<IFilePredicate> All { get; }
}

internal sealed class FilePredicateRegistry : IFilePredicateRegistry
{
    private readonly Dictionary<string, IFilePredicate> _byId;

    public FilePredicateRegistry(IEnumerable<IFilePredicate> predicates)
    {
        _byId = predicates.ToDictionary(p => p.Id, p => p, StringComparer.OrdinalIgnoreCase);
    }

    public IFilePredicate? Find(string id) => _byId.GetValueOrDefault(id);

    public IReadOnlyList<IFilePredicate> All => _byId.Values.ToList();
}
