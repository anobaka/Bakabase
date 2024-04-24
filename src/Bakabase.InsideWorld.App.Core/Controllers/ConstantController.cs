using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.InsideWorld.Models.Constants;
using Bootstrap.Extensions;
using Bootstrap.Models.ResponseModels;
using ElectronNET.API.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.InsideWorld.App.Core.Controllers
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
                enums.Add(SpecificTypeUtils<OpenDialogProperty>.Type);
                return enums;
            }
        }

        [SwaggerOperation(OperationId = "GetAllExtensionMediaTypes")]
        [HttpGet("extension-media-types")]
        public SingletonResponse<Dictionary<string, MediaType>> GetAllExtensionMediaTypes()
        {
            var map = new Dictionary<MediaType, ImmutableHashSet<string>>
            {
                {
                    MediaType.Image, InternalOptions.ImageExtensions
                },
                {
                    MediaType.Audio, InternalOptions.AudioExtensions
                },
                {
                    MediaType.Video, InternalOptions.VideoExtensions
                },
                {
                    MediaType.Text, InternalOptions.TextExtensions
                }
            };

            return new SingletonResponse<Dictionary<string, MediaType>>(map
                .SelectMany(t => t.Value.Select(a => (a, t.Key))).ToDictionary(t => t.a, t => t.Item2));
        }
    }
}