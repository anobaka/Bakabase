using System.Diagnostics;
using Bakabase.InsideWorld.Business.Components.Compression;

namespace Bakabase.Tests;

public sealed partial class ArchiveFormatProbeTests
{
    [DataTestMethod]
    [DataRow(40 * 1024 * 1024)]
    [DataRow(400 * 1024 * 1024)]
    public async Task StructuredZipHintUsesOneExplicitNonrecursiveTypeAndTheSameOpeningMode(int prefix)
    {
        await ZipStructureFixture.Write(_input, prefix, trailer: ZipStructureFixture.RarTail);
        var service = await StructuredStub($"printf '%s\\n' 'Type = zip' 'Offset = {prefix}' '----------' 'Type = Rar5'", prefix);
        var result = await service.ProbeAuthorizedArchiveFormatAsync(_input, default);
        Assert.AreEqual(".zip", result?.Extension);
        Assert.AreEqual($"-tzip:s{prefix + 1}b", result?.OpenOptions?.TypeArgument);
        Assert.AreEqual(1, File.ReadAllLines(Path.Combine(_directory, "probe-tool.calls")).Length);
        AssertToolStopped();
    }

    [TestMethod]
    public async Task OrdinaryDiscoveryDoesNotReadStructuralHintsOrExpandTheScan()
    {
        await ZipStructureFixture.Write(_input, 40 * 1024 * 1024);
        var service = await StructuredStub("exit 99");
        Assert.IsNull(await service.ProbeArchiveFormatAsync(_input, default));
        Assert.AreEqual(1, File.ReadAllLines(Path.Combine(_directory, "probe-tool.calls")).Length);
    }

    [DataTestMethod]
    [DataRow("377abcaf271c", "7z", ".7z")]
    [DataRow("526172211a070100", "Rar5", ".rar")]
    public async Task AnExistingOuterArchiveIsNotReplacedByItsEmbeddedZip(string signature, string type, string extension)
    {
        await ZipStructureFixture.Write(_input, 40 * 1024 * 1024);
        await using (var file = File.OpenWrite(_input)) await file.WriteAsync(Convert.FromHexString(signature));
        var service = await Stub($$"""
            [ "$#" = 8 ] && [ "$7" = -- ] || exit 91
            printf 'call\n' >> "$0.calls"
            printf '%s\n' 'Type = {{type}}' '----------'
            """);
        var result = await service.ProbeAuthorizedArchiveFormatAsync(_input, default);
        Assert.AreEqual(extension, result?.Extension);
        Assert.IsNull(result?.OpenOptions);
        Assert.AreEqual(1, File.ReadAllLines(Path.Combine(_directory, "probe-tool.calls")).Length);
    }

    [DataTestMethod]
    [DataRow("Type = Rar5", "Offset = 41943040")]
    [DataRow("Type = zip", "Offset = 17")]
    [DataRow("Type = zip", "")]
    public async Task GuidedProbeMustConfirmTheExpectedContainerAndOffset(string type, string offset)
    {
        await ZipStructureFixture.Write(_input, 40 * 1024 * 1024);
        var service = await StructuredStub($"printf '%s\\n' '{type}' '{offset}' '----------'");
        var error = await Assert.ThrowsExactlyAsync<ArchiveFormatProbeIncompleteException>(() =>
            service.ProbeAuthorizedArchiveFormatAsync(_input, default));
        StringAssert.Contains(error.Message, "expected ZIP structure");
        AssertToolStopped();
    }

    [TestMethod]
    public async Task StructuredConfirmationKeepsOutputAndCandidateTimeBudgets()
    {
        await ZipStructureFixture.Write(_input, 40 * 1024 * 1024);
        var service = await StructuredStub("printf '%070000d\\n' 0; printf '%s\\n' 'Type = zip' 'Offset = 41943040' '----------'");
        var error = await Assert.ThrowsExactlyAsync<ArchiveFormatProbeIncompleteException>(() =>
            service.ProbeAuthorizedArchiveFormatAsync(_input, default));
        StringAssert.Contains(error.Message, "output limit"); AssertToolStopped();
        service = await StructuredStub("exec sleep 60");
        var elapsed = Stopwatch.StartNew();
        error = await Assert.ThrowsExactlyAsync<ArchiveFormatProbeIncompleteException>(() =>
            service.ProbeAuthorizedArchiveFormatAsync(_input, default));
        StringAssert.Contains(error.Message, "time limit");
        Assert.IsTrue(elapsed.Elapsed < TimeSpan.FromSeconds(7)); AssertToolStopped();
    }

    private Task<CompressedFileService> StructuredStub(string success, int prefix = 40 * 1024 * 1024) => Stub($$"""
        printf 'call\n' >> "$0.calls"
        if [ "$#" = 8 ] || [ "$7" = '-t*:s32m:r' ]; then
          printf 'Cannot open the file as archive\n' >&2
          exit 2
        fi
        [ "$#" = 9 ] && [ "$7" = '-tzip:s{{prefix + 1}}b' ] && [ "$8" = -- ] && [ -f "$9" ] || exit 91
        read -r password || exit 92
        [ -z "$password" ] || exit 93
        if read -r extra; then exit 94; fi
        {{success}}
        """);
}
