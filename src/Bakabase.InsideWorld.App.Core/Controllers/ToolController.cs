using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Abstractions.Services;
using Bakabase.Infrastructures.Components.Storage.Services;
using Bakabase.InsideWorld.Business.Components.CookieValidation.Infrastructures;
using Bakabase.InsideWorld.Business.Components.Resource.Components.PropertyMatcher;
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
    public class ToolController() : Controller
    {
        [HttpGet("open")]
        [SwaggerOperation(OperationId = "OpenFileOrDirectory")]
        public BaseResponse Open(string path, bool openInDirectory)
        {
            FileService.Open(path, openInDirectory);
            return BaseResponseBuilder.Ok;
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
    }
}