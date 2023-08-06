using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.Storage;
using Bakabase.Infrastructures.Components.Storage.Services;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Business.Services.Bootstraps;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bakabase.InsideWorld.Models.ResponseModels;
using Bootstrap.Components.Notification.Abstractions;
using Bootstrap.Components.Notification.Abstractions.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Components.Subscribers
{
    public abstract class AbstractSubscriber<TPost> : ISubscriber
    {
        public abstract SubscriptionType Type { get; }
        public SubscriberStatus Status { get; protected set; } = new SubscriberStatus();
        public CancellationTokenSource Cts { get; set; }
        private readonly IServiceProvider _serviceProvider;
        protected SubscriptionService SubscriptionService => _serviceProvider.GetRequiredService<SubscriptionService>();
        protected MessageService NotificationManager => _serviceProvider.GetRequiredService<MessageService>();

        protected SubscriptionProgressService SubscriptionProgressService =>
            _serviceProvider.GetRequiredService<SubscriptionProgressService>();

        protected FileService FileService => _serviceProvider.GetRequiredService<FileService>();

        protected SystemPropertyService SystemPropertyService =>
            _serviceProvider.GetRequiredService<SystemPropertyService>();

        protected SubscriptionRecordService SubscriptionDataService =>
            _serviceProvider.GetRequiredService<SubscriptionRecordService>();

        protected virtual bool LocalizeCover { get; } = false;

        protected virtual Task<byte[]> DownloadCover(string url)
        {
            throw new NotImplementedException();
        }

        protected ILogger Logger;


        protected AbstractSubscriber(IServiceProvider serviceProvider, ILoggerFactory loggerFactory)
        {
            _serviceProvider = serviceProvider;
            Logger = loggerFactory.CreateLogger(GetType());
        }

        protected virtual Task Init(CancellationToken ct)
        {
            return Task.CompletedTask;
        }

        protected abstract Task<List<SubscriptionRecord>> GetRecords(SubscriptionDto subscription, int page,
            CancellationToken ct);

        public async Task Start(Dictionary<SubscriptionDto, SubscriptionProgress> subscriptions,
            CancellationToken externalCt)
        {
            if (Status?.Subscribing != true && subscriptions.Any())
            {
                Cts?.Cancel();
                Cts = new CancellationTokenSource();
                Status = new SubscriberStatus
                {
                    Subscribing = true,
                    Harvest = subscriptions.ToDictionary(t => t.Key.Keyword, t => 0),
                    ErrorMessage = null
                };
                var ct = CancellationTokenSource.CreateLinkedTokenSource(Cts.Token, externalCt).Token;
                try
                {
                    await Init(ct);
                    foreach (var (subscription, progress) in subscriptions)
                    {
                        var page = 1;
                        while (true)
                        {
                            var data = await GetRecords(subscription, page, ct);

                            data.RemoveAll(a => a.PublishDt <= progress.LatestSyncDt);
                            data.ForEach(sd =>
                            {
                                sd.SubscriptionId = subscription.Id;
                                sd.Type = Type;
                            });

                            if (data.Any())
                            {
                                Status.Harvest[subscription.Keyword] += data.Count;
                                page++;
                                var urls = data.Select(t => t.Url).ToList();
                                var existedSubscriptionData =
                                    await SubscriptionDataService.GetAll(t => urls.Contains(t.Url));
                                var existedUrls = existedSubscriptionData.Select(t => t.Url).ToHashSet();
                                var newData = (await SubscriptionDataService.AddRange(data
                                    .Where(t => !existedUrls.Contains(t.Url))
                                    .ToList())).Data;
                                if (LocalizeCover)
                                {
                                    var allData = newData.Concat(existedSubscriptionData).ToList();
                                    var networkCoverData = allData.Where(a =>
                                        a.CoverUrl.StartsWith("http") || a.CoverUrl.StartsWith("//")).ToList();
                                    if (networkCoverData.Any())
                                    {
                                        var (physicalDir, requestPath) = FileService.GetWebTempDirectory(
                                            "subscription",
                                            Type.ToString().ToLower(),
                                            true);
                                        const int concurrentCount = 10;
                                        var semaphore = new SemaphoreSlim(concurrentCount, concurrentCount);
                                        var exceptionCts = new CancellationTokenSource();
                                        var mixedCt =
                                            CancellationTokenSource.CreateLinkedTokenSource(exceptionCts.Token, ct)
                                                .Token;
                                        Exception exp = null;
                                        foreach (var d in networkCoverData)
                                        {
                                            var filename = $"{d.Id}-{Path.GetFileName(d.CoverUrl)}";
                                            var fullname = Path.Combine(physicalDir, filename);
                                            if (!File.Exists(fullname))
                                            {
                                                try
                                                {
                                                    await semaphore.WaitAsync(mixedCt);
                                                }
                                                catch (Exception e)
                                                {
                                                    if (exceptionCts.IsCancellationRequested)
                                                    {
                                                        throw exp;
                                                    }

                                                    throw;
                                                }

                                                Task.Run(async () =>
                                                {
                                                    try
                                                    {
                                                        var bytes = await DownloadCover(d.CoverUrl);
                                                        await File.WriteAllBytesAsync(fullname, bytes, mixedCt);
                                                        d.CoverUrl = $"{requestPath}/{filename}";
                                                    }
                                                    catch (Exception e)
                                                    {
                                                        exp = e;
                                                        exceptionCts.Cancel();
                                                    }
                                                    finally
                                                    {
                                                        semaphore.Release();
                                                    }
                                                });
                                            }
                                            else
                                            {
                                                d.CoverUrl = $"{requestPath}/{filename}";
                                            }
                                        }

                                        while (semaphore.CurrentCount < concurrentCount)
                                        {
                                            if (exp != null)
                                            {
                                                throw exp;
                                            }

                                            await Task.Delay(1, ct);
                                        }

                                        await SubscriptionDataService.UpdateRange(networkCoverData);
                                    }
                                }
                            }
                            else
                            {
                                await SubscriptionProgressService.UpdateByKey(progress.Id,
                                    s => s.LatestSyncDt = DateTime.Now);
                                break;
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    Cts.Cancel();
                    Logger.LogError(e, e.Message);
                    Status.ErrorMessage = e.Message;
                }
                finally
                {
                    Status.Subscribing = false;
                }
            }
        }

        public Task Stop()
        {
            Cts.Cancel();
            Status.Subscribing = false;
            return Task.CompletedTask;
        }
    }
}