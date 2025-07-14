using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Dependency.Abstractions;
using Bakabase.InsideWorld.Business.Components.Dependency.Models.View;

namespace Bakabase.InsideWorld.Business.Components.Dependency.Extensions
{
    public static class DependentComponentServiceExtensions
    {
        public static DependentComponentContextViewModel BuildContextDto(this IDependentComponentService service)
        {
            var ctx = service.Context;
            return new DependentComponentContextViewModel
            {
                Id = service.Id,
                Name = service.DisplayName,
                DefaultLocation = service.DefaultLocation,
                Status = service.Status,
                Description = service.Description,
                IsRequired = service.IsRequired,

                Error = ctx.Error,
                InstallationProgress = ctx.InstallationProgress,
                Location = ctx.Location,
                Version = ctx.Version
            };
        }
    }
}
