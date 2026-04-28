using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Service.Models.View.Constants;
using Bootstrap.Extensions;
using Microsoft.Extensions.Logging;

namespace Bakabase.Service.Components
{
    public static class BakabaseConstantTypes
    {
        public static List<Type> GetAll()
        {
            var assemblies = typeof(BakabaseConstantTypes).Assembly.GetReferencedAssemblies()
                .Where(t => t.Name != null && (t.Name.Contains("Bakabase") || t.Name == "Bootstrap"))
                .Select(Assembly.Load)
                .ToList();
            var enums = assemblies.SelectMany(t => t.GetTypes().Where(x => x.IsEnum)).ToList();
            enums.Add(SpecificTypeUtils<LogLevel>.Type);
            enums.Add(SpecificTypeUtils<DecompressionStatus>.Type);
            enums.Add(SpecificTypeUtils<CompressedFileDetectionResultStatus>.Type);
            return enums;
        }
    }
}
