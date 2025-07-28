using System.Collections.Generic;
using Bakabase.InsideWorld.Business.Components.PlayList.Models.Db;
using Bakabase.InsideWorld.Business.Components.PlayList.Models.Domain;
using Bakabase.InsideWorld.Business.Components.PlayList.Models.Input;
using Bakabase.InsideWorld.Business.Components.PlayList.Services;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bootstrap.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;

namespace Bakabase.InsideWorld.Business.Components.PlayList.Extensions
{
    public static class PlayListExtensions
    {
        public static Models.Domain.PlayList ToDomainModel(this PlayListAddInputModel addInputModel)
        {
            return new Models.Domain.PlayList
            {
                Name = addInputModel.Name,
            };
        }
        public static PlayListDbModel ToDbModel(this Models.Domain.PlayList domainModel)
        {
            return new PlayListDbModel
            {
                Name = domainModel.Name,
                Interval = domainModel.Interval,
                Order = domainModel.Order,
                ItemsJson = JsonConvert.SerializeObject(domainModel.Items),
                Id = domainModel.Id
            };
        }

        public static Models.Domain.PlayList ToDomainModel(this PlayListDbModel dbModel)
        {
            var items = new List<PlayListItem>();
            if (dbModel.ItemsJson.IsNotEmpty())
            {
                try
                {
                    items = JsonConvert.DeserializeObject<List<PlayListItem>>(dbModel.ItemsJson) ?? new List<PlayListItem>();
                }
                catch
                {
                    // Return empty list on deserialization error
                }
            }

            return new Models.Domain.PlayList
            {
                Id = dbModel.Id,
                Name = dbModel.Name,
                Items = items,
                Interval = dbModel.Interval,
                Order = dbModel.Order
            };
        }

        public static IServiceCollection AddPlayList(this IServiceCollection services)
        { 
            return services.AddScoped<IPlayListService, PlayListService>();
        }
    }
}