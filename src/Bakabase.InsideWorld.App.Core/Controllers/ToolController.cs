using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.Storage.Services;
using Bakabase.InsideWorld.Business.Components.CookieValidation.Infrastructures;
using Bakabase.InsideWorld.Business.Components.Resource.Components.PropertyMatcher;
using Bakabase.InsideWorld.Business.Components.ThirdParty.ExHentai;
using Bakabase.InsideWorld.Business.Components.ThirdParty.ExHentai.Models;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.RequestModels;
using Bakabase.InsideWorld.Models.ResponseModels;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.Constants;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using MimeKit;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.InsideWorld.App.Core.Controllers
{
    [Route("~/tool")]
    public class ToolController : Controller
    {
        private readonly ResourceService _resourceService;
        private readonly ResourceCategoryService _categoryService;
        private readonly ToolService _toolService;

        public ToolController(ResourceService resourceService,
            ResourceCategoryService categoryService, ToolService toolService)
        {
            _resourceService = resourceService;
            _categoryService = categoryService;
            _toolService = toolService;
        }

        //[SwaggerOperation(OperationId = "TestCleaning")]
        //[HttpPost("test-cleaning")]
        //public async Task<SingletonResponse<Dictionary<ResourceCategory, List<(string, string)>>>> TestCleaning()
        //{
        //    var categoryDirs = SpecificEnumUtils<ResourceCategory>.Values
        //        .ToDictionary(t => t, t => _fileService.GetCategoryDirectory(RootDirectoryType.TbdOtherWorld, t));
        //    var results =
        //        SpecificEnumUtils<ResourceCategory>.Values.ToDictionary(t => t,
        //            t => new List<(string Original, string New)>());
        //    foreach (var (c, dir) in categoryDirs)
        //    {
        //        var resourceNames = dir.GetDirectories().Select(t => t.Name).ToList();
        //        foreach (var n in resourceNames)
        //        {
        //            results[c].Add((n, await _resourceService.PretreatmentAsync(n)));
        //        }
        //    }

        //    return new SingletonResponse<Dictionary<ResourceCategory, List<(string, string)>>>(results);
        //}

        //[SwaggerOperation(OperationId = "CleanFullname")]
        //[HttpPost("clean-fullname")]
        //public async Task<SingletonResponse<string>> CleanFullname([FromBody] FullnameCleanRequestModel model)
        //{
        //    return new SingletonResponse<string>(await _resourceService.PretreatmentAsync(model.Directory));
        //}

        [SwaggerOperation(OperationId = "ExtraSubdirectories")]
        [HttpPost("extra-subdirectories")]
        public BaseResponse ExtraSubdirectories([FromBody] SubdirectoriesExtractRequestModel model)
        {
            return FileService.ExtractFromSubDirectories(model.Path);
        }

        // [SwaggerOperation(OperationId = "RevertFileChanges")]
        // [HttpPost("revert-file-changes")]
        // public async Task<BaseResponse> RevertFileChanges([FromBody] FileChangesRevertRequestModel model)
        // {
        //     return await _fileService.RevertFileChanges(model.BatchId);
        // }
        //
        // [SwaggerOperation(OperationId = "GetAllFileChangeBatches")]
        // [HttpGet("file-change-batches")]
        // public async Task<ListResponse<string>> GetAllFileChangeBatches()
        // {
        //     return new ListResponse<string>(await _fileService.GetFileChangeBatchIdsAsync());
        // }

        // [SwaggerOperation(OperationId = "AnalyzeFullname")]
        // [HttpPost("fullname-analysis")]
        // public async Task<SingletonResponse<FullnameAnalysisResponseModel>> AnalyzeFullname(
        //     [FromBody] FullnameAnalyzeRequestModel model)
        // {
        //     var parser =
        //         await _categoryService.GetComponent<IParser>(model.CategoryId, BasicCategoryComponentType.Enhancer);
        //     var result = new FullnameAnalysisResponseModel
        //     {
        //         Resource1 = await parser.Parse(model.Fullname1),
        //     };
        //     if (model.Fullname2.IsNotEmpty())
        //     {
        //         result.Resource2 = await parser.Parse(model.Fullname2);
        //     }
        //     //
        //     // if (result.Resource1 != null && result.Resource2 != null)
        //     // {
        //     //     result.CompareResult = result.Resource1.Compare(result.Resource2);
        //     // }
        //
        //     return new SingletonResponse<FullnameAnalysisResponseModel>(result);
        // }

        [HttpGet("open")]
        [SwaggerOperation(OperationId = "OpenFileOrDirectory")]
        public BaseResponse Open(string path, bool openInDirectory)
        {
            FileService.Open(path, openInDirectory);
            return BaseResponseBuilder.Ok;
        }

        [HttpPost("remove-relay-directories")]
        [SwaggerOperation(OperationId = "RemoveRelayDirectories")]
        public async Task<BaseResponse> RemoveRelayDirectories(string root)
        {
            FileService.RemoveRelayDirectories(root);
            return BaseResponseBuilder.Ok;
        }

        [HttpPost("group-files-into-directories")]
        [SwaggerOperation(OperationId = "GroupFilesToDirectories")]
        public async Task<BaseResponse> GroupFilesToDirectories(string root)
        {
            FileService.GroupFilesToDirectories(root);
            return BaseResponseBuilder.Ok;
        }

        [HttpPost("everything-extraction")]
        [SwaggerOperation(OperationId = "StartExtractEverything")]
        public async Task<BaseResponse> ExtractEverything(string root)
        {
            await _toolService.StartExtractEverything(root);
            return BaseResponseBuilder.Ok;
        }

        [HttpDelete("everything-extraction")]
        [SwaggerOperation(OperationId = "StopExtractingEverything")]
        public async Task<BaseResponse> StopExtractingEverything()
        {
            await _toolService.StopExtractEverything();
            return BaseResponseBuilder.Ok;
        }

        [HttpGet("everything-extraction/status")]
        [SwaggerOperation(OperationId = "GetEverythingExtractionStatus")]
        public async Task<SingletonResponse<EverythingExtractionStatus>> GetEverythingExtractionStatus()
        {
            return new SingletonResponse<EverythingExtractionStatus>(_toolService.EverythingExtractionStatus);
        }

        [HttpGet("{id}/play")]
        [SwaggerOperation(OperationId = "PlayResourceFile")]
        public async Task<BaseResponse> Play(int id, string file)
        {
            return await _categoryService.Play(id, file);
        }

        [HttpGet("test")]
        [SwaggerOperation("Test")]
        public async Task<IActionResult> Test()
        {
            var im = Icon.ExtractAssociatedIcon(@"C:\Users\anoba\Downloads\WeChat Image_20220708164438.jpg");
            var ms = new MemoryStream();
            im.ToBitmap().Save(@"C:\Users\anoba\Downloads\myicon.png", ImageFormat.Png);
            im.ToBitmap().Save(ms, ImageFormat.Png);
            im.Dispose();
            ms.Seek(0, SeekOrigin.Begin);
            var type = MimeTypes.GetMimeType(".ico");
            return File(ms, type);
        }

        [HttpGet("cookie-validation")]
        [SwaggerOperation(OperationId = "ValidateCookie")]
        public async Task<BaseResponse> ValidateCookie(CookieValidatorTarget target, string cookie,
            [FromServices] IEnumerable<ICookieValidator> validators)
        {
            var list = validators.ToList();

            var candidates = list.Where(t => t.Target == target).ToList();
            if (candidates.Count > 1)
            {
                return BaseResponseBuilder.Build(ResponseCode.SystemError,
                    $"More than 1 validators are found for target: {target}. Validators: {string.Join(',', candidates.Select(t => t.GetType().Name))}");
            }

            if (!candidates.Any())
            {
                return BaseResponseBuilder.Build(ResponseCode.SystemError,
                    $"No validator is found for target: {target}. Existed validators are: {string.Join(',', list.Select(t => t.Target.ToString()))}");
            }

            var result = await candidates.FirstOrDefault()!.Validate(cookie);
            return result;
        }

        //[HttpGet("exhentai-downloader-error")]
        //[SwaggerOperation(OperationId = "GetExHentaiDownloaderError")]
        //public async Task<SingletonResponse<string>> GetExHentaiDownloaderError()
        //{
        //    return new SingletonResponse<string>(await _exHentaiService.GetLastDownloadingError());
        //}

        //[HttpGet("exhentai-image-limits")]
        //[SwaggerOperation(OperationId = "GetExhentaiImageLimits")]
        //public async Task<SingletonResponse<ExHentaiImageLimits>> GetExhentaiImageLimits()
        //{
        //    return new SingletonResponse<ExHentaiImageLimits>(await _exHentaiService.GetImageLimits());
        //}
    }
}