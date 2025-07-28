using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Network;
using Bakabase.Abstractions.Extensions;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.ThirdParty.Abstractions.Http;
using Bakabase.Modules.ThirdParty.Abstractions.Logging;
using Bakabase.Modules.ThirdParty.Components.Http;
using Bakabase.Modules.ThirdParty.Components.Localization;
using Bakabase.Modules.ThirdParty.Services;
using Bakabase.Modules.ThirdParty.ThirdParties.Bangumi;
using Bakabase.Modules.ThirdParty.ThirdParties.Bilibili;
using Bakabase.Modules.ThirdParty.ThirdParties.Airav;
using Bakabase.Modules.ThirdParty.ThirdParties.AiravCC;
using Bakabase.Modules.ThirdParty.ThirdParties.Avsex;
using Bakabase.Modules.ThirdParty.ThirdParties.Avsox;
using Bakabase.Modules.ThirdParty.ThirdParties.CableAV;
using Bakabase.Modules.ThirdParty.ThirdParties.CNMDB;
using Bakabase.Modules.ThirdParty.ThirdParties.Dahlia;
using Bakabase.Modules.ThirdParty.ThirdParties.Dmm;
using Bakabase.Modules.ThirdParty.ThirdParties.DLsite;
using Bakabase.Modules.ThirdParty.ThirdParties.ExHentai;
using Bakabase.Modules.ThirdParty.ThirdParties.Faleno;
using Bakabase.Modules.ThirdParty.ThirdParties.Fantastica;
using Bakabase.Modules.ThirdParty.ThirdParties.FC2;
using Bakabase.Modules.ThirdParty.ThirdParties.Fc2club;
using Bakabase.Modules.ThirdParty.ThirdParties.Fc2hub;
using Bakabase.Modules.ThirdParty.ThirdParties.Fc2ppvdb;
using Bakabase.Modules.ThirdParty.ThirdParties.Freejavbt;
using Bakabase.Modules.ThirdParty.ThirdParties.Getchu;
using Bakabase.Modules.ThirdParty.ThirdParties.GetchuDl;
using Bakabase.Modules.ThirdParty.ThirdParties.Giga;
using Bakabase.Modules.ThirdParty.ThirdParties.Guochan;
using Bakabase.Modules.ThirdParty.ThirdParties.Hdouban;
using Bakabase.Modules.ThirdParty.ThirdParties.Hscangku;
using Bakabase.Modules.ThirdParty.ThirdParties.Iqqtv;
using Bakabase.Modules.ThirdParty.ThirdParties.IqqtvNew;
using Bakabase.Modules.ThirdParty.ThirdParties.Jav321;
using Bakabase.Modules.ThirdParty.ThirdParties.Javbus;
using Bakabase.Modules.ThirdParty.ThirdParties.Javday;
using Bakabase.Modules.ThirdParty.ThirdParties.Javdb;
using Bakabase.Modules.ThirdParty.ThirdParties.Javlibrary;
using Bakabase.Modules.ThirdParty.ThirdParties.Kin8;
using Bakabase.Modules.ThirdParty.ThirdParties.Love6;
using Bakabase.Modules.ThirdParty.ThirdParties.Lulubar;
using Bakabase.Modules.ThirdParty.ThirdParties.Madouqu;
using Bakabase.Modules.ThirdParty.ThirdParties.Mdtv;
using Bakabase.Modules.ThirdParty.ThirdParties.Mgstage;
using Bakabase.Modules.ThirdParty.ThirdParties.Mmtv;
using Bakabase.Modules.ThirdParty.ThirdParties.Mywife;
using Bakabase.Modules.ThirdParty.ThirdParties.Official;
using Bakabase.Modules.ThirdParty.ThirdParties.Pixiv;
using Bakabase.Modules.ThirdParty.ThirdParties.Prestige;
using Bakabase.Modules.ThirdParty.ThirdParties.SoulPlus;
using Bakabase.Modules.ThirdParty.ThirdParties.ThePornDB;
using Bakabase.Modules.ThirdParty.ThirdParties.ThePornDBMovies;
using Bakabase.Modules.ThirdParty.ThirdParties.Tmdb;
using Bakabase.Modules.ThirdParty.ThirdParties.Xcity;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Bakabase.Modules.ThirdParty.Extensions;

