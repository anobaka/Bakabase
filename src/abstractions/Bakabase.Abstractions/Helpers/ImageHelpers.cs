using Bakabase.Abstractions.Components;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Cover;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Drawing.Imaging;
using System.Drawing;

namespace Bakabase.Abstractions.Helpers;

public static class ImageHelpers
{
    public static async Task<string> SaveAsThumbnail<T>(this Image<T> image, string pathWithoutExtension,
        CancellationToken ct) where T : unmanaged, IPixel<T>
    {
        var hasAlpha = image.Metadata.GetPngMetadata().ColorType == PngColorType.RgbWithAlpha ||
                       image.Metadata.GetPngMetadata().ColorType == PngColorType.GrayscaleWithAlpha;
        if (image.Width >= InternalOptions.MaxThumbnailWidth ||
            image.Height >= InternalOptions.MaxThumbnailHeight)
        {
            var scale = Math.Min((decimal) InternalOptions.MaxThumbnailWidth / image.Width,
                (decimal) InternalOptions.MaxThumbnailHeight / image.Height);
            image.Mutate(t => t.Resize((int) (image.Width * scale), (int) (image.Height * scale)));
        }

        var thumbnailPath = pathWithoutExtension + (hasAlpha ? ".png" : ".jpg");
        var dir = Path.GetDirectoryName(thumbnailPath)!;
        Directory.CreateDirectory(dir);
        if (hasAlpha)
        {
            await image.SaveAsPngAsync(thumbnailPath, ct);
        }
        else
        {
            await image.SaveAsJpegAsync(thumbnailPath, ct);
        }

        return thumbnailPath;
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