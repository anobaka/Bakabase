using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bootstrap.Components.Orm.Infrastructures;

namespace Bakabase.InsideWorld.Business.Services
{
    public class SubscriptionProgressService : ResourceService<InsideWorldDbContext, SubscriptionProgress, int>
    {
        public SubscriptionProgressService(IServiceProvider serviceProvider) : base(serviceProvider)
        {
        }

        public async Task<List<SubscriptionProgress>> Update(int subscriptionId,
            Dictionary<SubscriptionType, DateTime?> typeAndStartDts)
        {
            var existed = await GetAll(a => a.SubscriptionId == subscriptionId);
            var toBeDeleted = existed.Where(a => !typeAndStartDts.ContainsKey(a.Type)).ToList();
            var existedTypes = existed.Select(a => a.Type).ToList();
            var toBeAdded = typeAndStartDts.Where(a => !existedTypes.Contains(a.Key)).Select(a =>
                new SubscriptionProgress
                {
                    SubscriptionId = subscriptionId,
                    Type = a.Key,
                    LatestSyncDt = a.Value ?? DateTime.MinValue
                }).ToList();
            await RemoveRange(toBeDeleted);
            var toBeUpdated = existed.Where(a => typeAndStartDts.ContainsKey(a.Type)).ToList();
            var toBeUpdatedCount = toBeUpdated.Where(t =>
            {
                var dt = typeAndStartDts[t.Type];
                if (dt.HasValue && dt.Value != t.LatestSyncDt)
                {
                    t.LatestSyncDt = dt.Value;
                    return true;
                }

                return false;
            }).Count();
            if (toBeUpdatedCount > 0)
            {
                await UpdateRange(toBeUpdated);
            }

            var @new = await AddRange(toBeAdded);
            existed.RemoveAll(a => toBeDeleted.Contains(a));
            return @new.Data.Concat(existed).ToList();
        }
    }
}