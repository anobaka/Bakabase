using System.Collections.Generic;
using Bakabase.InsideWorld.Business.Components.Dependency.Abstractions;
using Bakabase.InsideWorld.Business.Components.Dependency.Abstractions.Models.Constants;
using Bootstrap.Components.Doc.Swagger;

namespace Bakabase.InsideWorld.Business.Components.Dependency.Models.View
{
    [SwaggerCustomModel]
    public record DependentComponentContextViewModel : DependentComponentContext
    {
        public string Id { get; set; } = null!;
        public string Name { get; set; } = null!;
        public string DefaultLocation { get; set; } = null!;
        public DependentComponentStatus Status { get; set; }
        public bool IsAvailableOnCurrentPlatform { get; set; }
        public bool CanAutoInstallOnCurrentPlatform { get; set; }
        public IReadOnlyList<DependentComponentTarget> SupportedTargets { get; set; } = [];
        public IReadOnlyList<string> DependencyIds { get; set; } = [];
        public string CurrentOs { get; set; } = null!;
        public string CurrentArchitecture { get; set; } = null!;
        public string? CurrentRuntime { get; set; }
        public string? Description { get; set; }
    }
}
