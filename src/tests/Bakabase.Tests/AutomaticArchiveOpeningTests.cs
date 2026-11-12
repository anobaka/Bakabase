using System.Security.Cryptography;
using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;
using CliWrap;

namespace Bakabase.Tests;

[TestClass]
public sealed class AutomaticArchiveOpeningTests
{
    private string root = null!;
    [TestInitialize] public void Setup() => Directory.CreateDirectory(root = Path.Combine(Path.GetTempPath(), "automatic-archive-" + Guid.NewGuid().ToString("N")));
    [TestCleanup] public void Cleanup() => Directory.Delete(root, true);

    [DataTestMethod]
    [DataRow(".png", false)]
    [DataRow(".zip", false)]
    [DataRow(".png", true)]
    public async Task Selected_encrypted_prefixed_zip_uses_structure_for_listing_password_and_extraction(string suffix, bool workflow)
    {
        var executable = Executable();
        var payload = Path.Combine(root, "payload.txt"); await File.WriteAllTextAsync(payload, "synthetic archive payload");
        var ordinary = Path.Combine(root, "ordinary.zip");
        await Cli.Wrap(executable).WithArguments(["a", "-tzip", "-mem=ZipCrypto", "-pfixture-pass", ordinary, payload], true).ExecuteAsync();
        var zip = ZipStructureFixture.Zip64WithEmptyCentralExtra(await File.ReadAllBytesAsync(ordinary));
        var input = Path.Combine(root, "input"); Directory.CreateDirectory(input);
        var selected = await ZipStructureFixture.Write(Path.Combine(input, "selected" + suffix), 608331, zip);
        await using (var file = new FileStream(selected, FileMode.Open, FileAccess.Write))
            await file.WriteAsync(Convert.FromHexString("89504e470d0a1a0a"));
        var hash = SHA256.HashData(await File.ReadAllBytesAsync(selected));
        var service = new TestableCompressedFileService(executable); var extraction = new ArchiveExtractionService(service);
        var format = await extraction.ProbeAuthorizedFormatAsync(selected, default);
        Assert.AreEqual(".zip", format?.Extension); Assert.AreEqual("-tzip:s608332b", format!.OpenOptions!.TypeArgument);
        var entries = await service.GetCompressFileEntries(selected, default);
        Assert.AreEqual("payload.txt", entries.Data!.Single().Path);
        var wrong = await extraction.ProbePasswordAsync(selected, ["incorrect-fixture-pass"], null, default);
        Assert.IsFalse(wrong.Succeeded); Assert.IsTrue(wrong.PasswordRejected, wrong.Message);
        var correct = await extraction.ProbePasswordAsync(selected, ["fixture-pass"], null, default);
        Assert.IsTrue(correct.Succeeded, correct.Message);
        if (workflow)
        {
            var plan = new FileProcessingPlan {Requirement = "required", Steps = [new() {Id = "extract", Op = "extractArchive",
                Selector = Path.GetFileName(selected), Password = "fixture-pass", ExtractionLayout = FileExtractionLayouts.CurrentDirectory}]};
            var result = await new FileProcessingPlanExecutor(extraction).ExecuteAsync(plan, input, [selected], Path.Combine(root, "state"));
            Assert.IsTrue(result.Completed, result.Message);
            Assert.AreEqual("synthetic archive payload", await File.ReadAllTextAsync(result.Files.Single()));
        }
        else
        {
            var output = Path.Combine(root, "output"); Directory.CreateDirectory(output);
            var result = await extraction.ExtractAsync(new([selected], output, "fixture-pass", DecompressToNewFolder: false), null, default);
            Assert.IsTrue(result.Succeeded, result.Message);
            Assert.AreEqual("synthetic archive payload", await File.ReadAllTextAsync(Path.Combine(output, "payload.txt")));
        }
        CollectionAssert.AreEqual(hash, SHA256.HashData(await File.ReadAllBytesAsync(selected)));
    }

    [TestMethod]
    public async Task Zip_disguised_as_pdf_remains_a_zip_with_no_prefix_or_copy()
    {
        var path = Path.Combine(root, "subtitle.pdf"); await File.WriteAllBytesAsync(path, ZipStructureFixture.Zip());
        var service = new TestableCompressedFileService(Executable());
        Assert.AreEqual("payload.txt", (await service.GetCompressFileEntries(path, default)).Data!.Single().Path);
        var output = Path.Combine(root, "output"); Directory.CreateDirectory(output);
        var result = await new ArchiveExtractionService(service).ExtractAsync(new([path], output, DecompressToNewFolder: false), null, default);
        Assert.IsTrue(result.Succeeded, result.Message); Assert.IsTrue(File.Exists(path));
        Assert.AreEqual("inert payload", await File.ReadAllTextAsync(Path.Combine(output, "payload.txt")));
    }

    private static string Executable()
    {
        var path = Environment.GetEnvironmentVariable("BAKABASE_TEST_7Z_PATH");
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) Assert.Inconclusive("Set BAKABASE_TEST_7Z_PATH for the installed archive-tool fixture.");
        return path!;
    }
}
