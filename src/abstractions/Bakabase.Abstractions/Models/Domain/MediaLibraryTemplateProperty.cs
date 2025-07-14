using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Models.Domain;

public record MediaLibraryTemplateProperty
{
    private PropertyPool _pool;
    private int _id;

    public PropertyPool Pool
    {
        get => Property?.Pool ?? _pool;
        set => _pool = value;
    }

    public int Id
    {
        get => Property?.Id ?? _id;
        set => _id = value;
    }

    public Property? Property { get; set; }
    public List<PathPropertyLocator>? ValueLocators { get; set; }
}