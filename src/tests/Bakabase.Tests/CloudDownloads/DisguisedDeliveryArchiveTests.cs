using Bakabase.Service.Components.FileProcessing;

namespace Bakabase.Tests.CloudDownloads;

[TestClass]
public sealed class DisguisedDeliveryArchiveTests
{
    [DataTestMethod]
    [DataRow("app.apk", "377ABCAF271C0004", ".7z")]
    [DataRow("document.docx", "526172211A070100", ".rar")]
    [DataRow("comic.cbr", "377ABCAF271C0004", ".7z")]
    public async Task Explicit_selection_can_probe_a_contradictory_archive_header_but_discovery_preserves_delivery(
        string name, string header, string format)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "disguised-delivery-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var file = Path.Combine(root, name);
            await File.WriteAllBytesAsync(file, Convert.FromHexString(header));
            Assert.AreEqual(0, (await AuthorizedArchiveDetection.DetectAsync([file], false, default)).Count);
            Assert.AreEqual(0, (await AuthorizedArchiveDetection.DetectAsync([file], true, default,
                exactExtractionSelection: true)).Count);
            var candidate = (await AuthorizedArchiveDetection.DetectAsync([file], false, default,
                exactExtractionSelection: true)).Single();
            Assert.AreEqual(format, candidate.Extension);
            CollectionAssert.AreEqual(new[] {file}, candidate.Files);
            CollectionAssert.AreEqual(Convert.FromHexString(header), await File.ReadAllBytesAsync(file));
        }
        finally { Directory.Delete(root, true); }
    }

    [DataTestMethod]
    [DataRow("app.apk", "504B030400000000")]
    [DataRow("document.docx", "504B030400000000")]
    [DataRow("comic.cbr", "526172211A070100")]
    [DataRow("program.exe", "377ABCAF271C0004")]
    public async Task Ordinary_package_headers_and_executables_do_not_bypass_existing_extraction_guards(string name, string header)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "delivery-guard-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var file = Path.Combine(root, name);
            await File.WriteAllBytesAsync(file, Convert.FromHexString(header));
            Assert.AreEqual(0, (await AuthorizedArchiveDetection.DetectAsync([file], false, default,
                exactExtractionSelection: true)).Count);
        }
        finally { Directory.Delete(root, true); }
    }
}
