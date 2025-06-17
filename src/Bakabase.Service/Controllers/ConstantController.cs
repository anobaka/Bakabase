using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.InsideWorld.Models.Constants;
using Bootstrap.Extensions;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers
{
    public class ConstantController : Bootstrap.Components.Miscellaneous.ConstantController
    {
        protected override List<Type> Types
        {
            get
            {
                var assemblies = Assembly.GetExecutingAssembly().GetReferencedAssemblies()
                    .Where(t => t.Name.Contains("Bakabase") || t.Name == "Bootstrap").Select(Assembly.Load).ToList();
                var enums = assemblies.SelectMany(t => t.GetTypes().Where(t => t.IsEnum)).ToList();
                enums.Add(SpecificTypeUtils<LogLevel>.Type);
                return enums;
            }
        }

        [SwaggerOperation(OperationId = "GetAllExtensionMediaTypes")]
        [HttpGet("extension-media-types")]
        public SingletonResponse<Dictionary<string, MediaType>> GetAllExtensionMediaTypes()
        {
            return new SingletonResponse<Dictionary<string, MediaType>>(InternalOptions.MediaTypeExtensions
                .SelectMany(t => t.Value.Select(a => (a, t.Key))).ToDictionary(t => t.a, t => t.Item2));
        }
    }
}