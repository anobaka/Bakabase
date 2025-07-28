using System;
using Bakabase.InsideWorld.Models.Constants;
using Microsoft.Extensions.Localization;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Components
{
    /// <summary>
    /// Localizer interface for downloader-specific messages
    /// </summary>
    public interface IDownloaderLocalizer : IStringLocalizer
    {
        /// <summary>
        /// Get localized platform name
        /// </summary>
        /// <param name="thirdPartyId">The third party platform ID</param>
        /// <param name="taskType"></param>
        /// <returns>Localized platform name</returns>
        string GetDownloaderName<TEnum>(ThirdPartyId thirdPartyId, TEnum taskType);

        string? GetDownloaderDescription<TEnum>(ThirdPartyId thirdPartyId, TEnum taskType);

        /// <summary>
        /// Get localized name for a naming field
        /// </summary>
        /// <param name="namingFieldValue">The naming field enum value</param>
        /// <returns>Localized naming field name</returns>
        string GetNamingFieldName<TEnum>(TEnum namingFieldValue);

        string? GetNamingFieldDescription<TEnum>(TEnum namingFieldValue);
        string? GetNamingFieldExample<TEnum>(TEnum namingFieldValue);

        string InvalidFavorites();
        string FfMpegIsNotReady();
        string LuxIsNotReady();
        string InvalidCookie();
        string DownloadPathNotSet();
    }
}