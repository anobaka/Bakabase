namespace Bakabase.Modules.ThirdParty.Tests
{
    // Detects the "AAA AAA" or "AAAAAA" patterns produced by CsQuery's .Text()
    // concatenating identical innerText across multiple matched DOM elements
    // (e.g. mobile + desktop versions rendered in the same page).
    //
    // Intentionally conservative: we only flag values of length >= 6 to avoid
    // tripping on legitimate short repetitive names like "ナナ" or "PAPA".
    internal static class ParsingDuplicationAssert
    {
        private const int MinDuplicationLength = 6;

        public static void NotDuplicated(string? value, string field, string number)
        {
            if (string.IsNullOrEmpty(value)) return;
            var trimmed = value.Trim();
            var len = trimmed.Length;
            if (len < MinDuplicationLength) return;

            if (len % 2 == 0)
            {
                var half = trimmed.Substring(0, len / 2);
                Assert.AreNotEqual(half, trimmed.Substring(len / 2),
                    $"{field} for {number} looks duplicated: '{value}'");
            }

            var spaceIdx = trimmed.IndexOf(' ', trimmed.Length / 3);
            while (spaceIdx > 0 && spaceIdx < trimmed.Length - 1)
            {
                var left = trimmed.Substring(0, spaceIdx);
                var right = trimmed.Substring(spaceIdx + 1);
                Assert.AreNotEqual(left, right,
                    $"{field} for {number} looks duplicated: '{value}'");
                spaceIdx = trimmed.IndexOf(' ', spaceIdx + 1);
            }
        }
    }
}
