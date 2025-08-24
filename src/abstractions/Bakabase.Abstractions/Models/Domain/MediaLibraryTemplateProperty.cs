using Bakabase.Abstractions.Components;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bootstrap.Components.Cryptography;

namespace Bakabase.Abstractions.Models.Domain;

public record MediaLibraryTemplateProperty : ISyncVersion
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
    public List<PathPropertyExtractor>? ValueLocators { get; set; }

    public string? GetSyncVersion()
    {
        if (ValueLocators != null)
        {
            var keys = new List<string>
            {
                Pool.ToString(),
                Id.ToString(),
            };
            keys.AddRange(ValueLocators.Select(v => v.GetSyncVersion()));
            return CryptographyUtils.Md5(string.Join('-', keys));

        }

        return null;
    }
}