using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.InsideWorld.Business.Models.Domain;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bootstrap.Components.Cryptography;
using Bootstrap.Extensions;

namespace Bakabase.InsideWorld.Business.Components.Resource.Components
{
    public class DefaultComponentDataVersionGenerator : IComponentDataVersionGenerator
    {
        public virtual Task<string> GetVersionAsync(ComponentDescriptor descriptor)
        {
            return Task.FromResult(descriptor.OptionsJson.IsNullOrEmpty()
                ? null
                : CryptographyUtils.Md5(descriptor.OptionsJson)[..6]);
        }

        public static readonly DefaultComponentDataVersionGenerator Instance = new();
    }
}