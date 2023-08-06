using System.Collections.Generic;
using Bakabase.InsideWorld.Models.Models.Entities;

namespace Bakabase.InsideWorld.Models.Models.Dtos
{
    public record PublisherDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public List<PublisherDto> SubPublishers { get; set; } = new();
        public int Rank { get; set; } = 0;
        public bool Favorite { get; set; }
        public List<TagDto> Tags { get; set; } = new();
    }
}