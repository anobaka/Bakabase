using System;
using System.Text.RegularExpressions;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Av
{
    /// <summary>
    /// A parsed AV product code: a studio label plus a serial, e.g. <c>XDVD-101</c>.
    /// </summary>
    /// <param name="Label">Studio label, upper-cased.</param>
    /// <param name="Serial">Serial digits, kept verbatim so leading zeros survive.</param>
    public record AvProductCode(string Label, string Serial)
    {
        /// <summary>
        /// Canonical <c>LABEL-SERIAL</c> form, used as a grouping key and folder name.
        /// </summary>
        /// <remarks>
        /// Leading zeros are significant — <c>XDVD-001</c> and <c>XDVD-1</c> are different works —
        /// so the serial is never re-numbered.
        /// </remarks>
        public string Normalized => $"{Label}-{Serial}";

        public override string ToString() => Normalized;
    }

    /// <summary>
    /// Extracts an AV product code from a filename.
    /// </summary>
    /// <remarks>
    /// Handles the three things real filenames do that naive patterns do not:
    /// mixed case (<c>xdvd-101</c>), an optional separator (<c>XDVD101</c>, <c>XDVD_101</c>,
    /// <c>XDVD 104</c>), and junk around the code (<c>sssssssXDVD-101pl</c>,
    /// <c>[hd]XDVD-102 [1080p]</c>).
    ///
    /// A label is required to be a *whole* token of 2-10 letters rather than any letter run the
    /// scanner happens to land on. That is what stops junk being absorbed into the label: an
    /// unanchored greedy match turns <c>SSSSSSSXDVD-101pl</c> into <c>SSXDVD-101</c>, which is worse
    /// than no match because it silently creates a wrong group. Where the junk is separated by a
    /// case change — by far the common form — the boundary is recovered before matching. Where it
    /// is not (<c>SSSSSSSXDVD</c>, one indivisible 11-letter run), no code is reported and the file
    /// is simply left ungrouped, which is honest: without a table of real studio labels there is
    /// nothing in the string that says where the junk ends.
    /// </remarks>
    public static class AvProductCodeParser
    {
        /// <summary>
        /// Label, optional separator, serial. The label may not be preceded or followed by another
        /// letter, so it has to be a complete token.
        /// </summary>
        private static readonly Regex CodeRegex = new(
            @"(?<![A-Za-z])(?<label>[A-Za-z]{2,10})[-_. ]?(?<serial>\d{2,8})(?!\d)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));

        /// <summary>Bracketed segments: quality tags, release groups, site suffixes.</summary>
        private static readonly Regex BracketedRegex = new(
            @"[\[\(【][^\]\)】]*[\]\)】]",
            RegexOptions.Compiled | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));

        /// <summary>A lower-to-upper transition, which in practice separates junk from the label.</summary>
        private static readonly Regex CaseBoundaryRegex = new(
            @"(?<=[a-z])(?=[A-Z])",
            RegexOptions.Compiled | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));

        /// <summary>
        /// Returns the product code in <paramref name="name"/>, or null when there is none.
        /// </summary>
        public static AvProductCode? Parse(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            try
            {
                // Bracketed segments routinely hold digits (resolutions, years) that would
                // otherwise read as a serial, so drop them before matching.
                var cleaned = BracketedRegex.Replace(name, " ");
                cleaned = CaseBoundaryRegex.Replace(cleaned, " ");

                var m = CodeRegex.Match(cleaned);

                if (!m.Success)
                {
                    return null;
                }

                return new AvProductCode(
                    m.Groups["label"].Value.ToUpperInvariant(),
                    m.Groups["serial"].Value);
            }
            catch (RegexMatchTimeoutException)
            {
                return null;
            }
        }

        /// <summary>Convenience wrapper returning the canonical string, or null.</summary>
        public static string? ParseNormalized(string? name) => Parse(name)?.Normalized;
    }
}
