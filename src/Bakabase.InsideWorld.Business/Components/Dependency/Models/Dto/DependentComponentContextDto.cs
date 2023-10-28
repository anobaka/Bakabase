using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Dependency.Abstractions;
using Bakabase.InsideWorld.Business.Components.Dependency.Abstractions.Models.Constants;

namespace Bakabase.InsideWorld.Business.Components.Dependency.Models.Dto
{
    public record DependentComponentContextDto : DependentComponentContext
    {
        public string Id { get; set; } = null!;
        public string Name { get; set; } = null!;
        public string DefaultLocation { get; set; } = null!;
        public DependentComponentStatus Status { get; set; }
    }
}