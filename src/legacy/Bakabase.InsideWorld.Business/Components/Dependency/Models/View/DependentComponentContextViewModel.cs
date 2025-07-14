using Bakabase.InsideWorld.Business.Components.Dependency.Abstractions;
using Bakabase.InsideWorld.Business.Components.Dependency.Abstractions.Models.Constants;

namespace Bakabase.InsideWorld.Business.Components.Dependency.Models.View
{
    public record DependentComponentContextViewModel : DependentComponentContext
    {
        public string Id { get; set; } = null!;
        public string Name { get; set; } = null!;
        public string DefaultLocation { get; set; } = null!;
        public DependentComponentStatus Status { get; set; }
        public string? Description { get; set; }
    }
}