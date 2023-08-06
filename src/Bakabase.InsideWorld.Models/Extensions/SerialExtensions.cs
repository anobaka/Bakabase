using Bakabase.InsideWorld.Models.Models.Dtos;
using Bakabase.InsideWorld.Models.Models.Entities;

namespace Bakabase.InsideWorld.Models.Extensions
{
    public static class SerialExtensions
    {
        public static SeriesDto ToDto(this Series s)
        {
            if (s == null)
            {
                return null;
            }

            return new SeriesDto
            {
                Id = s.Id,
                Name = s.Name
            };
        }
    }
}