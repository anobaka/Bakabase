using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bakabase.InsideWorld.Models.Models.Entities;
using JetBrains.Annotations;
using Microsoft.Extensions.Localization;

namespace Bakabase.InsideWorld.Business.Components.Resource.Components
{
    public static class ComponentExtensions
    {
        // /// <summary>
        // /// 
        // /// </summary>
        // /// <param name="components"></param>
        // /// <param name="key"></param>
        // /// <returns></returns>
        // public static IComponent FindByKey(this IEnumerable<IComponent> components, string key)
        // {
        //     int? optionsId = int.TryParse(key, out var id) ? id : null;
        //     var candidates = components.Where(a => !optionsId.HasValue || id == optionsId.Value).ToArray();
        //     if (candidates.Length > 1)
        //     {
        //         throw new Exception("");
        //     }
        // }


        public static ComponentDescriptor ToInvalidDescriptor(this ComponentOptions options, IStringLocalizer localizer)
        {
            return new ComponentDescriptor
            {
                AssemblyQualifiedTypeName = options.ComponentAssemblyQualifiedTypeName,
                Message = localizer[SharedResource.TypeIsNotFound, options.ComponentAssemblyQualifiedTypeName],
                ComponentType = options.ComponentType,
                Name = options.Name,
                Description = options.Description,
                OptionsJson = options.Json,
                OptionsId = options.Id,
                Type = ComponentDescriptorType.Invalid
            };
        }

        public static ComponentDescriptor WithOptions(this ComponentDescriptor cd, ComponentOptions options,
            IStringLocalizer localizer)
        {
            if (options == null)
            {
                return cd with
                {
                    Type = ComponentDescriptorType.Invalid,
                    Message = localizer[SharedResource.Component_OptionsWithIdAreNotFound]
                };
            }
            else
            {
                var n = cd with
                {
                    OptionsId = options.Id,
                    OptionsJson = options.Json,
                    Name = options.Name ?? cd.Name,
                    Description = options.Description ?? cd.Description,
                    Type = ComponentDescriptorType.Instance
                };

                if (cd.Type != ComponentDescriptorType.Configurable)
                {
                    n.Message = localizer[SharedResource.Component_OptionsAppliedToWrongTypeDescriptor, cd.Type,
                        cd.AssemblyQualifiedTypeName];
                    n.Type = ComponentDescriptorType.Invalid;
                }

                return n;
            }
        }
    }
}