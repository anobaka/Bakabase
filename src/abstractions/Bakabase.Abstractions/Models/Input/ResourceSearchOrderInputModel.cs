using Bakabase.InsideWorld.Models.Constants.Aos;

namespace Bakabase.Abstractions.Models.Input
{
    public record ResourceSearchOrderInputModel
    {
        public ResourceSearchSortableProperty Property { get; set; }
        public bool Asc { get; set; }
    }
}