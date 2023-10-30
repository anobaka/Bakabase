using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Infrastructures.Components.App.Upgrade;
using Bakabase.InsideWorld.Business.Components.Dependency.Abstractions;
using Bakabase.InsideWorld.Business.Components.Dependency.Discovery;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Components.Dependency.Implementations.Updater
{
    public class UpdaterService
        (ILoggerFactory loggerFactory, AppService appService, UpdaterUpdater updaterUpdater) :
            DependentComponentService(loggerFactory, appService, "updater")
    {
        public override string Id => "274f8ab5-a0a6-4415-8466-137d986d3ddb";
        public override string DisplayName => "Updater";

        public override async Task<DependentComponentVersion> GetLatestVersion(CancellationToken ct)
        {
            var version = await updaterUpdater.CheckNewVersion();
            return new DependentComponentVersion
            {
                Version = version.Version,
                CanUpdate = Context?.Version != version.Version
            };
        }

        protected override async Task InstallCore(CancellationToken ct)
        {
            ct.Register(updaterUpdater.StopUpdating);
            await updaterUpdater.StartUpdating();
        }

        protected override IDiscoverer Discoverer { get; } = new UpdaterDiscover();
    }
}