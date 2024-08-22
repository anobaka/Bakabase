using System.Collections.Generic;
using System.Linq;

namespace Bakabase.InsideWorld.Models.Models.Dtos
{
    public class AliasDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public List<AliasDto> Candidates { get; set; } = new();
        public int GroupId { get; set; }

        public bool Contains(string substr) => Name?.Contains(substr) == true ||
                                               Candidates?.Any(c => c.Name?.Contains(substr) == true) == true;

        public HashSet<string> AllNames =>
            new[] {Name}.Concat(Candidates?.SelectMany(b => b.AllNames) ?? new string[] { }).ToHashSet();
    }
}