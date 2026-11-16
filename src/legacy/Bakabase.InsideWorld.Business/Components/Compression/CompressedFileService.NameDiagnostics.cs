using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace Bakabase.InsideWorld.Business.Components.Compression;

public partial class CompressedFileService
{
    internal static bool HasZipNameDecodingFailure(TestResult result)
    {
        if (result.ExitCode == 0 || !result.StandardOutput.Split('\n')
                .Any(line => line.Trim().Equals("Type = zip", StringComparison.OrdinalIgnoreCase)))
            return false;

        // This is a tool diagnostic, not a guess based on an archive's language or suffix.
        // Unix 7-Zip can read legacy ZIP bytes but fail to create the decoded member path.
        // Keep space/permissions/missing-parent failures with readable names in their own
        // category, and never choose an encoding or retry extraction automatically here.
        return result.StandardError.Split('\n').Any(line =>
        {
            var match = Regex.Match(line.Trim(),
                @"^ERROR:\s*Cannot open output file\s*:\s*errno=2\s*:\s*No such file or directory\s*:\s*(?<path>.+)$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            return match.Success && match.Groups["path"].Value.Contains('\ufffd');
        });
    }
}
