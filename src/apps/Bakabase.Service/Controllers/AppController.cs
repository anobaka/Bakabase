using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Infrastructures.Components.App.Models.RequestModels;
using Bakabase.Infrastructures.Components.App.Models.ResponseModels;
using Bakabase.InsideWorld.Business;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Extensions;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Localization;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers
{
    [Route("~/app")]
    public class AppController(AppService appService, AppDataMover appDataMover) : Controller
    {
        [HttpGet("initialized")]
        [SwaggerOperation(OperationId = "CheckAppInitialized")]
        public async Task<SingletonResponse<InitializationContentType>> Initialized()
        {
            if (appService.NotAcceptTerms)
            {
                return new SingletonResponse<InitializationContentType>(InitializationContentType.NotAcceptTerms);
            }

            if (appService.NeedRestart)
            {
                return new SingletonResponse<InitializationContentType>(InitializationContentType.NeedRestart);
            }

            return SingletonResponseBuilder<InitializationContentType>.Ok;
        }

        [HttpGet("info")]
        [SwaggerOperation(OperationId = "GetAppInfo")]
        public async Task<SingletonResponse<AppInfo>> Info()
        {
            return new SingletonResponse<AppInfo>(appService.AppInfo);
        }

        [HttpPost("terms")]
        [SwaggerOperation(OperationId = "AcceptTerms")]
        public async Task<BaseResponse> AcceptTerms()
        {
            appService.NotAcceptTerms = false;
            return BaseResponseBuilder.Ok;
        }

        [HttpPut("data-path")]
        [SwaggerOperation(OperationId = "MoveCoreData")]
        public async Task<BaseResponse> MoveCoreData([FromBody] CoreDataMoveRequestModel model)
        {
            if (model.DataPath.IsNotEmpty())
            {
                var dir = new DirectoryInfo(model.DataPath);
                if (dir.Exists)
                {
                    if (dir.GetFileSystemInfos().Any())
                    {
                        return BaseResponseBuilder.BuildBadRequest("The target folder is not empty");
                    }
                }

                await appDataMover.CopyCoreData(model.DataPath);
                return BaseResponseBuilder.Ok;
            }

            return BaseResponseBuilder.BuildBadRequest("Invalid path");
        }
    }
}