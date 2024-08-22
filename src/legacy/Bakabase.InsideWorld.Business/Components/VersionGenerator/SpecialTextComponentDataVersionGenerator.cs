using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.InsideWorld.Business.Components.Resource.Components;
using Bakabase.InsideWorld.Business.Models.Domain;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Components;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bootstrap.Extensions;

namespace Bakabase.InsideWorld.Business.Components.VersionGenerator
{
    // public class SpecialTextComponentDataVersionGenerator : DefaultComponentDataVersionGenerator
    // {
    //     public override async Task<string> GetVersionAsync(ComponentDescriptor descriptor)
    //     {
    //         var v = await base.GetVersionAsync(descriptor);
    //         return v.IsNotEmpty() ? $"{v}-{SpecialTextService.Version}" : SpecialTextService.Version;
    //     }
    // }
}