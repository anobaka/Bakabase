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
    public static class PlaylistExtensions
    {
        public static PlaylistDto ToDto(this Playlist pl)
        {
            if (pl == null) return null;
            return new PlaylistDto
            {
                Items = pl.ItemsJson.IsNullOrEmpty()
                    ? new List<PlaylistItemDto>()
                    : JsonConvert.DeserializeObject<List<PlaylistItemDto>>(pl.ItemsJson),
                Id = pl.Id,
                Interval = pl.Interval,
                Name = pl.Name,
                Order = pl.Order
            };
        }

        public static Playlist ToEntity(this PlaylistDto dto)
        {
            if (dto == null) return null;
            return new Playlist
            {
                ItemsJson = JsonConvert.SerializeObject(dto.Items),
                Id = dto.Id,
                Interval = dto.Interval,
                Name = dto.Name,
                Order = dto.Order
            };
        }
    }
}