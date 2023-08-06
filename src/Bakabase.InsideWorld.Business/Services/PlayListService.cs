using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bootstrap.Components.Orm.Infrastructures;

namespace Bakabase.InsideWorld.Business.Services
{
    public class PlaylistService : ResourceService<InsideWorldDbContext, Playlist, int>
    {
        public PlaylistService(IServiceProvider serviceProvider) : base(serviceProvider)
        {
        }
    }
}
