using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.ThirdParty.Abstractions.Http.Cookie;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;

namespace Bakabase.Modules.ThirdParty.Components.Http.Cookie
{
    public abstract class AbstractCookieValidator(IHttpClientFactory httpClientFactory, IBakabaseLocalizer localizer)
        : ICookieValidator
    {
        public abstract CookieValidatorTarget Target { get; }

        public async Task<BaseResponse> Validate(string cookie)
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, Url)
                {Headers = {{"Cookie", cookie}}});
            var (success, message, content) = await Validate(response);
            return success
                ? BaseResponseBuilder.Ok
                : BaseResponseBuilder.BuildBadRequest(localizer.CookieValidation_Fail(Url, message, content));
        }


        /// <summary>
        /// The new cookie is not set while validating the cookie, so we should use a clean <see cref="HttpClient"/> to handle this request to avoid the cookie being set with previous cookie.
        /// </summary>
        protected virtual string HttpClientName => InternalOptions.HttpClientNames.Default;

        protected abstract string Url { get; }
        protected abstract Task<(bool Success, string? Message, string? Content)> Validate(HttpResponseMessage rsp);
    }
}