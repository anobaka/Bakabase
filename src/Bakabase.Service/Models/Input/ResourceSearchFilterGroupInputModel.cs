using System.Collections.Generic;
using Bakabase.Abstractions.Components.Search;
using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Service.Models.Input;

public record
    ResourceSearchFilterGroupInputModel : IFilterExtractable<ResourceSearchFilterGroupInputModel,
    ResourceSearchFilterInputModel>
{
    public SearchCombinator Combinator { get; set; }
    public List<ResourceSearchFilterGroupInputModel>? Groups { get; set; }
    public List<ResourceSearchFilterInputModel>? Filters { get; set; }
}