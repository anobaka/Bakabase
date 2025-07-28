using System.Collections.Generic;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Components;
using Bakabase.InsideWorld.Models.Constants;
using Microsoft.Extensions.Localization;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Components
{
    /// <summary>
    /// Implementation of downloader localizer
    /// </summary>
    internal class DownloaderLocalizer(IStringLocalizer<DownloaderResource> localizer) : IDownloaderLocalizer
    {

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) =>
            localizer.GetAllStrings(includeParentCultures);

        public LocalizedString this[string name] => localizer[name];

        public LocalizedString this[string name, params object[] arguments] => localizer[name, arguments];

        public string GetDownloaderName<TEnum>(ThirdPartyId thirdPartyId, TEnum taskType)
        {
            var key = $"{thirdPartyId}.{taskType}";
            var result = localizer[key];
            return result.ResourceNotFound ? key : result.Value;
        }

        public string? GetDownloaderDescription<TEnum>(ThirdPartyId thirdPartyId, TEnum taskType)
        {
            var key = $"{thirdPartyId}.{taskType}.Description";
            var result = localizer[key];
            return result.ResourceNotFound ? null : result.Value;
        }

        public string GetNamingFieldName<TEnum>(TEnum namingFieldValue)
        {
            var key = $"{typeof(TEnum).Name}.{namingFieldValue}";
            var result = localizer[key];
            return result.ResourceNotFound ? namingFieldValue?.ToString() ?? string.Empty : result.Value;
        }

        public string? GetNamingFieldDescription<TEnum>(TEnum namingFieldValue)
        {
            var key = $"{typeof(TEnum).Name}.{namingFieldValue}.Description";
            var result = localizer[key];
            return result.ResourceNotFound ? null : result.Value;
        }

        public string? GetNamingFieldExample<TEnum>(TEnum namingFieldValue)
        {
            var key = $"{typeof(TEnum).Name}.{namingFieldValue}.Example";
            var result = localizer[key];
            return result.ResourceNotFound ? null : result.Value;
        }

        public string InvalidFavorites() => this[nameof(InvalidFavorites)];

        public string FfMpegIsNotReady() => this[nameof(FfMpegIsNotReady)];

        public string LuxIsNotReady() => this[nameof(LuxIsNotReady)];
        public string InvalidCookie()
        {
            return this[nameof(InvalidCookie)];
        }

        public string DownloadPathNotSet()
        {
            return this[nameof(DownloadPathNotSet)];
        }
    }
}