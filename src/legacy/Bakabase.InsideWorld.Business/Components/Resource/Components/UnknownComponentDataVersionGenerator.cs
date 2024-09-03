using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Component;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.InsideWorld.Business.Models.Domain;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bootstrap.Components.Cryptography;
using Bootstrap.Extensions;

namespace Bakabase.InsideWorld.Business.Components.Resource.Components
{
    public class UnknownComponentDataVersionGenerator : IComponentDataVersionGenerator
    {
        public virtual Task<string> GetVersionAsync(ComponentDescriptor descriptor)
        {
            return Task.FromResult("unknown");
        }

        public static readonly UnknownComponentDataVersionGenerator Instance = new();
    }
}