using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Db;
using Bakabase.InsideWorld.Models.Constants.Aos;
using Bakabase.InsideWorld.Models.RequestModels;
using Bootstrap.Components.Orm.Infrastructures;
using Bootstrap.Models.ResponseModels;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Services
{
    public class PasswordService : ResourceService<BakabaseDbContext, PasswordDbModel, string>
    {
        public PasswordService(IServiceProvider serviceProvider) : base(serviceProvider)
        {
        }

        public async Task<SearchResponse<PasswordDbModel>> Search(PasswordSearchRequestModel model)
        {
            Expression<Func<PasswordDbModel, object>>? orderBy = null;
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
                    p = new PasswordDbModel {Text = password};
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