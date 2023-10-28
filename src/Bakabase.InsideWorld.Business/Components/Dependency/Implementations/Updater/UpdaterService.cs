using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.App;
using Bakabase.InsideWorld.Business.Components.Dependency.Abstractions;
using Bakabase.InsideWorld.Business.Components.Dependency.Discovery;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Components.Dependency.Implementations.Updater
{
    public class UpdaterService(ILoggerFactory loggerFactory, AppService appService,
        IHttpClientFactory httpClientFactory) : HttpSourceComponentService(loggerFactory, appService, "updater",
        httpClientFactory)
    {
        public override string Id => "274f8ab5-a0a6-4415-8466-137d986d3ddb";
        public override string DisplayName => "Updater";

        public override async Task<DependentComponentVersion> GetLatestVersion(CancellationToken ct)
        {
            throw new NotImplementedException();
        }

        protected override IDiscoverer Discoverer { get; }

        protected override async Task<List<string>> GetDownloadUrls(DependentComponentVersion version,
            CancellationToken ct)
        {
            throw new NotImplementedException();
        }

        protected override async Task PostDownloading(List<string> files, CancellationToken ct)
        {
            throw new NotImplementedException();
        }
    }
}