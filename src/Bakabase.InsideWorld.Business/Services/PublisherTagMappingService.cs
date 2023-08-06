using System;
using System.Linq;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bootstrap.Components.Orm;

namespace Bakabase.InsideWorld.Business.Services
{
    public class PublisherTagMappingService : FullMemoryCacheResourceService<InsideWorldDbContext,
        PublisherTagMapping, int>
    {
        public PublisherTagMappingService(IServiceProvider serviceProvider) : base(serviceProvider)
        {
        }
    }
}