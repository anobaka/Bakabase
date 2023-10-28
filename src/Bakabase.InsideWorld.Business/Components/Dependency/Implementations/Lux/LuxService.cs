using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.App;
using Bakabase.InsideWorld.Business.Components.Dependency.Abstractions;
using Bakabase.InsideWorld.Business.Components.Dependency.Discovery;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Components.Dependency.Implementations.Lux
{
    public class LuxService
        (ILoggerFactory loggerFactory, AppService appService, IHttpClientFactory httpClientFactory) :
            HttpSourceComponentService(loggerFactory, appService, "lux", httpClientFactory)
    {
        public override string Id => "6cb4ac7f-f60d-45c9-86bc-a48918623654";
        public override string DisplayName => "lux";

        public override async Task<DependentComponentVersion> GetLatestVersion(CancellationToken ct)
        {
            throw new NotImplementedException();
        }

        protected override IDiscoverer Discoverer { get; } = new LuxDiscover(loggerFactory);

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