using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Models.Models.Dtos
{
    public class TagGroupDto
    {
        public const int DefaultGroupId = 0;

        public int Id { get; set; }
        public string Name { get; set; } = null!;
        public int Order { get; set; }
        public List<TagDto> Tags { get; set; } = new();
        public string? PreferredAlias { get; set; }

        public static TagGroupDto CreateDefault() => new()
        {
            Id = DefaultGroupId,
            Name = "Default"
        };
    }
}