public static class ThirdPartyExtensions
{
    public static IServiceCollection AddThirdParty<TBilibiliOptions, TBangumiOptions, TDLsiteOptions, TExHentaiOptions,
        TPixivOptions, TSoulPlusOptions, TTmdbOptions>(
        this IServiceCollection services)
        where TBilibiliOptions : class, IThirdPartyHttpClientOptions, new()
        where TBangumiOptions : class, IThirdPartyHttpClientOptions, new()
        where TDLsiteOptions : class, IThirdPartyHttpClientOptions, new()
        where TExHentaiOptions : class, IThirdPartyHttpClientOptions, new()
        where TPixivOptions : class, IThirdPartyHttpClientOptions, new()
        where TSoulPlusOptions : class, ISoulPlusOptions, new()
        where TTmdbOptions : class, ITmdbOptions, new()
    {
        services.AddBakabaseHttpClient<BangumiHttpMessageHandler<TBangumiOptions>>(
            InternalOptions.HttpClientNames
                .Bangumi);
        services.TryAddSingleton<BangumiClient>();

        services.AddBakabaseHttpClient<DLsiteHttpMessageHandler<TDLsiteOptions>>(InternalOptions
            .HttpClientNames
            .DLsite);
        services.TryAddSingleton<DLsiteClient>();

        services.AddBakabaseHttpClient<ExHentaiHttpMessageHandler<TExHentaiOptions>>(
            InternalOptions.HttpClientNames
                .ExHentai);
        services.TryAddSingleton<ExHentaiClient>();

        services.AddBakabaseHttpClient<PixivHttpMessageHandler<TPixivOptions>>(InternalOptions
            .HttpClientNames
            .Pixiv);
        services.TryAddSingleton<PixivClient>();

        services.AddBakabaseHttpClient<BilibiliHttpMessageHandler<TBilibiliOptions>>(
            InternalOptions.HttpClientNames
                .Bilibili);
        services.TryAddSingleton<BilibiliClient>();

        services.AddBakabaseHttpClient<SoulPlusHttpMessageHandler<TSoulPlusOptions>>(
            InternalOptions.HttpClientNames
                .SoulPlus);
        services.AddSingleton<IBOptions<ISoulPlusOptions>>(x => x.GetRequiredService<IBOptions<TSoulPlusOptions>>());
        services.TryAddSingleton<SoulPlusClient>();

        services.AddBakabaseHttpClient<TmdbHttpMessageHandler<TTmdbOptions>>(
            InternalOptions.HttpClientNames
                .Tmdb);
        services.AddSingleton<IBOptions<ITmdbOptions>>(x => x.GetRequiredService<IBOptions<TTmdbOptions>>());
        services.TryAddSingleton<TmdbClient>();

        // Register default-named clients (no custom HttpMessageHandler)
        services.TryAddSingleton<AiravClient>();
        services.TryAddSingleton<AiravCCClient>();
        services.TryAddSingleton<AvsexClient>();
        services.TryAddSingleton<AvsoxClient>();
        services.TryAddSingleton<CableAVClient>();
        services.TryAddSingleton<CNMDBClient>();
        services.TryAddSingleton<FC2Client>();
        services.TryAddSingleton<DahliaClient>();
        services.TryAddSingleton<DmmClient>();
        services.TryAddSingleton<FalenoClient>();
        services.TryAddSingleton<FantasticaClient>();
        services.TryAddSingleton<Fc2clubClient>();
        services.TryAddSingleton<Fc2hubClient>();
        services.TryAddSingleton<Fc2ppvdbClient>();
        services.TryAddSingleton<FreejavbtClient>();
        services.TryAddSingleton<GetchuClient>();
        services.TryAddSingleton<GetchuDlClient>();
        services.TryAddSingleton<GigaClient>();
        services.TryAddSingleton<GuochanClient>();
        services.TryAddSingleton<HdoubanClient>();
        services.TryAddSingleton<HscangkuClient>();
        services.TryAddSingleton<IqqtvClient>();
        services.TryAddSingleton<IqqtvNewClient>();
        services.TryAddSingleton<Jav321Client>();
        services.TryAddSingleton<JavbusClient>();
        services.TryAddSingleton<JavdayClient>();
        services.TryAddSingleton<JavdbClient>();
        services.TryAddSingleton<JavlibraryClient>();
        services.TryAddSingleton<Kin8Client>();
        services.TryAddSingleton<Love6Client>();
        services.TryAddSingleton<LulubarClient>();
        services.TryAddSingleton<MadouquClient>();
        services.TryAddSingleton<MdtvClient>();
        services.TryAddSingleton<MgstageClient>();
        services.TryAddSingleton<MmtvClient>();
        services.TryAddSingleton<MywifeClient>();
        services.TryAddSingleton<OfficialClient>();
        services.TryAddSingleton<PrestigeClient>();
        services.TryAddSingleton<ThePornDBClient>();
        services.TryAddSingleton<ThePornDBMoviesClient>();
        services.TryAddSingleton<XcityClient>();

        services.AddTransient<IThirdPartyLocalizer, ThirdPartyLocalizer>();
        services.TryAddSingleton<ThirdPartyHttpRequestLogger>();
        
        services.AddSingleton<IThirdPartyService, ThirdPartyService>();

        return services;
    }
}