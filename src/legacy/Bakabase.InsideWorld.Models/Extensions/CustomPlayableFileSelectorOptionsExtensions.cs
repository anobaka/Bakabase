using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Models.Aos;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bootstrap.Extensions;

namespace Bakabase.InsideWorld.Models.Extensions
{
    public static class CustomPlayableFileSelectorOptionsExtensions
    {
        public static CustomPlayableFileSelectorOptionsDto ToDto(this CustomPlayableFileSelectorOptions options)
        {
            if (options == null)
            {
                return null;
            }

            return new CustomPlayableFileSelectorOptionsDto
            {
                Extensions = options.ExtensionsString.IsNullOrEmpty()
                    ? Array.Empty<string>()
                    : options.ExtensionsString.Split(CustomPlayableFileSelectorOptions.ExtensionSeparator,
                        StringSplitOptions.RemoveEmptyEntries),
                Id = options.Id,
                MaxFileCount = options.MaxFileCount,
                Name = options.Name,
                ExtensionsString = options.ExtensionsString
            };
        }

        public static CustomPlayableFileSelectorOptions ToRaw(this CustomPlayableFileSelectorOptionsDto dto)
        {
            if (dto == null)
            {
                return null;
            }

            return new CustomPlayableFileSelectorOptions
            {
                Id = dto.Id,
                MaxFileCount = dto.MaxFileCount,
                Name = dto.Name,
                ExtensionsString = string.Join(CustomPlayableFileSelectorOptions.ExtensionSeparator,
                    dto.Extensions?.Select(a => $".{a.TrimStart('.')}") ?? new string[] { })
            };
        }
    }
}