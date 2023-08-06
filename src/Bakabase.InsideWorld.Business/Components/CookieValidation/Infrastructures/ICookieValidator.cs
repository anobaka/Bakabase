using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Constants;
using Bootstrap.Models.ResponseModels;

namespace Bakabase.InsideWorld.Business.Components.CookieValidation.Infrastructures
{
    public interface ICookieValidator
    {
        CookieValidatorTarget Target { get; }
        Task<BaseResponse> Validate(string cookie);
    }
}
