using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Property.Models.Db;

public record ResourceSearchFilterGroupDbModel
{
    public SearchCombinator Combinator { get; set; }
    public List<ResourceSearchFilterGroupDbModel>? Groups { get; set; }
    public List<ResourceSearchFilterDbModel>? Filters { get; set; }
}