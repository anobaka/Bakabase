using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bakabase.InsideWorld.Models.ResponseModels;

namespace Bakabase.InsideWorld.Business.Components.Subscribers
{
    public interface ISubscriber
    {
        SubscriptionType Type { get; }
        SubscriberStatus Status { get; }
        Task Start(Dictionary<SubscriptionDto, SubscriptionProgress> subscriptions, CancellationToken ct);
        Task Stop();
    }
}