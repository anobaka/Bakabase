using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bootstrap.Extensions;
using Newtonsoft.Json;

namespace Bakabase.InsideWorld.Models.Extensions
{
    public static class CustomPlayerOptionsExtensions
    {
        public static CustomPlayerOptionsDto ToDto(this CustomPlayerOptions options)
        {
            if (options == null)
            {
                return null;
            }

            return new CustomPlayerOptionsDto
            {
                Id = options.Id,
                Name = options.Name,
                Executable = options.Executable,
                CommandTemplate = options.CommandTemplate,
                SubCustomPlayerOptionsList = options.SubCustomPlayerOptionsListJson.IsNullOrEmpty()
                    ? new()
                    : JsonConvert.DeserializeObject<List<SubCustomPlayerOptionsDto>>(options
                        .SubCustomPlayerOptionsListJson)
            };
        }
        public static CustomPlayerOptions ToEntity(this CustomPlayerOptionsDto options)
        {
            if (options == null)
            {
                return null;
            }

            return new CustomPlayerOptions
            {
                Id = options.Id,
                Name = options.Name,
                Executable = options.Executable,
                CommandTemplate = options.CommandTemplate,
                SubCustomPlayerOptionsListJson = options.SubCustomPlayerOptionsList?.Any() == true
                    ? JsonConvert.SerializeObject(options.SubCustomPlayerOptionsList)
                    : null
            };
        }
    }
}