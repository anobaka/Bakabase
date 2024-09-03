using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Component;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.InsideWorld.Business.Models.Domain;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bootstrap.Extensions;
using EnhancerDescriptor = Bakabase.InsideWorld.Business.Models.Domain.EnhancerDescriptor;

namespace Bakabase.InsideWorld.Business.Components.Resource.Components.Enhancer.Infrastructures
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public class EnhancerAttribute : ComponentAttribute
    {
        public string[] Targets { get; set; }

        public EnhancerAttribute()
        {
            Targets = Array.Empty<string>();
        }

        public EnhancerAttribute(object[] propertyKeys, object[] fileKeys)
        {
            var ts = new List<string>();
            if (propertyKeys != null)
            {
                ts.AddRange(propertyKeys.Select(t => $"p:{t}"));
            }

            if (fileKeys != null)
            {
                ts.AddRange(fileKeys.Select(t => $"f:{t}"));
            }

            Targets = ts.ToArray();
        }

        public override ComponentType Type => ComponentType.Enhancer;

        public override ComponentDescriptor ToDescriptor(Type type)
        {
            var d = base.ToDescriptor(type);
            var ed = new EnhancerDescriptor()
            {
                Targets = Targets,
                Name = d.Name,
                AssemblyQualifiedTypeName = d.AssemblyQualifiedTypeName,
                Description = d.Description,
                ComponentType = d.ComponentType,
                OptionsId = d.OptionsId,
                OptionsType = d.OptionsType,
                Message = d.Message,
                OptionsJson = d.OptionsJson,
                Type = d.Type,
                Version = d.Version,
                DataVersion = d.DataVersion
            };

            return ed;
        }
    }
}