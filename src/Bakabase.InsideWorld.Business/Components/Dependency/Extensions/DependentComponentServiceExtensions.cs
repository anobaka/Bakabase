using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Dependency.Abstractions;
using Bakabase.InsideWorld.Business.Components.Dependency.Models.Dto;

namespace Bakabase.InsideWorld.Business.Components.Dependency.Extensions
{
    public static class DependentComponentServiceExtensions
    {
        public static DependentComponentContextDto BuildContextDto(this IDependentComponentService service)
        {
            var ctx = service.Context;
            return new DependentComponentContextDto
            {
                Id = service.Id,
                Name = service.DisplayName,
                DefaultLocation = service.DefaultLocation,
                Status = service.Status,

                Error = ctx.Error,
                InstallationProgress = ctx.InstallationProgress,
                Location = ctx.Location,
                Version = ctx.Version
            };
        }
    }
}
