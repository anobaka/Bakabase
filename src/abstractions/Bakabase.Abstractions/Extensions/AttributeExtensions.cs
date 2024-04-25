using Bakabase.Abstractions.Exceptions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Bootstrap.Extensions;

namespace Bakabase.Abstractions.Extensions
{
    public static class AttributeExtensions
    {
        public static T GetRequiredAttribute<T>(this Type type) where T : Attribute
        {
            var attr = type.GetCustomAttribute<T>();
            return attr ??
                   throw new DevException($"No {SpecificTypeUtils<T>.Type.FullName} is set for {type.FullName}");
        }
    }
}