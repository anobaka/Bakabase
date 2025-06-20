﻿using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Extensions;
using Bakabase.Modules.ThirdParty.Abstractions.Http;
using Bakabase.Modules.ThirdParty.Components.Localization;
using Bakabase.Modules.ThirdParty.ThirdParties.Bangumi;
using Bakabase.Modules.ThirdParty.ThirdParties.Bilibili;
using Bakabase.Modules.ThirdParty.ThirdParties.DLsite;
using Bakabase.Modules.ThirdParty.ThirdParties.ExHentai;
using Bakabase.Modules.ThirdParty.ThirdParties.Pixiv;
using Bakabase.Modules.ThirdParty.ThirdParties.SoulPlus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bakabase.Modules.ThirdParty.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddThirdParty(this IServiceCollection services)
    {
        services.AddBakabaseHttpClient<BangumiThirdPartyHttpMessageHandler>(InternalOptions.HttpClientNames
            .Bangumi);
        services.TryAddSingleton<BangumiClient>();

        services.AddBakabaseHttpClient<BangumiThirdPartyHttpMessageHandler>(InternalOptions.HttpClientNames
            .DLsite);
        services.TryAddSingleton<DLsiteClient>();

        services.AddBakabaseHttpClient<ExHentaiThirdPartyHttpMessageHandler>(InternalOptions.HttpClientNames
            .ExHentai);
        services.TryAddSingleton<ExHentaiClient>();

        services.AddBakabaseHttpClient<SoulPlusThirdPartyHttpMessageHandler>(InternalOptions.HttpClientNames
            .Pixiv);
        services.TryAddSingleton<PixivClient>();

        services.AddBakabaseHttpClient<BilibiliThirdPartyHttpMessageHandler>(InternalOptions.HttpClientNames
            .Bilibili);
        services.TryAddSingleton<BilibiliClient>();

        services.AddBakabaseHttpClient<SoulPlusThirdPartyHttpMessageHandler>(InternalOptions.HttpClientNames
            .SoulPlus);
        services.TryAddSingleton<SoulPlusClient>();

        services.AddTransient<IThirdPartyLocalizer, ThirdPartyLocalizer>();
        services.TryAddSingleton<ThirdPartyHttpRequestLogger>();
        return services;
    }
}