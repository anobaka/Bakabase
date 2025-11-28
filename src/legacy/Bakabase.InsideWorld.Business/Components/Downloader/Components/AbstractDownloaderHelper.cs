using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Components;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Input;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Extensions;
using Bootstrap.Models.Constants;
using Bootstrap.Models.ResponseModels;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Components
{
    /// <summary>
    /// Abstract base class for downloader helpers that provide both options and validation functionality
    /// </summary>
    /// <typeparam name="TOptions">The options type</typeparam>
    public abstract class AbstractDownloaderHelper<TOptions>(
        IBOptionsManager<TOptions> optionsManager,
        IDownloaderLocalizer localizer,
        HttpClient httpClient)
        : IDownloaderHelper
        where TOptions : class, ISimpleDownloaderOptionsHolder
    {
        public abstract ThirdPartyId ThirdPartyId { get; }

        /// <summary>
        /// Optional URL to validate cookie correctness
        /// </summary>
        protected virtual string? CookieValidationUrl => null;

        /// <summary>
        /// Perform additional platform-specific validation
        /// </summary>
        /// <param name="options">The options to validate</param>
        /// <returns>Validation result</returns>
        protected virtual Task<BaseResponse> ValidateAdditional(DownloaderOptions options)
        {
            return Task.FromResult(BaseResponseBuilder.Ok);
        }

        /// <summary>
        /// Validate cookie by making a test request
        /// </summary>
        /// <param name="cookie">Cookie to validate</param>
        /// <returns>True if cookie is valid</returns>
        protected virtual async Task<bool> ValidateCookieAsync(string cookie)
        {
            if (string.IsNullOrWhiteSpace(CookieValidationUrl))
            {
                return true; // No validation URL provided, assume valid
            }

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, CookieValidationUrl);
                request.Headers.Add("Cookie", cookie);

                using var response = await httpClient.SendAsync(request);
                return await IsCookieValidResponse(response);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Determine if the HTTP response indicates a valid cookie
        /// Override this method to customize validation logic
        /// </summary>
        /// <param name="response">HTTP response from validation request</param>
        /// <returns>True if cookie is considered valid</returns>
        protected virtual async Task<bool> IsCookieValidResponse(HttpResponseMessage response)
        {
            // Default implementation: successful status code means valid cookie
            return response.IsSuccessStatusCode;
        }

        /// <summary>
        /// Build download tasks from input model
        /// Override this method to provide custom task building logic
        /// </summary>
        /// <param name="model">The input model</param>
        /// <returns>Array of download tasks</returns>
        public virtual async Task<DownloadTask[]> BuildTasks(DownloadTaskAddInputModel model)
        {
            // Validate the model before building tasks
            await ValidateTaskCreationAsync(model);

            // Default implementation - can be overridden by specific helpers
            var tasks = model.Keys?.Select(key => new DownloadTask
            {
                Key = key,
                Name = model.Names?.ElementAtOrDefault(model.Keys.IndexOf(key)) ?? key,
                ThirdPartyId = ThirdPartyId,
                Type = model.Type,
                StartPage = model.StartPage,
                EndPage = model.EndPage,
                Interval = model.Interval,
                AutoRetry = model.AutoRetry,
                Status = DownloadTaskStatus.Idle,
                DownloadPath = model.DownloadPath,
                CreatedAt = DateTime.Now
            }).ToArray() ?? [];

            return tasks;
        }

        /// <summary>
        /// Validate download task creation parameters
        /// </summary>
        /// <param name="model">The input model to validate</param>
        protected virtual async Task ValidateTaskCreationAsync(DownloadTaskAddInputModel model)
        {
            var result = await ValidateTaskCreationInternalAsync(model);
            if (!result.IsSuccess())
            {
                throw new InvalidOperationException(result.Message);
            }
        }

        /// <summary>
        /// Internal validation that returns a response instead of throwing
        /// </summary>
        private async Task<BaseResponse> ValidateTaskCreationInternalAsync(DownloadTaskAddInputModel model)
        {
            // Common validation logic
            var commonValidation = await ValidateCommonRequirementsAsync(model);
            if (!commonValidation.IsSuccess())
            {
                return commonValidation;
            }

            // Platform-specific validation
            return await ValidateSpecificRequirementsAsync(model);
        }

        /// <summary>
        /// Common validation logic that applies to most download tasks
        /// Override this method to customize common validation behavior
        /// </summary>
        protected virtual async Task<BaseResponse> ValidateCommonRequirementsAsync(DownloadTaskAddInputModel model)
        {
            // Validate basic model structure
            if (model == null)
            {
                return BaseResponseBuilder.BuildBadRequest("Model cannot be null");
            }

            // Validate download path
            if (string.IsNullOrWhiteSpace(model.DownloadPath))
            {
                return BaseResponseBuilder.BuildBadRequest("Download path is required");
            }

            // Validate interval if specified
            if (model.Interval.HasValue && model.Interval.Value < 0)
            {
                return BaseResponseBuilder.BuildBadRequest("Interval cannot be negative");
            }

            // Validate page range if specified
            if (model.StartPage.HasValue && model.EndPage.HasValue && model.StartPage.Value > model.EndPage.Value)
            {
                return BaseResponseBuilder.BuildBadRequest("Start page cannot be greater than end page");
            }

            return BaseResponseBuilder.Ok;
        }

        /// <summary>
        /// Platform-specific validation logic
        /// Override this method to implement platform-specific validation
        /// </summary>
        protected virtual async Task<BaseResponse> ValidateSpecificRequirementsAsync(DownloadTaskAddInputModel model)
        {
            // Default implementation - no specific requirements
            return BaseResponseBuilder.Ok;
        }

        /// <summary>
        /// Helper method to validate that keys are required and present
        /// </summary>
        protected virtual BaseResponse ValidateKeysRequired(DownloadTaskAddInputModel model)
        {
            if (model.Keys?.Any() != true || model.Keys.Any(string.IsNullOrWhiteSpace))
            {
                return BaseResponseBuilder.BuildBadRequest("Keys are required but missing or empty");
            }

            return BaseResponseBuilder.Ok;
        }

        /// <summary>
        /// Helper method to validate that no keys are required (keys should be empty or null)
        /// </summary>
        protected virtual BaseResponse ValidateNoKeysRequired(DownloadTaskAddInputModel model)
        {
            // For tasks that don't require keys, having keys is still allowed but not required
            return BaseResponseBuilder.Ok;
        }

        public Task<DownloaderOptions> GetOptionsAsync()
        {
            var options = optionsManager.Value;
            var downloaderOptions = new DownloaderOptions
            {
                Cookie = options.Cookie,
                MaxConcurrency = options.MaxConcurrency,
                RequestInterval = options.RequestInterval,
                DefaultPath = options.DefaultPath,
                NamingConvention = options.NamingConvention,
                SkipExisting = options.SkipExisting,
                MaxRetries = options.MaxRetries,
                RequestTimeout = options.RequestTimeout
            };
            return Task.FromResult(downloaderOptions);
        }

        public async Task PutOptionsAsync(DownloaderOptions options)
        {
            await optionsManager.SaveAsync(x =>
            {
                x.Cookie = options.Cookie;
                x.MaxConcurrency = options.MaxConcurrency;
                x.RequestInterval = options.RequestInterval;
                x.DefaultPath = options.DefaultPath;
                x.NamingConvention = options.NamingConvention;
                x.SkipExisting = options.SkipExisting;
                x.MaxRetries = options.MaxRetries;
                x.RequestTimeout = options.RequestTimeout;
            });
        }

        public async Task ValidateOptionsAsync()
        {
            var result = await ValidateAsync();
            if (!result.IsSuccess())
            {
                throw new InvalidOperationException(result.Message);
            }
        }

        private async Task<BaseResponse> ValidateAsync()
        {
            var options = await GetOptionsAsync();

            // Validate cookie if provided and validation URL exists
            if (!string.IsNullOrWhiteSpace(options.Cookie))
            {
                var isCookieValid = await ValidateCookieAsync(options.Cookie);
                if (!isCookieValid)
                {
                    return BaseResponseBuilder.BuildBadRequest(localizer.InvalidCookie());
                }
            }

            // // Check if download path is set
            // if (string.IsNullOrWhiteSpace(options.DefaultPath))
            // {
            //     return BaseResponseBuilder.BuildBadRequest(localizer.DownloadPathNotSet());
            // }

            // Perform additional validation
            var additionalValidation = await ValidateAdditional(options);
            return additionalValidation;
        }
    }
}