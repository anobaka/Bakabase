using Bakabase.InsideWorld.Business.Components.Compression;

namespace Bakabase.Tests;

public sealed partial class ArchiveFormatProbeTests
{
    [DataTestMethod]
    [DataRow("ERROR: Cannot open the file as archive", "ArchiveInputRequired")]
    [DataRow("ERROR: selected.unknown : Cannot open the file as archive", "ArchiveInputRequired")]
    [DataRow("ERROR: /tmp/Wrong password.zip : Cannot open the file as archive", "ArchiveInputRequired")]
    [DataRow("ERROR: Wrong password.zip : Cannot open the file as archive", "ArchiveInputRequired")]
    [DataRow("ERROR: Wrong password : Cannot open the file as archive", "ArchiveInputRequired")]
    [DataRow("Cannot open the file as [7z] archive", "ArchiveInputRequired")]
    [DataRow("ERROR: Missing volume : pack.7z.003", "MissingVolumes")]
    [DataRow("ERROR: Unexpected end of archive", "MissingVolumes")]
    public async Task Explicit_structural_failures_stop_password_trials_without_claiming_a_wrong_password(string diagnostic, string code)
    {
        var service = await Stub($"printf 'call\\n' >> \"$0.calls\"\nprintf '%s\\n' '{diagnostic}' >&2\nexit 2");
        var result = await new ArchiveExtractionService(service).ProbePasswordAsync(_input, [null, "first", "second"], null, default);
        Assert.IsFalse(result.Succeeded);
        Assert.IsFalse(result.PasswordRejected);
        Assert.AreEqual(0, result.WrongPasswords.Count);
        Assert.AreEqual(code, result.FailureCode);
        Assert.AreEqual(1, File.ReadAllLines(Path.Combine(_directory, "probe-tool.calls")).Length);
    }

    [TestMethod]
    public async Task Wrong_password_diagnostic_keeps_candidate_retries_even_with_generic_cannot_open_line()
    {
        var service = await Stub("printf 'call\\n' >> \"$0.calls\"\nprintf 'Wrong password\\nCannot open the file as archive\\n' >&2\nexit 2");
        var result = await new ArchiveExtractionService(service).ProbePasswordAsync(_input, ["first", "second"], null, default);
        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.PasswordRejected);
        Assert.IsNull(result.FailureCode);
        Assert.AreEqual(2, File.ReadAllLines(Path.Combine(_directory, "probe-tool.calls")).Length);
    }

    [DataTestMethod]
    [DataRow("Path = Cannot open the file as archive")]
    [DataRow("Path = Missing volume : notes.txt")]
    [DataRow("ERROR: Data Error in encrypted file. Wrong password?")]
    [DataRow("ERROR: Cannot open encrypted archive. Wrong password?")]
    [DataRow("ERROR: Unsupported Method")]
    public void Generic_errors_and_member_names_are_not_claimed_as_missing_parts(string diagnostic) =>
        Assert.IsNull(ArchiveExtractionService.NonPasswordFailureCode(diagnostic));
}
