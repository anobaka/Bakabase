using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Network;
using Bakabase.Abstractions.Extensions;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.ThirdParty.Abstractions.Http;
using Bakabase.Modules.ThirdParty.Abstractions.Logging;
using Bakabase.Modules.ThirdParty.Components.Http;
using Bakabase.Modules.ThirdParty.Components.Localization;
using Bakabase.Modules.ThirdParty.Services;
using Bakabase.Modules.ThirdParty.ThirdParties.Av;
using Bakabase.Modules.ThirdParty.ThirdParties.Bangumi;
using Bakabase.Modules.ThirdParty.ThirdParties.Bilibili;
using Bakabase.Modules.ThirdParty.ThirdParties.Airav;
using Bakabase.Modules.ThirdParty.ThirdParties.Avsex;
using Bakabase.Modules.ThirdParty.ThirdParties.Avsox;
using Bakabase.Modules.ThirdParty.ThirdParties.CNMDB;
using Bakabase.Modules.ThirdParty.ThirdParties.Dahlia;
using Bakabase.Modules.ThirdParty.ThirdParties.DLsite;
using Bakabase.Modules.ThirdParty.ThirdParties.DMM;
using Bakabase.Modules.ThirdParty.ThirdParties.ExHentai;
using Bakabase.Modules.ThirdParty.ThirdParties.Steam;
using Bakabase.Modules.ThirdParty.ThirdParties.Faleno;
using Bakabase.Modules.ThirdParty.ThirdParties.Fantastica;
using Bakabase.Modules.ThirdParty.ThirdParties.FC2;
using Bakabase.Modules.ThirdParty.ThirdParties.Fc2hub;
using Bakabase.Modules.ThirdParty.ThirdParties.Freejavbt;
using Bakabase.Modules.ThirdParty.ThirdParties.GetchuDl;
using Bakabase.Modules.ThirdParty.ThirdParties.Iqqtv;
using Bakabase.Modules.ThirdParty.ThirdParties.Jav321;
using Bakabase.Modules.ThirdParty.ThirdParties.Javbus;
using Bakabase.Modules.ThirdParty.ThirdParties.Javday;
using Bakabase.Modules.ThirdParty.ThirdParties.Javdb;
using Bakabase.Modules.ThirdParty.ThirdParties.Javlibrary;
using Bakabase.Modules.ThirdParty.ThirdParties.Lulubar;
using Bakabase.Modules.ThirdParty.ThirdParties.Mmtv;
using Bakabase.Modules.ThirdParty.ThirdParties.Pixiv;
using Bakabase.Modules.ThirdParty.ThirdParties.SoulPlus;
using Bakabase.Modules.ThirdParty.ThirdParties.Tmdb;
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

        services.TryAddSingleton<SteamClient>();
        services.TryAddSingleton<SteamLocalLibrary>();

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

        // Register default-named clients (no custom HttpMessageHandler).
        // The IAvClient mapping is what AvEnhancer / AvController consume —
        // adding a new AV source means registering both lines below, after which
        // both dispatchers pick it up automatically.
        services.TryAddSingleton<AiravClient>();
        services.AddSingleton<IAvClient>(sp => sp.GetRequiredService<AiravClient>());
        services.TryAddSingleton<AvsexClient>();
        services.AddSingleton<IAvClient>(sp => sp.GetRequiredService<AvsexClient>());
        services.TryAddSingleton<AvsoxClient>();
        services.AddSingleton<IAvClient>(sp => sp.GetRequiredService<AvsoxClient>());
        services.TryAddSingleton<CNMDBClient>();
        services.AddSingleton<IAvClient>(sp => sp.GetRequiredService<CNMDBClient>());
        services.TryAddSingleton<FC2Client>();
        services.AddSingleton<IAvClient>(sp => sp.GetRequiredService<FC2Client>());
        services.TryAddSingleton<DahliaClient>();
        services.AddSingleton<IAvClient>(sp => sp.GetRequiredService<DahliaClient>());
        services.TryAddSingleton<DmmClient>();
        services.AddSingleton<IAvClient>(sp => sp.GetRequiredService<DmmClient>());
        services.TryAddSingleton<FalenoClient>();
        services.AddSingleton<IAvClient>(sp => sp.GetRequiredService<FalenoClient>());
        services.TryAddSingleton<FantasticaClient>();
        services.AddSingleton<IAvClient>(sp => sp.GetRequiredService<FantasticaClient>());
        services.TryAddSingleton<Fc2hubClient>();
        services.AddSingleton<IAvClient>(sp => sp.GetRequiredService<Fc2hubClient>());
        services.TryAddSingleton<FreejavbtClient>();
        services.AddSingleton<IAvClient>(sp => sp.GetRequiredService<FreejavbtClient>());
        services.TryAddSingleton<GetchuDlClient>();
        services.AddSingleton<IAvClient>(sp => sp.GetRequiredService<GetchuDlClient>());
        services.TryAddSingleton<IqqtvClient>();
        services.AddSingleton<IAvClient>(sp => sp.GetRequiredService<IqqtvClient>());
        services.TryAddSingleton<Jav321Client>();
        services.AddSingleton<IAvClient>(sp => sp.GetRequiredService<Jav321Client>());
        services.TryAddSingleton<JavbusClient>();
        services.AddSingleton<IAvClient>(sp => sp.GetRequiredService<JavbusClient>());
        services.TryAddSingleton<JavdayClient>();
        services.AddSingleton<IAvClient>(sp => sp.GetRequiredService<JavdayClient>());
        services.TryAddSingleton<JavdbClient>();
        services.AddSingleton<IAvClient>(sp => sp.GetRequiredService<JavdbClient>());
        services.TryAddSingleton<JavlibraryClient>();
        services.AddSingleton<IAvClient>(sp => sp.GetRequiredService<JavlibraryClient>());
        services.TryAddSingleton<LulubarClient>();
        services.AddSingleton<IAvClient>(sp => sp.GetRequiredService<LulubarClient>());
        services.TryAddSingleton<MmtvClient>();
        services.AddSingleton<IAvClient>(sp => sp.GetRequiredService<MmtvClient>());

        services.AddTransient<IThirdPartyLocalizer, ThirdPartyLocalizer>();
        services.TryAddSingleton<ThirdPartyHttpRequestLogger>();
        services.TryAddSingleton<IThirdPartyCookieContainer, ThirdPartyCookieContainer>();
        // Fallback resolver — supplies the built-in age-gate cookies for known AV
        // sources when nothing higher-level (e.g. AvSourceOptions) is registered.
        services.TryAddSingleton<IAvSourceOptionsProvider, DefaultAvSourceOptionsProvider>();

        services.AddSingleton<IThirdPartyService, ThirdPartyService>();

        return services;
    }
}