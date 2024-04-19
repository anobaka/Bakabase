using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Business.Components.Enhancement.Abstractions
{
    public class EnhancerService
    {
        public Dictionary<int, CategoryEnhancer> CategoryEnhancers { get; set; } = [];
        public async Task Enhance(Bakabase.Abstractions.Models.Domain.Resource resource, IEnhancer enhancer, object? options)
        {
            var enhancements = await enhancer.Enhance(resource);
        }
    }

    public record CategoryEnhancer
    {
        public Type Enhancer { get; set; }
        public object? Options { get; set; }
    }
}
