using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using ZXing;

namespace Bakabase.Service.Components.PostSources;

public sealed record DecodedPostImageSource(string Url, string MethodId);
public sealed record PostImageDecodeResult(string Status, IReadOnlyList<DecodedPostImageSource> Sources)
{
    internal static PostImageDecodeResult Empty(string status) => new(status, []);
}

internal static class PostSourceQr
{
    internal const string Client = "Bakabase.SavedPostImage";
    internal const int MaxBytes = 4 * 1024 * 1024;
    internal const int MaxBase64Length = (MaxBytes + 2) / 3 * 4;
    private const long MaxPixels = 12_000_000;
    private static readonly SemaphoreSlim Decoders = new(2);

    internal static async Task<PostImageDecodeResult> ReadAsync(HttpClient client, SavedPostImage image,
        string? imageData, CancellationToken ct)
    {
        // A cancelled dialog should release its reader. Limit in-flight compressed bytes and decodes together.
        await Decoders.WaitAsync(ct);
        try
        {
            byte[] bytes;
            if (imageData != null)
            {
                if (imageData.Length > MaxBase64Length) return PostImageDecodeResult.Empty("imageTooLarge");
                try { bytes = Convert.FromBase64String(imageData); }
                catch (System.FormatException) { return PostImageDecodeResult.Empty("unsupportedImage"); }
                if (bytes.Length > MaxBytes) return PostImageDecodeResult.Empty("imageTooLarge");
            }
            else
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
                deadline.CancelAfter(TimeSpan.FromSeconds(18));
                try
                {
                    using var response = await client.GetAsync(image.Url, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
                    if (!response.IsSuccessStatusCode) return PostImageDecodeResult.Empty("imageUnavailable");
                    if (response.Content.Headers.ContentLength > MaxBytes) return PostImageDecodeResult.Empty("imageTooLarge");
                    using var content = await response.Content.ReadAsStreamAsync(deadline.Token);
                    using var buffer = new MemoryStream();
                    var chunk = new byte[32768];
                    int length;
                    while ((length = await content.ReadAsync(chunk, deadline.Token)) != 0)
                    {
                        if (buffer.Length + length > MaxBytes) return PostImageDecodeResult.Empty("imageTooLarge");
                        buffer.Write(chunk, 0, length);
                    }
                    bytes = buffer.ToArray();
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return PostImageDecodeResult.Empty("imageUnavailable"); }
                catch (Exception e) when (e is HttpRequestException or IOException or ArgumentException)
                { return PostImageDecodeResult.Empty("imageUnavailable"); }
            }
            return Decode(bytes, ct);
        }
        finally { Decoders.Release(); }
    }

    internal static PostImageDecodeResult Decode(byte[] bytes, CancellationToken ct)
    {
        if (bytes.Length > MaxBytes) return PostImageDecodeResult.Empty("imageTooLarge");
        if (bytes.Length == 0) return PostImageDecodeResult.Empty("unsupportedImage");
        ct.ThrowIfCancellationRequested();
        var options = new DecoderOptions { MaxFrames = 1, SkipMetadata = true };
        try
        {
            // Only raster pixels enter ZXing. Reject other codecs before Identify/Load and
            // skip all metadata, including lazy ICC entries, in the shared v3 ImageSharp pipeline.
            if (Image.DetectFormat(bytes).Name is not ("PNG" or "JPEG" or "GIF" or "Webp" or "BMP"))
                return PostImageDecodeResult.Empty("unsupportedImage");
            var info = Image.Identify(options, bytes);
            if ((long)info.Width * info.Height > MaxPixels || info.Width > 8192 || info.Height > 8192)
                return PostImageDecodeResult.Empty("imageTooLarge");
            using var image = Image.Load<L8>(options, bytes);
            var codes = Scan(image, ct);
            if (codes.Length == 0 && Math.Min(image.Width, image.Height) >= 128)
            {
                // One bounded resample helps rasterized QR modules with aliased or stylized edges.
                var scale = Math.Min(.75, 1024d / Math.Max(image.Width, image.Height));
                image.Mutate(x => x.Resize(Math.Max(1, (int)(image.Width * scale)), Math.Max(1, (int)(image.Height * scale))));
                codes = Scan(image, ct);
            }
            ct.ThrowIfCancellationRequested();
            if (codes is not { Length: > 0 }) return PostImageDecodeResult.Empty("noQrCode");
            var sources = codes.Select(c => SupportedSource(c.Text)).OfType<DecodedPostImageSource>().Distinct().ToArray();
            return new(sources.Length == 0 ? "unsupportedSource" : "decoded", sources);
        }
        catch (Exception e) when (e is UnknownImageFormatException or InvalidImageContentException or
            NotSupportedException or ArgumentException or EndOfStreamException)
        { return PostImageDecodeResult.Empty("unsupportedImage"); }
    }

    private static Result[] Scan(Image<L8> image, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var pixels = new byte[checked(image.Width * image.Height)];
        image.CopyPixelDataTo(pixels);
        var reader = new BarcodeReaderGeneric { AutoRotate = true };
        reader.Options.PossibleFormats = [BarcodeFormat.QR_CODE];
        reader.Options.TryHarder = true;
        reader.Options.TryInverted = true;
        var luminance = new RGBLuminanceSource(pixels, image.Width, image.Height, RGBLuminanceSource.BitmapFormat.Gray8);
        var codes = reader.DecodeMultiple(luminance);
        return codes is {Length: > 0} ? codes : reader.Decode(luminance) is { } single ? [single] : [];
    }

    private static DecodedPostImageSource? SupportedSource(string? text)
    {
        var url = text?.Trim();
        return PostSourceUrl.MatchMethod(url) is { } method ? new(url!, method) : null;
    }
}
