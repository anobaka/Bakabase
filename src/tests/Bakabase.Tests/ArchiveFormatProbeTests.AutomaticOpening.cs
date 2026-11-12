using Bakabase.InsideWorld.Business.Components.Compression;

namespace Bakabase.Tests;

public sealed partial class ArchiveFormatProbeTests
{
    [DataTestMethod]
    [DataRow("t")]
    [DataRow("x")]
    [DataRow("l")]
    [DataRow("e")]
    public async Task Shared_automatic_operations_receive_only_a_verified_nonrecursive_zip_hint(string operation)
    {
        await ZipStructureFixture.Write(_input, 4096, ZipStructureFixture.Zip64WithEmptyCentralExtra(ZipStructureFixture.Zip()));
        var service = await Stub("printf '%s\\n' \"$@\" > \"$0.arguments\"\nprintf '%s\\n' '----------' 'Path = payload.txt' 'Size = 13' ''\n");
        await Operation();
        var args = File.ReadAllLines(Path.Combine(_directory, "probe-tool.arguments"));
        Assert.IsTrue(args.Contains(operation)); Assert.IsTrue(args.Contains("-tzip:s4097b"));
        Assert.IsFalse(args.Any(a => a.Contains(":r")));
        async Task Operation()
        {
            switch (operation)
            {
                case "l": await service.GetCompressFileEntries(_input, default); break;
                case "e": using (await service.ExtractOneEntry(_input, "payload.txt", default)) { } break;
                case "t": await service.TestCompressedFile(_input, "fixture", null, null, null, default); break;
                default: await service.ExtractWithProgress(_input, Path.Combine(_directory, "out"), "fixture", true,
                    CompressedFileService.OverwriteMode.None, CompressedFileService.ProgressOutputTarget.StandardOutput, null, null, null, default); break;
            }
        }
    }

    [TestMethod]
    public async Task No_structural_proof_never_infers_a_format_or_a_password_error()
    {
        var service = await Stub("printf '%s\\n' \"$@\" > \"$0.arguments\"\nprintf 'ERROR: Cannot open the file as archive\\n' >&2\nexit 2");
        var result = await new ArchiveExtractionService(service).ProbePasswordAsync(_input, ["fixture"], null, default);
        Assert.IsFalse(result.Succeeded); Assert.IsFalse(result.PasswordRejected);
        Assert.IsFalse(File.ReadAllLines(Path.Combine(_directory, "probe-tool.arguments")).Any(a => a.StartsWith("-t")));
    }

    [TestMethod]
    public async Task Explicit_bounded_format_is_not_silently_replaced_by_automatic_hint()
    {
        await ZipStructureFixture.Write(_input, 4096, ZipStructureFixture.Zip());
        var service = await Stub("printf '%s\\n' \"$@\" > \"$0.arguments\"");
        await service.TestCompressedFile(_input, null, null, null, null, default, ArchiveOpenOptions.BoundedPrefix);
        Assert.IsTrue(File.ReadAllLines(Path.Combine(_directory, "probe-tool.arguments")).Contains("-t*:s32m:r"));
    }
}
