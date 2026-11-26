using System.Text.Json.Nodes;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Downloader.Cloud;

namespace Bakabase.Tests.CloudDownloads;

public sealed partial class PikPakCliTests
{
    [DataTestMethod]
    [DataRow("verified")]
    [DataRow("wrong-fingerprint")]
    [DataRow("edited-part")]
    [DataRow("replaced-part")]
    [DataRow("replaced-output")]
    [DataRow("legacy")]
    public async Task OnlyACommittedMatchingHttpDownloadCanRetireItsOriginalCliPart(string scenario)
    {
        await Login();
        _native.FailPayload = true;
        _native.KeepFailedNativePartial = true;
        await Assert.ThrowsExceptionAsync<CloudProviderException>(() => _provider.DownloadAsync(Request, _ => Task.CompletedTask, default));
        var part = _native.Calls.Last(c => c[0] == "download")[3] + ".part";
        _restored.Fail = true;
        await Assert.ThrowsExceptionAsync<CloudProviderException>(() => _provider.RetryPreparedDownloadAsync(Request, _ => Task.CompletedTask, default));
        Assert.AreEqual("bad", await File.ReadAllTextAsync(part));
        if (scenario == "legacy")
        {
            var journal = Directory.EnumerateFiles(Path.Combine(_root, "appdata"), "download.json", SearchOption.AllDirectories).Single();
            var old = JsonNode.Parse(await File.ReadAllTextAsync(journal))!.AsObject();
            Assert.IsTrue(old.Remove("preservedNativePartVersions"));
            await File.WriteAllTextAsync(journal, old.ToJsonString());
        }
        _restored.Fail = false;
        var recovered = await _provider.RetryPreparedDownloadAsync(Request, _ => Task.CompletedTask, default);
        Assert.AreEqual("bad", await File.ReadAllTextAsync(part), "Provider completion precedes the native result commit.");
        var output = recovered.Files.Single();
        var fingerprint = (await ArtifactManifestFiles.CaptureAsync(Request.OutputDirectory, recovered.Files)).Fingerprint;
        var unrelated = Path.Combine(Path.GetDirectoryName(part)!, "foreign.payload.part");
        await File.WriteAllTextAsync(unrelated, "untouched");
        if (scenario == "wrong-fingerprint") fingerprint = new string('0', 64);
        if (scenario == "edited-part")
        {
            await File.WriteAllTextAsync(part, "new");
            File.SetLastWriteTimeUtc(part, DateTime.UtcNow.AddDays(-1));
        }
        if (scenario == "replaced-part")
        {
            File.Move(part, part + ".original");
            await File.WriteAllTextAsync(part, "new");
        }
        if (scenario == "replaced-output")
        {
            File.Move(output, output + ".original");
            await File.WriteAllTextAsync(output, "abc");
        }
        if (scenario == "legacy") Assert.IsNull(recovered.OnLocalFilesCommitted);
        else
        {
            Assert.IsNotNull(recovered.OnLocalFilesCommitted);
            recovered.OnLocalFilesCommitted(fingerprint);
            recovered.OnLocalFilesCommitted(fingerprint);
        }
        Assert.AreEqual(scenario != "verified", File.Exists(part));
        Assert.AreEqual("abc", await File.ReadAllTextAsync(output));
        Assert.AreEqual("untouched", await File.ReadAllTextAsync(unrelated));
    }
}
