using System;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Entities;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Implementations;

public class SoulPlusPostExtractor: AbstractDownloader
{
    public SoulPlusPostExtractor(IServiceProvider serviceProvider) : base(serviceProvider)
    {
    }

    public override ThirdPartyId ThirdPartyId => ThirdPartyId.SoulPlus;
    protected override async Task StartCore(DownloadTask task, CancellationToken ct)
    {
        throw new NotImplementedException();
    }
}