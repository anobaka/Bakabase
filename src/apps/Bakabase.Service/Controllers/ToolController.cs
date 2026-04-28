using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Helpers;
using Bakabase.Abstractions.Services;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Infrastructures.Components.Gui;
using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.Modules.ThirdParty.Abstractions.Http.Cookie;
using Bakabase.Modules.ThirdParty.Helpers;
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
    public class ToolController(IResourceService resourceService, CompressedFileService compressedFileService) : Controller
    {
        [HttpGet("open")]
        [SwaggerOperation(OperationId = "OpenFileOrDirectory")]
        public BaseResponse Open(string path, bool openInDirectory)
        {
            OsShell.Open(path, openInDirectory);
            return BaseResponseBuilder.Ok;
        }

        [HttpPost("cookie-capture")]
        [SwaggerOperation(OperationId = "CaptureCookie")]
        public async Task<SingletonResponse<CookieCaptureResult>> CaptureCookie(
            CookieValidatorTarget target,
            [FromServices] IGuiAdapter guiAdapter,
            [FromServices] IEnumerable<ICookieCaptureFlow> captureFlows,
            [FromServices] IBakabaseLocalizer localizer)
        {
            var flow = captureFlows.FirstOrDefault(f => f.Target == target);
            if (flow == null)
            {
                return SingletonResponseBuilder<CookieCaptureResult>.Build(ResponseCode.NotFound,
                    $"Cookie capture is not supported for target: {target}");
            }

            var labels = new Dictionary<string, string>
            {
                ["confirm"] = localizer["CookieCapture_Confirm"],
                ["cancel"] = localizer["CookieCapture_Cancel"],
                ["waitingForLogin"] = localizer["CookieCapture_WaitingForLogin"],
                ["extractingCookies"] = localizer["CookieCapture_ExtractingCookies"],
                ["error"] = localizer["CookieCapture_Error"],
            };

            var cookie = await guiAdapter.CaptureWebViewCookiesAsync(
                flow.StartUrl,
                localizer["CookieCapture_LoginTo", flow.PlatformName],
                flow.CookieUrls,
                labels);

            if (cookie == null)
            {
                // Success + no data avoids HTTP 400 / global error toast; message explains cancel or unsupported environment.
                return new SingletonResponse<CookieCaptureResult>(null)
                {
                    Code = (int)ResponseCode.Success,
                    Message = localizer["CookieCapture_Cancelled"],
                };
            }

            // Return cookie along with the WebView's User-Agent and inferred TLS preset
            var webViewUserAgent = GetWebViewUserAgent();
            var tlsPreset = TlsPresetHelper.InferPresetFromUserAgent(webViewUserAgent);

            return new SingletonResponse<CookieCaptureResult>(new CookieCaptureResult
            {
                Cookie = cookie,
                UserAgent = webViewUserAgent,
                TlsPreset = tlsPreset,
            });
        }

        [HttpGet("tls-presets")]
        [SwaggerOperation(OperationId = "GetTlsPresets")]
        public TlsPresetInfo[] GetTlsPresets()
        {
            return TlsPresetHelper.AvailablePresets;
        }

        [HttpGet("cookie-validation")]
        [SwaggerOperation(OperationId = "ValidateCookie")]
        public async Task<BaseResponse> ValidateCookie(CookieValidatorTarget target, string cookie,
            string? userAgent, string? tlsPreset,
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

            var result = await candidates.FirstOrDefault()!.Validate(cookie, userAgent, tlsPreset);
            return result;
        }

        [HttpGet("thumbnail")]
        [SwaggerOperation(OperationId = "GetThumbnail")]
        [ResponseCache(VaryByQueryKeys = [nameof(path), nameof(w), nameof(h)], Duration = 30 * 60)]
        public async Task<IActionResult> GetThumbnail(string path, int? w, int? h)
        {
            // Check if path contains compressed file separator (e.g., "archive.zip!folder/image.jpg")
            string? compressedFilePath = null;
            string? entryPath = null;

            if (path.Contains(InternalOptions.CompressedFileRootSeparator))
            {
                // Find the compressed file extension followed by the separator
                foreach (var compressedExt in InternalOptions.CompressedFileExtensions)
                {
                    var pattern = $"{compressedExt}{InternalOptions.CompressedFileRootSeparator}";
                    var idx = path.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
                    if (idx > 0)
                    {
                        var separatorPos = idx + compressedExt.Length;
                        compressedFilePath = path[..separatorPos];
                        entryPath = path[(separatorPos + 1)..]; // +1 to skip '!'
                        break;
                    }
                }
            }

            // Handle compressed file entry
            if (compressedFilePath != null && entryPath != null)
            {
                return await GetThumbnailFromCompressedFile(compressedFilePath, entryPath, w, h);
            }

            // Regular file handling
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

        private async Task<IActionResult> GetThumbnailFromCompressedFile(string compressedFilePath, string entryPath, int? w, int? h)
        {
            // Check if the compressed file exists
            if (!System.IO.File.Exists(compressedFilePath))
            {
                return NotFound();
            }

            // Check if the entry is an image
            var ext = Path.GetExtension(entryPath);
            if (!InternalOptions.ImageExtensions.Contains(ext))
            {
                // For non-image files in compressed archives, return the archive icon
                var iconData = ImageHelpers.ExtractIconAsPng(compressedFilePath);
                if (iconData != null)
                {
                    return File(iconData, MimeTypes.GetMimeType(".png"));
                }
                return NotFound();
            }

            // Extract the file from the archive
            var extractedStream = await compressedFileService.ExtractOneEntry(compressedFilePath, entryPath, HttpContext.RequestAborted);
            if (extractedStream == null || extractedStream.Length == 0)
            {
                return NotFound();
            }

            try
            {
                // Load the image from the extracted stream
                var img = await Image.LoadAsync<Argb32>(extractedStream, HttpContext.RequestAborted);

                // Calculate scale if dimensions are specified
                var scale = 1m;
                if (w > 0 && img.Width > w)
                {
                    scale = Math.Min(scale, (decimal) w / img.Width);
                }

                if (h > 0 && img.Height > h)
                {
                    scale = Math.Min(scale, (decimal) h / img.Height);
                }

                var outputMs = new MemoryStream();

                if (scale < 1)
                {
                    img.Mutate(x => x.Resize((int) (img.Width * scale), (int) (img.Height * scale)));
                }

                await img.SaveAsPngAsync(outputMs, HttpContext.RequestAborted);
                outputMs.Seek(0, SeekOrigin.Begin);
                return File(outputMs, MimeTypes.GetMimeType(".png"));
            }
            finally
            {
                await extractedStream.DisposeAsync();
            }
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

        [HttpGet("generate-files-to-embedded")]
        [SwaggerOperation(OperationId = "GenerateFilesToEmbedded")]
        public async Task<BaseResponse> GenerateFilesToEmbedded(string dir)
        {
            var folderPath = Path.Combine(dir, "raw");
            var resources = await resourceService.GetAll(null, ResourceAdditionalItem.All);
            foreach(var r in resources) {
              var filePath = Path.Combine(folderPath, $"{r.Id}.txt");
              var content = new string?[]
              {
                  r.DisplayName,
              };
            }

            return BaseResponseBuilder.Ok;
        }

        /// <summary>
        /// Returns the User-Agent string used by the embedded WebView on the current platform.
        /// This is a known constant since the WebView UA is explicitly set during initialization.
        /// </summary>
        private static string GetWebViewUserAgent()
        {
            if (OperatingSystem.IsMacOS())
                return "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36";
            if (OperatingSystem.IsLinux())
                return "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36";
            // Windows (default)
            return "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36";
        }
    }

    public class CookieCaptureResult
    {
        public string Cookie { get; set; } = string.Empty;
        public string? UserAgent { get; set; }
        public string? TlsPreset { get; set; }
    }
}