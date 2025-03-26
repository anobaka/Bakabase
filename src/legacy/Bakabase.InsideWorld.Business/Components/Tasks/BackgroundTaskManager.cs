using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.Metrics;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Infrastructures.Components.Gui;
using Bakabase.InsideWorld.Business.Components.Gui;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Aos;
using Bootstrap.Components.Logging.LogService;
using Bootstrap.Components.Logging.LogService.Services;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Components.Notification.Abstractions;
using Bootstrap.Components.Notification.Abstractions.Models.Constants;
using Bootstrap.Components.Notification.Abstractions.Models.RequestModels;
using Bootstrap.Components.Notification.Abstractions.Services;
using Bootstrap.Extensions;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Components.Tasks
{
    [Obsolete]
    public class BackgroundTaskManager
    {
        private readonly ConcurrentDictionary<string, BackgroundTask> _tasks = new();
        private readonly LogService _logService;
        private readonly IHubContext<WebGuiHub, IWebGuiClient> _uiHub;
        private readonly ILogger<BackgroundTaskManager> _logger;
        private readonly IGuiAdapter _guiAdapter;
        private readonly IServiceProvider _sp;

        public BackgroundTaskManager(LogService logService, IHubContext<WebGuiHub, IWebGuiClient> uiHub,
            ILogger<BackgroundTaskManager> logger, IGuiAdapter guiAdapter, IServiceProvider sp)
        {
            _logService = logService;
            _uiHub = uiHub;
            _logger = logger;
            _guiAdapter = guiAdapter;
            _sp = sp;
#if DEBUG
            Task.Run(async () =>
            {
                await Task.Delay(5_000);
                RunInBackground("TestTask", new CancellationTokenSource(), async (task, sp) =>
                {
                    var i = 0;
                    while (i++ < 3)
                    {
                        task.Percentage = MathUtils.GetRandom(100);
                        await Task.Delay(5000, task.Cts.Token);
                    }

                    return BaseResponseBuilder.Ok;
                }, BackgroundTaskLevel.Critical);

                // while (true)
                // {
                //     await Task.Delay(1000);
                //     await _uiHub.Clients.All.GetData(nameof(BackgroundTask), Tasks);
                // }
            });
#endif
        }

        protected IBackgroundTaskLocalizer Localizer => _sp.GetRequiredService<IBackgroundTaskLocalizer>();

        public List<BackgroundTaskDto> Tasks
        {
            get
            {
                var localizer = Localizer;
                return _tasks.Values.Select(t => t.GetInformation(localizer)).OrderByDescending(a => a.StartDt)
                    .ToList();
            }
        }

        public List<BackgroundTaskDto> GetByName(string name)
        {
            var localizer = Localizer;
            return _tasks.Values.Where(a => a.Name == name).Select(a => a.GetInformation(localizer)).ToList();
        }

        public bool IsRunningByName(string name) => GetByName(name).Any(a => a.Status == BackgroundTaskStatus.Running);

        public void StopByName(string name) => GetByName(name).ForEach(a => Stop(a.Id));

        public BackgroundTask RunInBackground(string name, CancellationTokenSource cts,
            Func<BackgroundTask, IServiceProvider, Task<BaseResponse>> func, BackgroundTaskLevel level = BackgroundTaskLevel.Default,
            Func<int, Task> onProgressChange = null, Func<Task> onComplete = null,
            Func<BackgroundTask, Task> onFail = null)
        {
            var t = new BackgroundTask(name, cts, level, onProgressChange, onComplete, onFail);
            t.OnChange += async () =>
            {
                await OnTaskChange(t);
                RefreshTrayOnProgressChange();
            };
            t.OnStatusChange += async () => { RefreshTrayOnStatusChange(); };

            _clearCompletedTask(name);
            _tasks[t.Id] = t;
            RefreshTrayOnStatusChange();
            Task.Run(async () =>
            {
                try
                {
                    await OnTaskChange(t);
                    // await _messageService.Send(new CommonMessageSendRequestModel
                    // {
                    //     Subject = nameof(BackgroundTask),
                    //     Content = $"{name} is starting",
                    //     Types = NotificationType.Os
                    // });
                    await _logService.Log($"{nameof(BackgroundTaskManager)}:Task:{name}", LogLevel.Information, "Start",
                        "Starting");
                    var r = await func(t, _sp);
                    if (r.Code == 0)
                    {
                        t.Status = BackgroundTaskStatus.Complete;
                    }
                    else
                    {
                        t.Message = r.Message;
                        t.Status = BackgroundTaskStatus.Failed;
                    }
                }
                catch (Exception e)
                {
                    t.Message = e.BuildFullInformationText();
                    t.Status = BackgroundTaskStatus.Failed;
                    _logger.LogError(e.BuildFullInformationText());
                }

                var logLevel = t.Status switch
                {
                    BackgroundTaskStatus.Complete => LogLevel.Information,
                    BackgroundTaskStatus.Failed => LogLevel.Error,
                    _ => throw new ArgumentOutOfRangeException()
                };

                if (t.Status == BackgroundTaskStatus.Complete)
                {
                    t.Percentage = 100;
                }

                if (t.Status is BackgroundTaskStatus.Failed or BackgroundTaskStatus.Complete)
                {
                    // await _messageService.Send(new CommonMessageSendRequestModel
                    // {
                    //     Subject = nameof(BackgroundTask),
                    //     Content = $"{name} is {t.Status}, please check it in app.",
                    //     Types = NotificationType.Os
                    // });
                }

                await _logService.Log($"{nameof(BackgroundTaskManager)}:Task:{name}", logLevel, t.Status, t.Message);
                await OnTaskChange(t);
            });
            return t;
        }

        private async Task OnTaskChange(BackgroundTask t)
        {
            await _uiHub.Clients.All.GetIncrementalData(nameof(BackgroundTask), t.GetInformation(Localizer));
        }

        public void Stop(string id)
        {
            if (_tasks.TryGetValue(id, out var r))
            {
                r.Message += "Stopped by user";
                r.Cts.Cancel();
            }
        }

        private void _clearCompletedTask(string name)
        {
            var tasks = _tasks.Values.Where(x =>
                x.Name == name &&
                (x.Status == BackgroundTaskStatus.Complete || x.Status == BackgroundTaskStatus.Failed)).ToList();
            var taskIds = tasks.Select(t => t.Id).ToList();
            var changed = false;
            foreach (var id in taskIds)
            {
                changed |= _tasks.TryRemove(id, out _);
            }

            if (changed)
            {
                _uiHub.Clients.All.GetData(nameof(BackgroundTask), Tasks);
            }
        }

        public void Remove(string id)
        {
            var task = _tasks[id];
            if (task.Status == BackgroundTaskStatus.Running)
            {
                throw new Exception($"Running task can not be removed");
            }

            _tasks.Remove(id, out _);
            task.Dispose();
            _uiHub.Clients.All.GetData(nameof(BackgroundTask), Tasks);
        }

        private void RefreshTrayOnProgressChange()
        {
            var runningTasks = _tasks.Where(t => t.Value.Status == BackgroundTaskStatus.Running).ToArray();
            if (runningTasks.Length > 0)
            {
                var sb = new StringBuilder("Running tasks:");
                foreach (var t in runningTasks)
                {
                    sb.Append(Environment.NewLine).Append($"{t.Value.Name} - {t.Value.Percentage}%");
                }

                _guiAdapter.SetTrayText(sb.ToString());
            }
            else
            {
                _guiAdapter.SetTrayText($"Inside World - v{AppService.CoreVersion}");
            }
        }

        private void RefreshTrayOnStatusChange()
        {
            var filename = _tasks.Any(t => t.Value.Status == BackgroundTaskStatus.Running)
                ? "tray-running"
                : "favicon";
            var icon = $"Assets/{filename}.ico";
            _guiAdapter.SetTrayIcon(new Icon(icon));
        }

        public void ClearInactive()
        {
            var doneTasks = _tasks.Values.Where(a => a.Status != BackgroundTaskStatus.Running);
            foreach (var t in doneTasks)
            {
                _tasks.Remove(t.Id, out _);
                t.Dispose();
            }

            _uiHub.Clients.All.GetData(nameof(BackgroundTask), Tasks);
        }
    }
}