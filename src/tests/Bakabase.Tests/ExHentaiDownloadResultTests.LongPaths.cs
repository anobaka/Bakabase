using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.ExHentai;

namespace Bakabase.Tests;

public sealed partial class ExHentaiDownloadResultTests
{
    [TestMethod]
    public async Task ReportedLongTorrentTitleDownloadsWithoutChainedTemporarySuffixes()
    {
        const string title = "[Akase34] Amelia Rosequartz | My Status as an Assassin Obviously Exceeds the Hero's (Ansatsusha de Aru Ore no Status ga Yuusha yori mo Akiraka ni Tsuyoi no da ga) | 138 pics (Patreon) [AI Generated]";
        var handler = new GalleryHandler {GalleryName = title, TorrentBytes = _metadata};
        using var producer = await BuildProducer(handler);
        await RunProducer(producer, "https://exhentai.org/g/4215763/abcdef0123/", _ => Task.CompletedTask,
            resultWorkflowId: null);

        var result = (await _results.GetByTaskAsync(10)).Single();
        var path = ExHentaiDownloadResultHelper.GetTorrentDownloadPath(result)!;
        var expected = "[Misc] " + title.Replace('|', '_') + " [g4215763].torrent";
        Assert.AreEqual(224, expected.Length);
        Assert.AreEqual(Path.Combine(_root, expected), path);
        CollectionAssert.AreEqual(_metadata, await File.ReadAllBytesAsync(path));
        Assert.AreEqual(1, handler.TorrentRequests);
        AssertNoTemporaryFiles();
    }

    [TestMethod]
    [DataRow("ascii")]
    [DataRow("cjk")]
    [DataRow("emoji")]
    public async Task OversizedTorrentNamesPersistActualCopiesAndResumeWithoutAnotherRequest(string kind)
    {
        var title = kind switch
        {
            "cjk" => new string('画', 150),
            "emoji" => string.Concat(Enumerable.Repeat("🎨", 100)),
            _ => new string('a', 300)
        };
        var handler = new GalleryHandler {GalleryName = title, TorrentBytes = _metadata};
        using var producer = await BuildProducer(handler);
        await RunProducer(producer, "https://exhentai.org/g/12345/abcdef0123/", _ => Task.CompletedTask,
            resultWorkflowId: null);
        await RunProducer(producer, "https://exhentai.org/g/12346/abcdef0123/", _ => Task.CompletedTask,
            resultWorkflowId: null);

        var results = await _results.GetByTaskAsync(10);
        var paths = results.Select(ExHentaiDownloadResultHelper.GetTorrentDownloadPath).ToArray();
        Assert.AreEqual(2, paths.Length);
        Assert.AreNotEqual(paths[0], paths[1]);
        foreach (var path in paths)
        {
            Assert.AreEqual(_root, Path.GetDirectoryName(path));
            var name = Path.GetFileName(path)!;
            Assert.IsTrue(name.Length <= 255);
            Assert.IsTrue(new UTF8Encoding(false, true).GetByteCount(name) <= 255);
            StringAssert.Contains(name, " [g");
            CollectionAssert.AreEqual(_metadata, await File.ReadAllBytesAsync(path!));
        }
        AssertNoTemporaryFiles();

        var resumed = new List<string>();
        using var retry = await BuildProducer(new GalleryHandler {RejectRequests = true}, "Changed/{PageTitle}{Extension}");
        retry.OnFileDownloaded += (path, _) => {resumed.Add(path); return Task.CompletedTask;};
        await RunProducer(retry, "https://exhentai.org/g/12345/abcdef0123/", _ => Task.CompletedTask);
        await RunProducer(retry, "https://exhentai.org/g/12346/abcdef0123/", _ => Task.CompletedTask);
        CollectionAssert.AreEquivalent(paths, resumed.ToArray());
    }

    [TestMethod]
    public async Task LongImageFilenameUsesShortTemporaryNameAndKeepsRecordedOutputName()
    {
        var title = new string('a', 226) + ".jpg";
        var handler = new GalleryHandler {GalleryName = "Gallery", PageTitle = title};
        using var producer = await BuildProducer(handler, "{RawName}/{PageTitle}{Extension}");
        await RunProducer(producer, "https://exhentai.org/g/12345/abcdef0123/", _ => Task.CompletedTask,
            resultWorkflowId: null);

        var expected = Path.Combine(_root, "Gallery", title);
        CollectionAssert.AreEqual(handler.GetImageBytes(), await File.ReadAllBytesAsync(expected));
        var result = (await _results.GetByTaskAsync(10)).Single();
        Assert.IsTrue(result.FilesJson.Contains(title, StringComparison.Ordinal));
        AssertNoTemporaryFiles();
    }

    private void AssertNoTemporaryFiles()
    {
        Assert.AreEqual(0, Directory.GetFiles(_root, "*.tmp", SearchOption.AllDirectories).Length);
        Assert.AreEqual(0, Directory.GetFiles(_root, "*.download", SearchOption.AllDirectories).Length);
    }
}
