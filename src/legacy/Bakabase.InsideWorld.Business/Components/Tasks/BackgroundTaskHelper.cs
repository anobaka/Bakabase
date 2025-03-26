using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Resource.Components.Enhancer.Infrastructures;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Extensions;
using Bootstrap.Models.ResponseModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using IEnhancer = Bakabase.InsideWorld.Business.Components.Resource.Components.Enhancer.Infrastructures.IEnhancer;

namespace Bakabase.InsideWorld.Business.Components.Tasks
{
    [Obsolete]
    public class BackgroundTaskHelper
    {
        private readonly BackgroundTaskManager _btm;

        protected T GetRequiredService<T>() => _serviceProvider.Value!.GetRequiredService<T>();
        protected object GetRequiredService(Type type) => _serviceProvider.Value!.GetRequiredService(type);
        private readonly AsyncLocal<IServiceProvider> _serviceProvider = new();
        private readonly IServiceProvider _rootServiceProvider;

        public BackgroundTaskHelper(BackgroundTaskManager btm, IServiceProvider rootServiceProvider)
        {
            _btm = btm;
            _rootServiceProvider = rootServiceProvider;
        }

        private AsyncServiceScope CreateNewScope()
        {
            var scope = _rootServiceProvider.CreateAsyncScope();
            _serviceProvider.Value = scope.ServiceProvider;
            return scope;
        }

        public void RunInNewScope<TNewScopedService>(string taskName,
            Func<TNewScopedService, BackgroundTask, Task<BaseResponse>> runTask,
            BackgroundTaskLevel level = BackgroundTaskLevel.Default)
        {
            if (_btm.IsRunningByName(taskName))
            {
                return;
            }

            _btm.RunInBackground(taskName,
                new CancellationTokenSource(),
                async (task, sp) =>
                {
                    await using var scope = CreateNewScope();
                    var portal = scope.ServiceProvider.GetRequiredService<TNewScopedService>();
                    return await runTask(portal, task);
                }, level);
        }
    }
}