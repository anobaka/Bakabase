using Bakabase.Abstractions.Components;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Cover;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using StackExchange.Profiling;
using System.Drawing.Imaging;
using System.Drawing;

namespace Bakabase.Abstractions.Helpers;

public static class ImageHelpers
{
    public static async Task<string> SaveAsThumbnail<T>(this Image<T> image, string pathWithoutExtension,
        CancellationToken ct) where T : unmanaged, IPixel<T>
    {
        using (MiniProfiler.Current.Step("SaveAsThumbnail.Inner"))
        {
            bool hasAlpha;
            using (MiniProfiler.Current.Step("CheckAlphaChannel"))
            {
                hasAlpha = image.Metadata.GetPngMetadata().ColorType == PngColorType.RgbWithAlpha ||
                           image.Metadata.GetPngMetadata().ColorType == PngColorType.GrayscaleWithAlpha;
            }

            using (MiniProfiler.Current.Step($"CheckAndResize (original: {image.Width}x{image.Height})"))
            {
                if (image.Width >= InternalOptions.MaxThumbnailWidth ||
                    image.Height >= InternalOptions.MaxThumbnailHeight)
                {
                    var scale = Math.Min((decimal) InternalOptions.MaxThumbnailWidth / image.Width,
                        (decimal) InternalOptions.MaxThumbnailHeight / image.Height);
                    var newWidth = (int) (image.Width * scale);
                    var newHeight = (int) (image.Height * scale);
                    using (MiniProfiler.Current.Step($"Resize to {newWidth}x{newHeight}"))
                    {
                        image.Mutate(t => t.Resize(newWidth, newHeight));
                    }
                }
            }

            var thumbnailPath = pathWithoutExtension + (hasAlpha ? ".png" : ".jpg");
            var dir = Path.GetDirectoryName(thumbnailPath)!;

            using (MiniProfiler.Current.Step("CreateDirectory"))
            {
                Directory.CreateDirectory(dir);
            }

            if (hasAlpha)
            {
                using (MiniProfiler.Current.Step($"SaveAsPngAsync ({image.Width}x{image.Height})"))
                {
                    await image.SaveAsPngAsync(thumbnailPath, ct);
                }
            }
            else
            {
                using (MiniProfiler.Current.Step($"SaveAsJpegAsync ({image.Width}x{image.Height})"))
                {
                    await image.SaveAsJpegAsync(thumbnailPath, ct);
                }
            }

            return thumbnailPath;
        }
    }

    public static byte[]? ExtractIconAsPng(string path)
    {
        try
        {
            using var icon = File.Exists(path) ? Icon.ExtractAssociatedIcon(path) : DefaultIcons.GetStockIcon(3, 0x04);

            if (icon != null)
            {
                var ms = new MemoryStream();
                // Ico encoder is not found.
                icon.ToBitmap().Save(ms, ImageFormat.Png);
                icon.Dispose();
                return ms.ToArray();
            }
        }
        catch (Exception)
        {
            // ignored
        }

        return null;
    }
}