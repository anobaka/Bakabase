using Bakabase.InsideWorld.Models.Constants;
using Bootstrap.Models.ResponseModels;

namespace Bakabase.Modules.ThirdParty.Abstractions.Http.Cookie
{
    public interface ICookieValidator
    {
        CookieValidatorTarget Target { get; }
        Task<BaseResponse> Validate(string cookie);
    }
}
