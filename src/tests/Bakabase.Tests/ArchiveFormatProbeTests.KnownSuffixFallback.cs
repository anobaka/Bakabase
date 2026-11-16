using System.Diagnostics;
using Bakabase.InsideWorld.Business.Components.Compression;

namespace Bakabase.Tests;

public sealed partial class ArchiveFormatProbeTests
{
    [DataTestMethod]
    [DataRow(".zip", "zip", "7z", ".7z")]
    [DataRow(".ZIP", "zip", "7z", ".7z")]
    [DataRow(".7z", "7z", "zip", ".zip")]
    [DataRow(".rar", "rar", "zip", ".zip")]
    public async Task UnrecognizedKnownSuffixGetsOneWildcardRetryWithTheSameLiteralInput(
        string suffix, string typedFormat, string actualFormat, string extension)
    {
        PrepareKnownSuffixInput(suffix);
        var service = await KnownSuffixStub(typedFormat,
            "printf 'Cannot open the file as archive\\n' >&2; exit 2",
            $"printf '%s\\n' 'Type = {actualFormat}' '----------'");

        var result = await service.ProbeAuthorizedArchiveFormatAsync(_input, default);

        Assert.AreEqual(extension, result?.Extension);
        Assert.AreEqual(ArchiveOpenOptions.BoundedPrefix, result?.OpenOptions);
        AssertKnownSuffixCalls("default", $"-t{typedFormat}:s32m:r", "-t*:s32m:r");
        Assert.AreEqual(16L * 1024 * 1024, new FileInfo(_input).Length);
        AssertToolStopped();
    }

    [DataTestMethod]
    [DataRow(".zip", "zip", ".zip")]
    [DataRow(".7z", "7z", ".7z")]
    [DataRow(".rar", "rar", ".rar")]
    public async Task RecognizedKnownSuffixKeepsItsTypedOpeningWithoutWildcardRetry(
        string suffix, string typedFormat, string extension)
    {
        PrepareKnownSuffixInput(suffix);
        var service = await KnownSuffixStub(typedFormat,
            $"printf '%s\\n' 'Type = {typedFormat}' '----------'");

        var result = await service.ProbeAuthorizedArchiveFormatAsync(_input, default);

        Assert.AreEqual(extension, result?.Extension);
        Assert.AreEqual(ArchiveOpenOptions.BoundedPrefixFor(_input), result?.OpenOptions);
        AssertKnownSuffixCalls("default", $"-t{typedFormat}:s32m:r");
        AssertToolStopped();
    }

    [DataTestMethod]
    [DataRow("password")]
    [DataRow("unsupported")]
    [DataRow("headers-error")]
    [DataRow("data-error")]
    [DataRow("unrecognized-headers-error")]
    [DataRow("unrecognized-data-error")]
    [DataRow("timeout")]
    [DataRow("output-limit")]
    public async Task TypedFailuresAndUnsupportedHandlersNeverBroadenToWildcard(string kind)
    {
        PrepareKnownSuffixInput(".zip");
        var body = kind switch
        {
            "password" => "printf 'Cannot open encrypted archive. Wrong password?\\n' >&2; exit 2",
            "unsupported" => "printf '%s\\n' 'Type = PE' '----------'",
            "headers-error" => "printf 'Headers Error\\n' >&2; exit 2",
            "data-error" => "printf 'Data Error\\n' >&2; exit 2",
            "unrecognized-headers-error" => "printf '%s\\n' 'ERROR: Cannot open the file as [zip] archive' 'Headers Error' >&2; exit 2",
            "unrecognized-data-error" => "printf '%s\\n' 'ERROR: Cannot open the file as [zip] archive' 'ERROR: Data Error : payload.bin' >&2; exit 2",
            "timeout" => "exec sleep 60",
            _ => "while :; do printf '%02000d' 0 >&2; done"
        };
        // If a failure were misclassified as unrecognized, this success would expose it.
        var service = await KnownSuffixStub("zip", body, "printf '%s\\n' 'Type = 7z' '----------'");

        if (kind == "unsupported")
            Assert.IsNull(await service.ProbeAuthorizedArchiveFormatAsync(_input, default));
        else
        {
            var error = await Assert.ThrowsExactlyAsync<ArchiveFormatProbeIncompleteException>(() =>
                service.ProbeAuthorizedArchiveFormatAsync(_input, default));
            Assert.AreEqual(kind switch
            {
                "password" => ArchiveFormatProbeFailureCodes.PasswordRequired,
                "timeout" => ArchiveFormatProbeFailureCodes.TimedOut,
                "output-limit" => ArchiveFormatProbeFailureCodes.OutputLimit,
                _ => ArchiveFormatProbeFailureCodes.ToolFailed
            }, error.Code);
        }
        AssertKnownSuffixCalls("default", "-tzip:s32m:r");
        AssertToolStopped();
    }

