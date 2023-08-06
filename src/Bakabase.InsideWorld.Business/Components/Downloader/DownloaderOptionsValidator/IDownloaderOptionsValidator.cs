using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions;
using Bakabase.InsideWorld.Models.Constants;
using Bootstrap.Models.ResponseModels;

namespace Bakabase.InsideWorld.Business.Components.Downloader.DownloaderOptionsValidator
{
    public interface IDownloaderOptionsValidator
    {
        ThirdPartyId ThirdPartyId { get; }
        Task<BaseResponse> Validate();
    }
}
