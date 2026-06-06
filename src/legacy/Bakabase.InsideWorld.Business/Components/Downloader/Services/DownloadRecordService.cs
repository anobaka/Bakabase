using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Downloader.Models.Db;
using Bakabase.InsideWorld.Models.Constants;
using Bootstrap.Components.Orm;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Services
{
    /// <summary>
    /// Maintains the permanent "this item has been downloaded" history, keyed by
    /// (<see cref="ThirdPartyId"/>, Key). Records survive deletion of download tasks.
    /// </summary>
    public class DownloadRecordService(
        FullMemoryCacheResourceService<BakabaseDbContext, DownloadRecordDbModel, int> orm)
    {
        /// <summary>
        /// Upsert a download record. Called when a download task completes successfully.
        /// </summary>
        public async Task Record(ThirdPartyId thirdPartyId, string? key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            var existing = await orm.GetFirstOrDefault(x => x.ThirdPartyId == thirdPartyId && x.Key == key);
            if (existing != null)
            {
                existing.DownloadedAt = DateTime.Now;
                await orm.Update(existing);
            }
            else
            {
                await orm.Add(new DownloadRecordDbModel
                {
                    ThirdPartyId = thirdPartyId,
                    Key = key,
                    DownloadedAt = DateTime.Now
                });
            }
        }

        /// <summary>
        /// Return the records matching any of <paramref name="keys"/> within a third party.
        /// </summary>
        public async Task<List<DownloadRecordDbModel>> Query(ThirdPartyId thirdPartyId, IReadOnlyCollection<string> keys)
        {
            if (keys == null || keys.Count == 0)
            {
                return [];
            }

            var keySet = keys.ToHashSet();
            return await orm.GetAll(x => x.ThirdPartyId == thirdPartyId && keySet.Contains(x.Key));
        }
    }
}
