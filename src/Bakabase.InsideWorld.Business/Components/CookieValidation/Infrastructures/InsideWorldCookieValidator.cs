using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.ThirdParty.Pixiv.Models;
using Bakabase.InsideWorld.Business.Resources;
using Bakabase.InsideWorld.Models.Constants;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;

namespace Bakabase.InsideWorld.Business.Components.CookieValidation.Infrastructures
{
    public abstract class InsideWorldCookieValidator : ICookieValidator
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly InsideWorldLocalizer _localizer;

        protected InsideWorldCookieValidator(IHttpClientFactory httpClientFactory, InsideWorldLocalizer localizer)
        {
            _httpClientFactory = httpClientFactory;
            _localizer = localizer;
        }

        public abstract CookieValidatorTarget Target { get; }

        public async Task<BaseResponse> Validate(string cookie)
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, Url)
                {Headers = {{"Cookie", cookie}}});
            var (success, message, content) = await Validate(response);
            return success
                ? BaseResponseBuilder.Ok
                : BaseResponseBuilder.BuildBadRequest(_localizer.CookieValidation_Fail(Url, message, content));
        }


        /// <summary>
        /// The new cookie is not set while validating the cookie, so we should use a clean <see cref="HttpClient"/> to handle this request to avoid the cookie being set with previous cookie.
        /// </summary>
        protected virtual string HttpClientName { get; } = BusinessConstants.HttpClientNames.Default;

        protected abstract string Url { get; }
        protected abstract Task<(bool Success, string? Message, string? Content)> Validate(HttpResponseMessage rsp);
    }
}