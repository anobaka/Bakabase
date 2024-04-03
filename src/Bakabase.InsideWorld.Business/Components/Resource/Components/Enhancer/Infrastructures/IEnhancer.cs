using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Models.Dtos;

namespace Bakabase.InsideWorld.Business.Components.Resource.Components.Enhancer.Infrastructures
{
    public interface IEnhancer : IComponent
    {
        Task<Enhancement[]> Enhance(Models.Domain.Resource resource);
    }
}