    [TestMethod]
    public async Task ThirdWildcardProbeSharesTheOriginalCandidateOutputBudget()
    {
        PrepareKnownSuffixInput(".zip");
        const string unrecognized = "printf '%024000d\\n' 0; printf 'Cannot open the file as archive\\n' >&2; exit 2";
        var service = await KnownSuffixStub("zip", unrecognized,
            "printf '%024000d\\n' 0; printf '%s\\n' 'Type = 7z' '----------'", unrecognized);

        var error = await Assert.ThrowsExactlyAsync<ArchiveFormatProbeIncompleteException>(() =>
            service.ProbeAuthorizedArchiveFormatAsync(_input, default));

        Assert.AreEqual(ArchiveFormatProbeFailureCodes.OutputLimit, error.Code);
        AssertKnownSuffixCalls("default", "-tzip:s32m:r", "-t*:s32m:r");
        AssertToolStopped();
    }

    [TestMethod]
    public async Task ThirdWildcardProbeSharesTheOriginalCandidateTimeBudget()
    {
        PrepareKnownSuffixInput(".zip");
        const string unrecognized = "sleep 1.5; printf 'Cannot open the file as archive\\n' >&2; exit 2";
        var service = await KnownSuffixStub("zip", unrecognized, "exec sleep 60", unrecognized);
        var elapsed = Stopwatch.StartNew();

        var error = await Assert.ThrowsExactlyAsync<ArchiveFormatProbeIncompleteException>(() =>
            service.ProbeAuthorizedArchiveFormatAsync(_input, default));

        Assert.AreEqual(ArchiveFormatProbeFailureCodes.TimedOut, error.Code);
        Assert.IsTrue(elapsed.Elapsed < ArchiveFormatProbeLimits.PerCandidate + TimeSpan.FromSeconds(2));
        AssertKnownSuffixCalls("default", "-tzip:s32m:r", "-t*:s32m:r");
        AssertToolStopped();
    }

    private void PrepareKnownSuffixInput(string suffix)
    {
        var renamed = Path.ChangeExtension(_input, suffix);
        File.Move(_input, renamed);
        _input = renamed;
        using var file = File.OpenWrite(_input);
        file.SetLength(16 * 1024 * 1024);
    }

    private Task<CompressedFileService> KnownSuffixStub(string typedFormat, string typedBody,
        string wildcardBody = "exit 95",
        string defaultBody = "printf 'Cannot open the file as archive\\n' >&2; exit 2") => Stub($$"""
        [ "$1" = l ] && [ "$2" = -slt ] && [ "$3" = -sccUTF-8 ] || exit 91
        [ "$4" = -bsp0 ] && [ "$5" = -bd ] && [ "$6" = -spd ] || exit 92
        case "$#" in
          8)
            [ "$7" = -- ] && [ -f "$8" ] || exit 93
            printf '%s\n' default >> "$0.calls"
            printf '%s\n' "$8" >> "$0.inputs"
            {{defaultBody}}
            ;;
          9)
            [ "$8" = -- ] && [ -f "$9" ] || exit 94
            printf '%s\n' "$7" >> "$0.calls"
            printf '%s\n' "$9" >> "$0.inputs"
            case "$7" in
              '-t{{typedFormat}}:s32m:r') {{typedBody}} ;;
              '-t*:s32m:r') {{wildcardBody}} ;;
              *) exit 96 ;;
            esac
            ;;
          *) exit 97 ;;
        esac
        """);

    private void AssertKnownSuffixCalls(params string[] expected)
    {
        CollectionAssert.AreEqual(expected, File.ReadAllLines(Path.Combine(_directory, "probe-tool.calls")));
        CollectionAssert.AreEqual(Enumerable.Repeat(_input, expected.Length).ToArray(),
            File.ReadAllLines(Path.Combine(_directory, "probe-tool.inputs")));
    }
}
