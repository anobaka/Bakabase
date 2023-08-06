using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Infrastructures.Components.App.Models.RequestModels;
using Bakabase.Infrastructures.Components.App.Models.ResponseModels;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Configurations;
using Bakabase.InsideWorld.Business.Extensions;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Constants.Aos;
using Bakabase.InsideWorld.Models.Extensions;
using Bakabase.InsideWorld.Models.Models.Aos;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bakabase.InsideWorld.Models.RequestModels;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Extensions;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Localization;
using Swashbuckle.AspNetCore.Annotations;
using Xabe.FFmpeg;

namespace Bakabase.InsideWorld.App.Core.Controllers
{
    [Route("~/app")]
    public class AppController : Controller
    {
        private readonly IHostApplicationLifetime _lifetime;
        private readonly IStringLocalizer<SharedResource> _localizer;
        private readonly AppService _appService;
        private readonly AppDataMover _appDataMover;
        private readonly InsideWorldOptionsManagerPool _insideWorldOptionsManager;

        public AppController(IHostApplicationLifetime lifetime, IStringLocalizer<SharedResource> localizer,
            AppService appService, AppDataMover appDataMover, InsideWorldOptionsManagerPool insideWorldOptionsManager)
        {
            _lifetime = lifetime;
            _localizer = localizer;
            _appService = appService;
            _appDataMover = appDataMover;
            _insideWorldOptionsManager = insideWorldOptionsManager;
        }

        [HttpGet("initialized")]
        [SwaggerOperation(OperationId = "CheckAppInitialized")]
        public async Task<SingletonResponse<InitializationContentType>> Initialized()
        {
            if (_appService.NotAcceptTerms)
            {
                return new SingletonResponse<InitializationContentType>(InitializationContentType.NotAcceptTerms);
            }

            if (_appService.NeedRestart)
            {
                return new SingletonResponse<InitializationContentType>(InitializationContentType.NeedRestart);
            }

            return SingletonResponseBuilder<InitializationContentType>.Ok;
        }

        [HttpGet("info")]
        [SwaggerOperation(OperationId = "GetAppInfo")]
        public async Task<SingletonResponse<AppInfo>> Info()
        {
            return new SingletonResponse<AppInfo>(_appService.AppInfo);
        }

        [HttpPost("terms")]
        [SwaggerOperation(OperationId = "AcceptTerms")]
        public async Task<BaseResponse> AcceptTerms()
        {
            _appService.NotAcceptTerms = false;
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

                await _appDataMover.CopyCoreData(model.DataPath);
                return BaseResponseBuilder.Ok;
            }

            return BaseResponseBuilder.BuildBadRequest("Invalid path");
        }
    }
}