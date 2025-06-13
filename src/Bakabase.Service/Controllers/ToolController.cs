using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Cover;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Helpers;
using Bakabase.Infrastructures.Components.Storage.Services;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.ThirdParty.Abstractions.Http.Cookie;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.Constants;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using MimeKit;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Swashbuckle.AspNetCore.Annotations;
using Image = SixLabors.ImageSharp.Image;

namespace Bakabase.Service.Controllers
{
    [Route("~/tool")]
    public class ToolController(ICoverDiscoverer coverDiscoverer) : Controller
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

        [HttpGet("thumbnail")]
        [SwaggerOperation(OperationId = "GetThumbnail")]
        [ResponseCache(VaryByQueryKeys = [nameof(path), nameof(w), nameof(h)], Duration = 30 * 60)]
        public async Task<IActionResult> GetThumbnail(string path, int? w, int? h)
        {
            var isFile = System.IO.File.Exists(path);
            if (isFile)
            {
                var ext = Path.GetExtension(path);
                if (InternalOptions.ImageExtensions.Contains(ext))
                {
                    if (!w.HasValue && !h.HasValue)
                    {
                        var contentType = MimeTypes.GetMimeType(ext);
                        return File(System.IO.File.OpenRead(path), contentType);
                    }

                    var img = await Image.LoadAsync<Argb32>(path);

                    var scale = 1m;
                    if (w > 0 && img.Width > w)
                    {
                        scale = Math.Min(scale, (decimal) w / img.Width);
                    }

                    if (h > 0 && img.Height > h)
                    {
                        scale = Math.Min(scale, (decimal) h / img.Height);
                    }

                    var ms = new MemoryStream();

                    if (scale < 1)
                    {
                        img.Mutate(x => x.Resize((int) (img.Width * scale), (int) (img.Height * scale)));
                    }

                    await img.SaveAsPngAsync(ms, HttpContext.RequestAborted);
                    ms.Seek(0, SeekOrigin.Begin);
                    return File(ms, MimeTypes.GetMimeType(".png"));
                }

            }

            if (isFile || Directory.Exists(path))
            {
                var iconData = ImageHelpers.ExtractIconAsPng(path);
                if (iconData != null)
                {
                    return File(iconData, MimeTypes.GetMimeType(".png"));
                }
            }

            return NotFound();
        }

        [HttpPost("match-all")]
        [SwaggerOperation(OperationId = "TestMatchAll")]
        public async Task<SingletonResponse<Dictionary<string, List<string>>>> TestMatchAll(string regex, string text)
        {
            var r = new Regex(regex);
            var groupValues = r.MatchAllAndMergeByNamedGroups(text);
            return new SingletonResponse<Dictionary<string, List<string>>>(groupValues);
        }

        [HttpGet("open-file")]
        [SwaggerOperation(OperationId = "OpenFile")]
        public async Task<BaseResponse> OpenFile(string path)
        {
            var p = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                }
            };
            p.Start();
            return BaseResponseBuilder.Ok;
        }
    }
}