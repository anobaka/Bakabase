using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;

namespace Bakabase.InsideWorld.Business.Components.Enhancement.Abstractions.Models.Domain
{
    public record EnhancementTask(
        Bakabase.Abstractions.Models.Domain.Resource Resource,
        IEnhancer Enhancer,
        CategoryEnhancerOptions CategoryEnhancerOptions,
        Dictionary<int, CustomProperty> TargetPropertyMap,
        List<CustomPropertyValue>? CurrentValues);
}