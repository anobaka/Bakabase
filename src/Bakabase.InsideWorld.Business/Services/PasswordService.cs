using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Constants.Aos;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bakabase.InsideWorld.Models.RequestModels;
using Bootstrap.Components.Orm.Infrastructures;
using Bootstrap.Models.ResponseModels;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Services
{
    public class PasswordService : ResourceService<InsideWorldDbContext, Password, string>
    {
        public PasswordService(IServiceProvider serviceProvider) : base(serviceProvider)
        {
        }

        public async Task<SearchResponse<Password>> Search(PasswordSearchRequestModel model)
        {
            Expression<Func<Password, object>>? orderBy = null;
            if (model.Order.HasValue)
            {
                switch (model.Order.Value)
                {
                    case PasswordSearchOrder.Latest:
                        orderBy = p => p.LastUsedAt;
                        break;
                    case PasswordSearchOrder.Frequency:
                        orderBy = p => p.UsedTimes;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            return await base.Search(p => true, model.PageIndex, model.PageSize, orderBy);
        }

        public async Task AddUsedTimes(string password)
        {
            try
            {
                var p = await GetByKey(password);
                if (p == null)
                {
                    p = new Password {Text = password};
                    DbContext.Add(p);
                }

                p.LastUsedAt = DateTime.Now;
                p.UsedTimes++;
                await DbContext.SaveChangesAsync();
            }
            catch (Exception e)
            {
                Logger.LogError(e, $"An error occurred adding used times for password: {password}");
            }
        }
    }
}