using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Resource.Components;

namespace Bakabase.InsideWorld.Business.Components.Enhancement.Abstractions;

public interface IEnhancer
{
    EnhancerId Id { get; }
    Task<List<Enhancement>?> Enhance(Bakabase.Abstractions.Models.Domain.Resource resource, object? options);
}
