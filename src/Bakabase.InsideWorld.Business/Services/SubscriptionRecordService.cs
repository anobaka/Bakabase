using System;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bootstrap.Components.Notification.Abstractions;
using Bootstrap.Components.Notification.Abstractions.Models.Constants;
using Bootstrap.Components.Notification.Abstractions.Models.RequestModels;
using Bootstrap.Components.Notification.Abstractions.Services;
using Bootstrap.Components.Orm.Infrastructures;

namespace Bakabase.InsideWorld.Business.Services
{
    public class SubscriptionRecordService : ResourceService<InsideWorldDbContext, SubscriptionRecord, int>
    {
        protected MessageService NotificationManager => GetRequiredService<MessageService>();

        public SubscriptionRecordService(IServiceProvider serviceProvider) : base(serviceProvider)
        {
        }

        public async Task NotifyIfNotAllRead()
        {
            var unreadRecordCount = await Count(t => !t.Read);
            if (unreadRecordCount > 0)
            {
                var message = new CommonMessageSendRequestModel
                {
                    Subject = $"[BxB]{unreadRecordCount} unread subscription records.",
                    Types = NotificationType.Email,
                    Content = $"{unreadRecordCount} records to read",
                    Sender = "anobaka@qq.com"
                };
                await NotificationManager.Send(message);
            }
        }
    }
}