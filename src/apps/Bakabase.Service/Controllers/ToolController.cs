using System;
using System.Collections.Generic;
using System.Collections.Immutable;
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
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Bakabase.Service.Components.RemoteAccess;
using Swashbuckle.AspNetCore.Annotations;
using Image = SixLabors.ImageSharp.Image;
using Bakabase.Modules.RemoteAccess.Abstractions.Components;
using Bakabase.Modules.RemoteAccess.Components.Pairing;

namespace Bakabase.Service.Controllers
{
    [Route("~/tool")]
    public class ToolController(IResourceService resourceService, CompressedFileService compressedFileService) : Controller
    {
        [RunsOnUserMachine(Reason = "Opening a file or folder happens on the machine you are sitting at.")]
        [HttpGet("open")]
        [SwaggerOperation(OperationId = "OpenFileOrDirectory")]
        public BaseResponse Open(string path, bool openInDirectory)
        {
            if (OperatingSystem.IsLinux() && openInDirectory) LinuxFileManager.RevealInParentDirectory(path);
            else OsShell.Open(path, openInDirectory);
            return BaseResponseBuilder.Ok;
        }

        [RunsOnUserMachine(Reason = "The sign-in window has to open on the machine you are sitting at.")]
        [HttpPost("cookie-capture")]
        [SwaggerOperation(OperationId = "CaptureCookie")]
        public async Task<SingletonResponse<CookieCaptureResult>> CaptureCookie(
            CookieValidatorTarget target,
            [FromServices] CookieCaptureOrchestrator orchestrator,
            [FromServices] IEnumerable<ICookieCaptureFlow> captureFlows,
            [FromServices] IBakabaseLocalizer localizer)
        {
            var flow = captureFlows.FirstOrDefault(f => f.Target == target);
            if (flow == null)
            {
                return SingletonResponseBuilder<CookieCaptureResult>.Build(ResponseCode.NotFound,
                    $"Cookie capture is not supported for target: {target}");
            }

            var captured = await orchestrator.CaptureAsync(flow);

            if (captured == null)
            {
                // Success + no data avoids HTTP 400 / global error toast; message explains cancel or unsupported environment.
                return new SingletonResponse<CookieCaptureResult>(null)
                {
                    Code = (int)ResponseCode.Success,
                    Message = localizer["CookieCapture_Cancelled"],
                };
            }


            return new SingletonResponse<CookieCaptureResult>(captured);
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
        // The token is part of the cache key even though the action ignores it. A signed
        // URL carries no Authorization header, so unlike a header-signed request it does
        // read and write this cache — and without varying on the token, a thumbnail a
        // paired device pulled would afterwards be served to an anonymous caller asking
        // for the same path.
        [ResponseCache(VaryByQueryKeys = [nameof(path), nameof(w), nameof(h), SignedMediaUrl.QueryKey],
            Duration = 30 * 60)]
        [RemoteAccessible(PathParameters = [nameof(path)])]
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
                    var contentType = MimeTypes.GetMimeType(ext);

                    // No size asked for, or a format we never re-encode: hand back the
                    // bytes on disk untouched.
                    if ((!w.HasValue && !h.HasValue) || !ResizableImageExtensions.Contains(ext))
                    {
                        return File(System.IO.File.OpenRead(path), contentType);
                    }

                    // The header alone says whether a resize is needed. When it isn't,
                    // streaming the original beats decoding and re-encoding it.
                    var info = await Image.IdentifyAsync(path, HttpContext.RequestAborted);
                    var scale = GetDownscaleFactor(info.Width, info.Height, w, h);
                    if (scale >= 1)
                    {
                        return File(System.IO.File.OpenRead(path), contentType);
                    }

                    using var img = await Image.LoadAsync(path, HttpContext.RequestAborted);
                    img.Mutate(x => x.Resize((int) (img.Width * scale), (int) (img.Height * scale)));

                    var ms = new MemoryStream();
                    await img.SaveAsync(ms, GetThumbnailEncoder(ext), HttpContext.RequestAborted);
                    ms.Seek(0, SeekOrigin.Begin);
                    return File(ms, contentType);
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

        /// <summary>
        /// Formats we are willing to decode and re-encode when a caller asks for a
        /// smaller thumbnail. The rest of <see cref="InternalOptions.ImageExtensions"/>
        /// is streamed untouched: .svg and .ico cannot be decoded here at all, and
        /// .gif / .bmp / .tiff would either lose animation or come back larger than
        /// they went in.
        /// </summary>
        private static readonly ImmutableHashSet<string> ResizableImageExtensions =
            ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, ".jpg", ".jpeg", ".png", ".webp");

        /// <summary>
        /// 1 means "already small enough" — the caller should stream the original
        /// rather than pay for a decode and re-encode.
        /// </summary>
        private static decimal GetDownscaleFactor(int width, int height, int? w, int? h)
        {
            var scale = 1m;
            if (w > 0 && width > w)
            {
                scale = Math.Min(scale, (decimal) w / width);
            }

            if (h > 0 && height > h)
            {
                scale = Math.Min(scale, (decimal) h / height);
            }

            return scale;
        }

        /// <summary>
        /// Keeps the source format so the response stays the content type the caller
        /// asked for. Re-encoding everything to PNG (which this endpoint used to do)
        /// makes photographic covers several times larger than the file on disk.
        /// </summary>
        private static IImageEncoder GetThumbnailEncoder(string ext) => ext.ToLowerInvariant() switch
        {
            ".png" => new PngEncoder(),
            ".webp" => new WebpEncoder(),
            _ => new JpegEncoder {Quality = 85}
        };

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

            var contentType = MimeTypes.GetMimeType(ext);

            // Same rule as the on-disk path: only decode when a resize is actually
            // needed and the format is one we re-encode; otherwise pass the extracted
            // bytes straight through.
            if ((!w.HasValue && !h.HasValue) || !ResizableImageExtensions.Contains(ext))
            {
                extractedStream.Seek(0, SeekOrigin.Begin);
                return File(extractedStream, contentType);
            }

            try
            {
                var info = await Image.IdentifyAsync(extractedStream, HttpContext.RequestAborted);
                var scale = GetDownscaleFactor(info.Width, info.Height, w, h);
                extractedStream.Seek(0, SeekOrigin.Begin);
                if (scale >= 1)
                {
                    return File(extractedStream, contentType);
                }

                using var img = await Image.LoadAsync(extractedStream, HttpContext.RequestAborted);
                img.Mutate(x => x.Resize((int) (img.Width * scale), (int) (img.Height * scale)));

                var outputMs = new MemoryStream();
                await img.SaveAsync(outputMs, GetThumbnailEncoder(ext), HttpContext.RequestAborted);
                outputMs.Seek(0, SeekOrigin.Begin);
                await extractedStream.DisposeAsync();
                return File(outputMs, contentType);
            }
            catch
            {
                await extractedStream.DisposeAsync();
                throw;
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

        [RunsOnUserMachine(Reason = "Opening a file happens on the machine you are sitting at.")]
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
    }

}
