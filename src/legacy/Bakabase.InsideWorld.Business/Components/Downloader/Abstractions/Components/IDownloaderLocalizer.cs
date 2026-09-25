using System;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Protocol;
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
        string InvalidCookie();
        string DownloadPathNotSet();

        /// <summary>
        /// The step shown while a task waits to re-run after a transient network failure.
        /// </summary>
        string TransientNetworkErrorRetrying(int delaySeconds, int retry, int maxRetries);

        /// <summary>The first line of a task's notes: how many notices follow.</summary>
        string DownloadNoticesSummary(int count);

        /// <summary>The line replacing the notices beyond the listed ones.</summary>
        string DownloadNoticesTruncated(int remaining);

        /// <summary>The favorites folder of a task is not among the account's folders.</summary>
        string BilibiliFavoritesNotFound(string favoritesId, string? name);

        /// <summary>A run stopped by Bilibili's risk control. <paramref name="code"/>: the API code or HTTP status.</summary>
        string BilibiliRiskControl(int? code);

        /// <summary>The step shown while a task waits out Bilibili's risk control.</summary>
        string BilibiliRiskControlWaiting(int minutes, int retry, int maxRetries);

        /// <summary>The Bilibili cookie is missing, expired or not logged in.</summary>
        string BilibiliNotLoggedIn();

        /// <summary>No free space left to write <paramref name="path"/>.</summary>
        string BilibiliDiskFull(string path);

        /// <summary>Why a Bilibili item or page was skipped.</summary>
        /// <param name="code">The API code, favorites item type, HTTP status or exit code the reason refers to.</param>
        /// <param name="message">Bilibili's own short message, if any.</param>
        string DescribeBilibiliSkip(BilibiliSkipReason reason, int? code, string? message);

        /// <summary>One notice line: <paramref name="subject"/> was skipped because of <paramref name="reason"/>.</summary>
        string BilibiliSkipNotice(string subject, string reason);

        /// <summary>The footer under Bilibili notes: skipped items are not retried automatically.</summary>
        string BilibiliSkipFooter();
    }
}