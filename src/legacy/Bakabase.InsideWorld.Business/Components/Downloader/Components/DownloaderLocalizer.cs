using System.Collections.Generic;
using System.Globalization;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Components;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Protocol;
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

        public string InvalidCookie()
        {
            return this[nameof(InvalidCookie)];
        }

        public string DownloadPathNotSet()
        {
            return this[nameof(DownloadPathNotSet)];
        }

        public string TransientNetworkErrorRetrying(int delaySeconds, int retry, int maxRetries) =>
            this[nameof(TransientNetworkErrorRetrying), delaySeconds, retry, maxRetries];

        public string DownloadNoticesSummary(int count) => this["DownloadNotices.Summary", count];

        public string DownloadNoticesTruncated(int remaining) => this["DownloadNotices.Truncated", remaining];

        public string BilibiliFavoritesNotFound(string favoritesId, string? name) =>
            this["Bilibili.FavoritesNotFound", favoritesId, name ?? string.Empty];

        public string BilibiliRiskControl(int? code) =>
            this["Bilibili.RiskControl", FormatCode(code)];

        public string BilibiliRiskControlWaiting(int minutes, int retry, int maxRetries) =>
            this["Bilibili.RiskControlWaiting", minutes, retry, maxRetries];

        public string BilibiliNotLoggedIn() => this["Bilibili.NotLoggedIn"];

        public string BilibiliDiskFull(string path) => this["Bilibili.DiskFull", path];

        public string DescribeBilibiliSkip(BilibiliSkipReason reason, int? code, string? message)
        {
            var result = localizer[$"Bilibili.Skip.{reason}", FormatCode(code), message ?? string.Empty];
            return result.ResourceNotFound ? reason.ToString() : result.Value.TrimEnd();
        }

        public string BilibiliSkipNotice(string subject, string reason) => this["Bilibili.SkipNotice", subject, reason];

        public string BilibiliSkipFooter() => this["Bilibili.SkipFooter"];

        /// <summary>Protocol codes are identifiers, not quantities: always "-352", whatever the culture's minus sign.</summary>
        private static string FormatCode(int? code) => code?.ToString(CultureInfo.InvariantCulture) ?? "?";
    }
}