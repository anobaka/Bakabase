using System.Diagnostics;
using Bakabase.InsideWorld.Business.Components.Compression;

namespace Bakabase.Tests;

[TestClass]
public sealed partial class ArchiveFormatProbeTests
{
    private string _directory = null!;
    private string _input = null!;
    [TestInitialize]
    public void Initialize()
    {
        _directory = Path.Combine(Path.GetTempPath(), "archive-probe-process-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _input = Path.Combine(_directory, "@literal?*.bin");
        if (OperatingSystem.IsWindows()) _input = Path.Combine(_directory, "literal.bin");
        File.WriteAllText(_input, "synthetic input");
    }
    [TestCleanup] public void Cleanup() => Directory.Delete(_directory, true);

    [TestMethod]
    public async Task ToolReceivesOnlyLiteralReadOnlyListingAndClosedStdin()
    {
        var service = await Stub("""
            [ "$#" = 8 ] || exit 91
            [ "$1" = l ] && [ "$2" = -slt ] && [ "$3" = -sccUTF-8 ] || exit 92
            [ "$4" = -bsp0 ] && [ "$5" = -bd ] && [ "$6" = -spd ] && [ "$7" = -- ] || exit 93
            [ -f "$8" ] || exit 94
            if read -r anything; then exit 95; fi
            printf '%s\n' '--' 'Path = fixture----------name' 'Type = zip' 'Physical Size = 12' '----------' 'Type = rar'
            """);
        Assert.AreEqual(".zip", await service.ProbeArchiveFormatAsync(_input, default));
        Assert.AreEqual("synthetic input", File.ReadAllText(_input));
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CombinedOutputLimitStopsTheToolWithoutTreatingItAsNoArchive(bool standardError)
    {
        var service = await Stub(standardError
            ? "while :; do printf '%02000d' 0 >&2; done"
            : "while :; do printf '%02000d' 0; done");
        var error = await Assert.ThrowsExactlyAsync<ArchiveFormatProbeIncompleteException>(() =>
            service.ProbeArchiveFormatAsync(_input, default));
        StringAssert.Contains(error.Message, "output limit");
        AssertToolStopped();
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task TimeLimitStopsAStalledTool(bool afterHeader)
    {
        var service = await Stub((afterHeader ? "printf '%s\\n' 'Type = zip' '----------'\n" : "") + "exec sleep 60");
        var elapsed = Stopwatch.StartNew();
        var error = await Assert.ThrowsExactlyAsync<ArchiveFormatProbeIncompleteException>(() =>
            service.ProbeArchiveFormatAsync(_input, default));
        StringAssert.Contains(error.Message, "time limit");
        Assert.IsTrue(elapsed.Elapsed < ArchiveFormatProbeLimits.PerCandidate + TimeSpan.FromSeconds(5));
        AssertToolStopped();
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CallerCancellationIsNotConvertedToAnUnrecognizedFormat(bool afterHeader)
    {
        var service = await Stub((afterHeader ? "printf '%s\\n' 'Type = zip' '----------'\n" : "") + "exec sleep 60");
        using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        await Assert.ThrowsAsync<OperationCanceledException>(() => service.ProbeArchiveFormatAsync(_input, stop.Token));
        AssertToolStopped();
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ManyMembersAreDrainedWithoutTrustingMemberTypesOrExhaustingHeaderBudget(bool crlf)
    {
        // Position the separator across a 4096-byte boundary for both newline styles.
        // An embedded run of dashes in the path is not the separator.
        var newLine = crlf ? "\\r\\n" : "\\n";
        var padding = crlf ? 4044 : 4048;
        var service = await Stub($$"""
            printf '%s{{newLine}}' '--' 'Path = fixture----------name' 'Type = zip'
            printf '%0{{padding}}d{{newLine}}' 0
            printf '%s{{newLine}}' '----------'
            i=0
            while [ "$i" -lt 1024 ]; do
              printf '%s{{newLine}}' 'Path = member' 'Type = rar'
              printf '%02000d{{newLine}}' 0
              i=$((i + 1))
            done
            """);
        Assert.AreEqual(".zip", await service.ProbeArchiveFormatAsync(_input, default));
        Assert.AreEqual("synthetic input", File.ReadAllText(_input));
        AssertToolStopped();
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RecognizedHeaderDoesNotHideLateFailureOrStandardErrorOverflow(bool standardError)
    {
        var service = await Stub("printf '%s\\n' 'Type = zip' '----------'\n" + (standardError
            ? "while :; do printf '%02000d' 0 >&2; done"
            : "printf '%080000d\\n' 0; exit 8"));
        var error = await Assert.ThrowsExactlyAsync<ArchiveFormatProbeIncompleteException>(() =>
            service.ProbeArchiveFormatAsync(_input, default));
        StringAssert.Contains(error.Message, standardError ? "output limit" : "could not complete");
        AssertToolStopped();
    }

    [TestMethod]
    public async Task RecognizedTypeDoesNotBypassAnOverlongUnterminatedHeader()
    {
        var service = await Stub("printf '%s\\n' 'Type = zip'; while :; do printf '%02000d' 0; done");
        var error = await Assert.ThrowsExactlyAsync<ArchiveFormatProbeIncompleteException>(() =>
            service.ProbeArchiveFormatAsync(_input, default));
        StringAssert.Contains(error.Message, "output limit");
        AssertToolStopped();
    }

    [TestMethod]
    public async Task StandardErrorAndHeaderStillShareOneOutputBudget()
    {
        var service = await Stub("printf '%040000d\\n' 0; printf '%s\\n' 'Type = zip' '----------'; printf '%030000d' 0 >&2");
        var error = await Assert.ThrowsExactlyAsync<ArchiveFormatProbeIncompleteException>(() =>
            service.ProbeArchiveFormatAsync(_input, default));
        StringAssert.Contains(error.Message, "output limit");
        AssertToolStopped();
    }

    [TestMethod]
    public async Task InputMustRemainUnchangedWhileMemberOutputIsDrained()
    {
        var service = await Stub("printf '%s\\n' 'Type = zip' '----------'; printf ' changed' >> \"$8\"; printf '%080000d\\n' 0");
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => service.ProbeArchiveFormatAsync(_input, default));
        AssertToolStopped();
    }

    [TestMethod]
    public async Task NonContainerToolHandlersAreNotAuthorizedForExtraction()
    {
        var service = await Stub("printf '%s\\n' '--' 'Type = PE' '----------'");
        Assert.IsNull(await service.ProbeArchiveFormatAsync(_input, default));
    }

    [TestMethod]
    public async Task ToolFailureDoesNotMasqueradeAsAnUnrecognizedFile()
    {
        var service = await Stub("printf '%s\\n' 'Type = zip'; exit 8");
        await Assert.ThrowsExactlyAsync<ArchiveFormatProbeIncompleteException>(() =>
            service.ProbeArchiveFormatAsync(_input, default));
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AuthorizedUnrecognizedLargeInputGetsExactlyOneBoundedPrefixRetry(bool typed)
    {
        using (var file = File.OpenWrite(_input)) file.SetLength(16 * 1024 * 1024);
        var error = typed ? "Cannot open the file as [zip] archive" : "Cannot open the file as archive";
        var service = await Stub($$"""
            printf 'call\n' >> "$0.calls"
            if [ "$#" = 8 ]; then
              printf '%s\n' '{{error}}' >&2
              exit 2
            fi
            [ "$#" = 9 ] && [ "$7" = '-t*:s32m:r' ] && [ "$8" = -- ] && [ -f "$9" ] || exit 91
            printf '%s\n' 'Type = zip' '----------'
            """);
        var result = await service.ProbeAuthorizedArchiveFormatAsync(_input, default);
        Assert.AreEqual(".zip", result?.Extension);
        Assert.AreEqual(ArchiveOpenOptions.BoundedPrefix, result?.OpenOptions);
        Assert.AreEqual(2, File.ReadAllLines(Path.Combine(_directory, "probe-tool.calls")).Length);
        AssertToolStopped();
    }

    [DataTestMethod]
    [DataRow("recognized")]
    [DataRow("unsupported")]
    [DataRow("password")]
    [DataRow("failure")]
    [DataRow("small")]
    public async Task PrefixRetryDoesNotBroadenRecognizedSmallOrFailedInputs(string kind)
    {
        if (kind != "small") using (var file = File.OpenWrite(_input)) file.SetLength(16 * 1024 * 1024);
        var body = kind switch
        {
            "recognized" => "printf '%s\\n' 'Type = zip' '----------'",
            "unsupported" => "printf '%s\\n' 'Type = PE' '----------'",
            "password" => "printf 'Wrong password\\nCannot open the file as archive\\n' >&2; exit 2",
            "small" => "printf 'Cannot open the file as archive\\n' >&2; exit 2",
            _ => "exit 8"
        };
        var service = await Stub("printf 'call\\n' >> \"$0.calls\"\n" + body);
        if (kind is "password" or "failure")
            await Assert.ThrowsExactlyAsync<ArchiveFormatProbeIncompleteException>(() =>
                service.ProbeAuthorizedArchiveFormatAsync(_input, default));
        else
        {
            var result = await service.ProbeAuthorizedArchiveFormatAsync(_input, default);
            if (kind == "recognized") { Assert.AreEqual(".zip", result?.Extension); Assert.IsNull(result?.OpenOptions); }
            else Assert.IsNull(result);
        }
        Assert.AreEqual(1, File.ReadAllLines(Path.Combine(_directory, "probe-tool.calls")).Length);
    }

    [TestMethod]
    public async Task PrefixRetryCannotResetTheCandidateTimeBudget()
    {
        using (var file = File.OpenWrite(_input)) file.SetLength(16 * 1024 * 1024);
        var service = await Stub("""
            if [ "$#" = 8 ]; then
              sleep 3
              printf 'Cannot open the file as archive\n' >&2
              exit 2
            fi
            exec sleep 60
            """);
        var elapsed = Stopwatch.StartNew();
        var error = await Assert.ThrowsExactlyAsync<ArchiveFormatProbeIncompleteException>(() =>
            service.ProbeAuthorizedArchiveFormatAsync(_input, default));
        StringAssert.Contains(error.Message, "time limit");
        Assert.IsTrue(elapsed.Elapsed < TimeSpan.FromSeconds(7));
        AssertToolStopped();
    }

    [TestMethod]
    public async Task AuthorizedHeaderPasswordUsesOnlyStdinAndOnlyAfterEncryptionEvidence()
    {
        var service = await Stub("""
            [ "$#" = 8 ] && [ "$7" = -- ] || exit 91
            read -r password || exit 92
            if read -r extra; then exit 93; fi
            printf 'call\n' >> "$0.calls"
            if [ "$password" != 'approved-pass' ]; then
              printf 'Cannot open encrypted archive. Wrong password?\n' >&2
              exit 2
            fi
            printf '%s\n' 'Type = Rar5' '----------'
            """);
        var result = await service.ProbeAuthorizedArchiveFormatAsync(_input, ["approved-pass"], default);
        Assert.AreEqual(".rar", result?.Extension);
        Assert.AreEqual(2, File.ReadAllLines(Path.Combine(_directory, "probe-tool.calls")).Length);
        AssertToolStopped();
    }

    [DataTestMethod]
    [DataRow("Type = PE", 0)]
    [DataRow("Cannot open the file as archive", 2)]
    public async Task AnAuthorizedPasswordDoesNotTurnUnrelatedInputIntoAnArchive(string output, int exitCode)
    {
        var service = await Stub($$"""
            read -r password || exit 92
            [ -z "$password" ] || exit 93
            printf 'call\n' >> "$0.calls"
            printf '%s\n' '{{output}}' {{(exitCode == 0 ? "" : ">&2")}}
            exit {{exitCode}}
            """);
        Assert.IsNull(await service.ProbeAuthorizedArchiveFormatAsync(_input, ["approved-pass"], default));
        Assert.AreEqual(1, File.ReadAllLines(Path.Combine(_directory, "probe-tool.calls")).Length);
    }

    [TestMethod]
    public async Task PasswordRetriesRemainBoundedAndNeverExposeCandidateValues()
    {
        var service = await Stub("printf 'call\\n' >> \"$0.calls\"; printf 'Cannot open encrypted archive. Wrong password?\\n' >&2; exit 2");
        var error = await Assert.ThrowsExactlyAsync<ArchiveFormatProbeIncompleteException>(() =>
            service.ProbeAuthorizedArchiveFormatAsync(_input, ["secret-one", "secret-one", "secret-two", "secret-three"], default));
        Assert.IsFalse(error.Message.Contains("secret-", StringComparison.Ordinal));
        Assert.AreEqual(1 + ArchiveFormatProbeLimits.MaximumAuthorizedPasswords,
            File.ReadAllLines(Path.Combine(_directory, "probe-tool.calls")).Length);
    }

    [TestMethod]
    public async Task PasswordRetrySharesTheCandidateTimeAndOutputLimits()
    {
        var service = await Stub("""
            read -r password || exit 92
            if [ -z "$password" ]; then
              sleep 3
              printf 'Cannot open encrypted archive. Wrong password?\n' >&2
              exit 2
            fi
            exec sleep 60
            """);
        var elapsed = Stopwatch.StartNew();
        var error = await Assert.ThrowsExactlyAsync<ArchiveFormatProbeIncompleteException>(() =>
            service.ProbeAuthorizedArchiveFormatAsync(_input, ["approved-pass"], default));
        StringAssert.Contains(error.Message, "time limit");
        Assert.IsTrue(elapsed.Elapsed < TimeSpan.FromSeconds(7));
        AssertToolStopped();
        service = await Stub("""
            read -r password || exit 92
            printf '%040000d\n' 0
            if [ -z "$password" ]; then
              printf 'Cannot open encrypted archive. Wrong password?\n' >&2
              exit 2
            fi
            printf '%s\n' 'Type = Rar5' '----------'
            """);
        error = await Assert.ThrowsExactlyAsync<ArchiveFormatProbeIncompleteException>(() =>
            service.ProbeAuthorizedArchiveFormatAsync(_input, ["approved-pass"], default));
        StringAssert.Contains(error.Message, "output limit");
        AssertToolStopped();
    }

    private async Task<CompressedFileService> Stub(string body)
    {
        if (OperatingSystem.IsWindows()) Assert.Inconclusive("The process lifecycle fixture uses a Unix shell.");
        var path = Path.Combine(_directory, "probe-tool");
        await File.WriteAllTextAsync(path, "#!/bin/sh\nprintf '%s' \"$$\" > \"$0.pid\"\n" + body + "\n");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return new TestableCompressedFileService(path);
    }
    private void AssertToolStopped()
    {
        var pid = int.Parse(File.ReadAllText(Path.Combine(_directory, "probe-tool.pid")));
        try { using var process = Process.GetProcessById(pid); Assert.IsTrue(process.HasExited); }
        catch (ArgumentException) { /* Reaped by the command runner. */ }
    }
}
