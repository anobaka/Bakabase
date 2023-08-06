using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Subscribers;
using Bakabase.InsideWorld.Business.Components.Tasks;
using Bakabase.InsideWorld.Business.Services.Bootstraps;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Extensions;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bakabase.InsideWorld.Models.RequestModels;
using Bakabase.InsideWorld.Models.ResponseModels;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Components.Notification.Abstractions;
using Bootstrap.Components.Notification.Abstractions.Models.Constants;
using Bootstrap.Components.Notification.Abstractions.Models.RequestModels;
using Bootstrap.Components.Notification.Abstractions.Services;
using Bootstrap.Components.Orm.Infrastructures;
using Bootstrap.Extensions;
using Bootstrap.Models.Constants;
using Bootstrap.Models.ResponseModels;

namespace Bakabase.InsideWorld.Business.Services
{
    public class SubscriptionService : ResourceService<InsideWorldDbContext, Subscription, int>
    {
        private Dictionary<SubscriptionType, ISubscriber> Subscribers =>
            GetRequiredService<IEnumerable<ISubscriber>>().ToDictionary(t => t.Type, t => t);

        protected SubscriptionRecordService SubscriptionRecordService =>
            GetRequiredService<SubscriptionRecordService>();

        protected SubscriptionProgressService SubscriptionProgressService =>
            GetRequiredService<SubscriptionProgressService>();

        protected MessageService NotificationManager => GetRequiredService<MessageService>();
        protected BackgroundTaskManager BackgroundTaskManager => GetRequiredService<BackgroundTaskManager>();
        protected SystemPropertyService SystemPropertyService => GetRequiredService<SystemPropertyService>();

        private SubscriptionSyncStatus _status = new();

        public SubscriptionService(IServiceProvider serviceProvider) : base(serviceProvider)
        {
        }

        public async Task<ListResponse<Subscription>> Add(SubscriptionAddOrUpdateRequestModel model)
        {
            var subs = model.Keyword.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(a => new Subscription
            {
                Keyword = model.Keyword
            });
            var rsp = await AddRange(subs);
            foreach (var sub in rsp.Data)
            {
                await SubscriptionProgressService.Update(sub.Id, model.TypeAndStartDts);
            }

            return rsp;
        }

        public async Task<List<SubscriptionDto>> GetAll()
        {
            var subscriptions = await base.GetAll();
            var progresses = (await SubscriptionProgressService.GetAll()).GroupBy(a => a.SubscriptionId)
                .ToDictionary(a => a.Key, a => a.ToList());
            var ids = subscriptions.Select(a => a.Id).ToList();
            var records = await SubscriptionRecordService.GetAll(a => ids.Contains(a.SubscriptionId));
            return (subscriptions).Select(a => a.ToDto(progresses.GetValueSafely(a.Id), records)).ToList();
        }

        public async Task<BaseResponse> Update(int id, SubscriptionAddOrUpdateRequestModel model)
        {
            var sub = await GetByKey(id);
            if (model.Keyword.IsNotEmpty())
            {
                sub.Keyword = model.Keyword;
            }

            if (model.TypeAndStartDts?.Any() == true)
            {
                await SubscriptionProgressService.Update(id, model.TypeAndStartDts);
            }

            await Update(sub);
            return BaseResponseBuilder.Ok;
        }

        private async Task<BaseResponse> SyncCore(List<SubscriptionDto> allSubscriptions, DateTime? earliestDt, CancellationToken ct)
        {
            _status = new SubscriptionSyncStatus();
            var targetSubscriptions =
                new Dictionary<SubscriptionType, Dictionary<SubscriptionDto, SubscriptionProgress>>();
            foreach (var s in allSubscriptions)
            {
                var targetProgresses = s.Progresses
                    .Where(a => !earliestDt.HasValue || a.LatestSyncDt < earliestDt.Value).ToList();
                if (targetProgresses.Any())
                {
                    foreach (var p in targetProgresses)
                    {
                        if (!targetSubscriptions.TryGetValue(p.Type, out var subscriptions))
                        {
                            subscriptions = targetSubscriptions[p.Type] =
                                new Dictionary<SubscriptionDto, SubscriptionProgress>();
                        }

                        subscriptions[s] = p;
                    }
                }
            }

            var tasks = new List<Task>();
            foreach (var (type, ss) in targetSubscriptions)
            {
                var subscriber = Subscribers[type];
                tasks.Add(subscriber.Start(ss, ct));
            }

            var message = new CommonMessageSendRequestModel
            {
                Types = NotificationType.Os,
                Subject = "Subscription"
            };
            try
            {
                await Task.WhenAll(tasks);
                message.Content = "StartSync is done";
                return BaseResponseBuilder.Ok;
            }
            catch (Exception e)
            {
                message.Content = $"StartSync failed: {e.Message}";
                return BaseResponseBuilder.Build(ResponseCode.SystemError, e.BuildFullInformationText());
            }
            finally
            {
                await NotificationManager.Send(message);
                await SubscriptionRecordService.NotifyIfNotAllRead();
            }
        }

        public async Task<BaseResponse> StartSync(DateTime? earliestDt)
        {
            if (_status?.Syncing == true)
            {
                return BaseResponseBuilder.BuildBadRequest("Already syncing");
            }

            if (!Subscribers.Any())
            {
                return BaseResponseBuilder.BuildBadRequest("There is no subscriber registered");
            }

            var allSubscriptions = await GetAll();
            if (!allSubscriptions.Any())
            {
                return BaseResponseBuilder.Build(ResponseCode.Success, "All subscriptions are up-to-date");
            }

            var cts = new CancellationTokenSource();
            BackgroundTaskManager.RunInBackground("Sync Subscriptions", cts,
                async task => await SyncCore(allSubscriptions, earliestDt, cts.Token));
            return BaseResponseBuilder.Build(ResponseCode.Success);
        }

        public async Task<SubscriptionSyncStatus> GetSyncStatus()
        {
            _status.Status = Subscribers.ToDictionary(t => t.Key, t => t.Value?.Status);
            return _status;
        }

        public async Task StopSyncing()
        {
            foreach (var (_, subscriber) in Subscribers)
            {
                await subscriber.Stop();
            }
        }
    }
}