using System.Collections.Generic;
using System.Linq;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bakabase.InsideWorld.Models.Models.Entities;

namespace Bakabase.InsideWorld.Models.Extensions
{
    public static class AliasExtensions
    {
        public static AliasDto ToDto(this Alias a)
        {
            return new()
            {
                Id = a.Id,
                Name = a.Name,
                GroupId = a.GroupId
            };
        }

        public static List<AliasDto> Merge(this IEnumerable<Alias> aliases)
        {
            var groups = aliases.GroupBy(a => a.GroupId).ToList();
            var list = new List<AliasDto>();
            foreach (var g in groups)
            {
                var preferred = g.FirstOrDefault(a => a.IsPreferred) ?? g.FirstOrDefault();
                var dto = preferred.ToDto();
                dto.Candidates = g.Where(a => a != preferred).Select(a => a.ToDto()).ToList();
                list.Add(dto);
            }

            return list;
        }
    }
}