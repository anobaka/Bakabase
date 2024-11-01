using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Infrastructures.Components.App.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Dependency.Abstractions;
using Bakabase.InsideWorld.Business.Components.Dependency.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Dependency.Discovery;
using Bakabase.InsideWorld.Models.Constants;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Components.Dependency
{
    public abstract class DependentComponentService : IDependentComponentService
    {
        private readonly IServiceProvider _globalServiceProvider;
        public abstract string Id { get; }

        public string DisplayName =>
            _globalServiceProvider.GetRequiredService<IDependencyLocalizer>()
                .Dependency_Component_Name(KeyInLocalizer) ?? GetType().Name;

        public string? Description => _globalServiceProvider.GetRequiredService<IDependencyLocalizer>()
            .Dependency_Component_Description(KeyInLocalizer);

        protected abstract string KeyInLocalizer { get; }
        public string DefaultLocation { get; }
        public abstract bool IsRequired { get; }
        protected ILogger Logger;
        protected string DirectoryName { get; }
        protected string TempDirectory { get; }

        protected DependentComponentService(ILoggerFactory loggerFactory, AppService appService,
            string directoryName, IServiceProvider globalServiceProvider)
        {
            _globalServiceProvider = globalServiceProvider;
            Logger = loggerFactory.CreateLogger(GetType());

            DirectoryName = directoryName;
            DefaultLocation = Path.Combine(appService.ComponentsPath, DirectoryName);
            TempDirectory = Path.Combine(DefaultLocation, InternalOptions.TempDirectoryName);
        }

        protected string GetExecutableWithValidation(string name) => Status == DependentComponentStatus.Installed
            ? Path.Combine(Context.Location!, name)
            : throw new Exception($"{DisplayName} is not ready");

        protected abstract Task InstallCore(CancellationToken ct);

        public virtual async Task Install(CancellationToken ct)
        {
            if (Status == DependentComponentStatus.Installing)
            {
                return;
            }

            Status = DependentComponentStatus.Installing;
            await UpdateContext(d =>
            {
                d.Error = null;
                d.InstallationProgress = 0;
            });
            try
            {
                await InstallCore(ct);
                Status = DependentComponentStatus.Installed;
                await UpdateContext(d => { d.InstallationProgress = 100; });
            }
            catch (Exception e)
            {
                Status = DependentComponentStatus.NotInstalled;
                await UpdateContext(d => { d.Error = e.Message; });
                Logger.LogError(e, $"An error occurred during installing {DisplayName}: {e.Message}");
                throw;
            }
            finally
            {
                await Discover(ct);
            }
        }

        /// <summary>
        /// <inheritdoc cref="IDependentComponentService.Discover"/>
        /// </summary>
        /// <param name="ct"></param>
        /// <returns></returns>
        public virtual async Task Discover(CancellationToken ct)
        {
            var r = await Discoverer.Discover(DefaultLocation, ct);
            if (Status != DependentComponentStatus.Installing)
            {
                Status = string.IsNullOrEmpty(r?.Version)
                    ? DependentComponentStatus.NotInstalled
                    : DependentComponentStatus.Installed;
            }

            if (r.HasValue)
            {
                await UpdateContext(c =>
                {
                    c.Location = r.Value.Location;
                    c.Version = r.Value.Version;
                });
            }
        }

        protected abstract IDiscoverer Discoverer { get; }

        public DependentComponentStatus Status { get; protected set; } = DependentComponentStatus.NotInstalled;

        private DependentComponentVersion? _latestVersion;

        public async Task<DependentComponentVersion> GetLatestVersion(bool fromCache, CancellationToken ct)
        {
            if (!fromCache || _latestVersion == null)
            {
                _latestVersion = await GetLatestVersion(ct);
            }

            return _latestVersion;
        }

        public abstract Task<DependentComponentVersion> GetLatestVersion(CancellationToken ct);


        protected async Task UpdateContext(Action<DependentComponentContext> update)
        {
            update(Context);
            await TriggerOnStateChange();
        }

        public virtual DependentComponentContext Context { get; } = new();

        public event Func<DependentComponentContext, Task>? OnStateChange;

        protected virtual async Task TriggerOnStateChange()
        {
            if (OnStateChange != null)
            {
                await OnStateChange(Context);
            }
        }
    }
}