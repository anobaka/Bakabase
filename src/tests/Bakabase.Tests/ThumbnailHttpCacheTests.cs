using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Bakabase.Service.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Bakabase.Tests;

[TestClass]
public sealed class ThumbnailHttpCacheTests
{
    private string _directory = null!;
    private string _path = null!;
    private static readonly DateTime Modified = new(2026, 10, 18, 1, 2, 3, DateTimeKind.Utc);

    [TestInitialize]
    public async Task Setup()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"ThumbnailHttpCache.{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
        _path = Path.Combine(_directory, "cover.bmp");
        await WriteImage(new Rgba32(255, 0, 0), Modified.AddMilliseconds(100));
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_directory, true);

    private async Task WriteImage(Rgba32 color, DateTime modified)
    {
        using var image = new Image<Rgba32>(2, 2, color);
        await image.SaveAsBmpAsync(_path);
        File.SetLastWriteTimeUtc(_path, modified);
    }

    private static ToolController Controller(string? tag = null, string? modified = null)
    {
        var controller = new ToolController(null!, null!)
        {
            ControllerContext = new ControllerContext {HttpContext = new DefaultHttpContext()}
        };
        if (tag != null) controller.Request.Headers.IfNoneMatch = tag;
        if (modified != null) controller.Request.Headers.IfModifiedSince = modified;
        return controller;
    }

    private static async Task<byte[]> ReadBody(IActionResult result)
    {
        Assert.IsInstanceOfType<FileStreamResult>(result);
        await using var stream = ((FileStreamResult) result).FileStream;
        using var output = new MemoryStream();
        await stream.CopyToAsync(output);
        return output.ToArray();
    }

    [TestMethod]
    public void ThumbnailResponses_RequireRevalidationInsteadOfFreshnessCaching()
    {
        var policy = typeof(ToolController).GetMethod(nameof(ToolController.GetThumbnail))!
            .GetCustomAttribute<ResponseCacheAttribute>()!;
        Assert.AreEqual(ResponseCacheLocation.None, policy.Location);
        Assert.IsFalse(policy.NoStore, "Unchanged images should remain reusable through conditional requests.");
    }

    [TestMethod]
    public async Task UnchangedFile_UsesConditional304WithoutResendingImage()
    {
        var first = Controller();
        await ReadBody(await first.GetThumbnail(_path, 600, 600));
        var tag = first.Response.Headers.ETag.ToString();
        Assert.IsTrue(tag.StartsWith("W/\"thumbnail-2-"));
        Assert.IsFalse(string.IsNullOrEmpty(first.Response.Headers.LastModified));

        var repeated = Controller(tag);
        var response = await repeated.GetThumbnail(_path, 600, 600);
        Assert.IsInstanceOfType<StatusCodeResult>(response);
        Assert.AreEqual(StatusCodes.Status304NotModified, ((StatusCodeResult) response).StatusCode);
        Assert.AreEqual(tag, repeated.Response.Headers.ETag.ToString());
    }

    [TestMethod]
    public async Task SameSizeReplacementWithinOneSecond_ChangesValidatorAndOverridesOldDate()
    {
        var first = Controller();
        var oldBytes = await ReadBody(await first.GetThumbnail(_path, 600, 600));
        var tag = first.Response.Headers.ETag.ToString();
        var date = first.Response.Headers.LastModified.ToString();
        await WriteImage(new Rgba32(0, 0, 255), Modified.AddMilliseconds(200));

        var replaced = Controller(tag, date);
        var newBytes = await ReadBody(await replaced.GetThumbnail(_path, 600, 600));
        Assert.AreEqual(oldBytes.Length, newBytes.Length);
        CollectionAssert.AreNotEqual(oldBytes, newBytes);
        Assert.AreNotEqual(tag, replaced.Response.Headers.ETag.ToString());
        Assert.AreEqual(date, replaced.Response.Headers.LastModified.ToString());
    }

    [TestMethod]
    public async Task DateOnlyValidator_DoesNotMaskSubsecondReplacement()
    {
        var first = Controller();
        await ReadBody(await first.GetThumbnail(_path, 600, 600));
        var date = first.Response.Headers.LastModified.ToString();
        await WriteImage(new Rgba32(0, 0, 255), Modified.AddMilliseconds(200));
        var replaced = Controller(modified: date);
        await ReadBody(await replaced.GetThumbnail(_path, 600, 600));

        // Exact whole-second source timestamps can still use Last-Modified on their own.
        File.SetLastWriteTimeUtc(_path, Modified);
        var unchanged = Controller(modified: date);
        var response = await unchanged.GetThumbnail(_path, 600, 600);
        Assert.IsInstanceOfType<StatusCodeResult>(response);
        Assert.AreEqual(StatusCodes.Status304NotModified, ((StatusCodeResult) response).StatusCode);
    }

    [TestMethod]
    public async Task DifferentThumbnailSize_DoesNotReuseAnotherRepresentationValidator()
    {
        var first = Controller();
        await ReadBody(await first.GetThumbnail(_path, 600, 600));
        var resized = Controller(first.Response.Headers.ETag.ToString());
        await ReadBody(await resized.GetThumbnail(_path, 300, 300));
        Assert.AreNotEqual(first.Response.Headers.ETag.ToString(), resized.Response.Headers.ETag.ToString());
    }

    [TestMethod]
    public async Task RemovedFile_DoesNotReturn304ForPreviouslyCachedImage()
    {
        var first = Controller();
        await ReadBody(await first.GetThumbnail(_path, 600, 600));
        File.Delete(_path);
        var removed = Controller(first.Response.Headers.ETag.ToString());
        Assert.IsInstanceOfType<NotFoundResult>(await removed.GetThumbnail(_path, 600, 600));
    }
}
