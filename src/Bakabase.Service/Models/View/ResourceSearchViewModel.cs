using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Input;
using Bootstrap.Models.RequestModels;

namespace Bakabase.Service.Models.View
{
    public record ResourceSearchViewModel
    {
        public ResourceSearchFilterGroupViewModel? Group { get; set; }
        public ResourceSearchOrderInputModel[]? Orders { get; set; }
        public string? Keyword { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public ResourceTag[]? Tags { get; set; }
    }
}
