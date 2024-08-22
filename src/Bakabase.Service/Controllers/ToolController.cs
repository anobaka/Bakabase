using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Cover;
using Bakabase.Infrastructures.Components.Storage.Services;
using Bakabase.InsideWorld.Business.Components.CookieValidation.Infrastructures;
using Bakabase.InsideWorld.Models.Constants;
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
            var result = await coverDiscoverer.Discover(path, CoverSelectOrder.FilenameAscending, true,
                HttpContext.RequestAborted);
            if (result == null)
            {
                return NotFound();
            }
            else
            {
                if (!w.HasValue && !h.HasValue)
                {
                    var contentType = MimeTypes.GetMimeType(result.Ext);
                    if (result.Data != null)
                    {
                        return File(result.Data, contentType);
                    }

                    return File(System.IO.File.OpenRead(result.Path), contentType);
                }

                var img = result.Data != null
                    ? await Image.LoadAsync<Argb32>(new MemoryStream(result.Data))
                    : await Image.LoadAsync<Argb32>(result.Path);

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
    }
}