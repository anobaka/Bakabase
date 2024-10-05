using Bakabase.Abstractions.Models.Input;
using Bootstrap.Models.RequestModels;

namespace Bakabase.Service.Models.View
{
    public record ResourceSearchViewModel : SearchRequestModel
    {
        public ResourceSearchFilterGroupViewModel? Group { get; set; }
        public ResourceSearchOrderInputModel[]? Orders { get; set; }
        public string? Keyword { get; set; }
    }
}
