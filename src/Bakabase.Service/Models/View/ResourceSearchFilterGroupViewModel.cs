using System.Collections.Generic;
using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Service.Models.View;

public record ResourceSearchFilterGroupViewModel
{
    public SearchCombinator Combinator { get; set; }
    public List<ResourceSearchFilterGroupViewModel>? Groups { get; set; }
    public List<ResourceSearchFilterViewModel>? Filters { get; set; }
}