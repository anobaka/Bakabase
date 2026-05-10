using Bakabase.Abstractions.Models.Domain;
using Newtonsoft.Json;

namespace Bakabase.Modules.ThirdParty.Tests
{
    /// <summary>
    /// Fixture entry per source. Mirrors IAvDetail except URL-shaped fields are
    /// stored as bool ("non-empty?") so that fixtures don't go stale every time
    /// a CDN/path changes — we only assert the URL is or isn't present.
    /// </summary>
    public sealed class AvFixtureEntry
    {
        public string? Number { get; set; }
        public string? Title { get; set; }
        public string? OriginalTitle { get; set; }
        public string? Actor { get; set; }
        public string? Tag { get; set; }
        public string? Release { get; set; }
        public string? Year { get; set; }
        public string? Studio { get; set; }
        public string? Publisher { get; set; }
        public string? Series { get; set; }
        public string? Runtime { get; set; }
        public string? Director { get; set; }
        public string? Source { get; set; }
        public string? Mosaic { get; set; }

        public bool CoverUrlNonEmpty { get; set; }
        public bool PosterUrlNonEmpty { get; set; }
        public bool WebsiteNonEmpty { get; set; }
        public bool SearchUrlNonEmpty { get; set; }

        public static AvFixtureEntry FromDetail(IAvDetail d) => new()
        {
            Number = d.Number,
            Title = d.Title,
            OriginalTitle = d.OriginalTitle,
            Actor = d.Actor,
            Tag = d.Tag,
            Release = d.Release,
            Year = d.Year,
            Studio = d.Studio,
            Publisher = d.Publisher,
            Series = d.Series,
            Runtime = d.Runtime,
            Director = d.Director,
            Source = d.Source,
            Mosaic = d.Mosaic,
            CoverUrlNonEmpty = !string.IsNullOrEmpty(d.CoverUrl),
            PosterUrlNonEmpty = !string.IsNullOrEmpty(d.PosterUrl),
            WebsiteNonEmpty = !string.IsNullOrEmpty(d.Website),
            SearchUrlNonEmpty = !string.IsNullOrEmpty(d.SearchUrl),
        };

        public IEnumerable<string> CompareTo(AvFixtureEntry actual, string source)
        {
            var failures = new List<string>();
            CompareString(failures, source, nameof(Number), Number, actual.Number);
            CompareString(failures, source, nameof(Title), Title, actual.Title);
            CompareString(failures, source, nameof(OriginalTitle), OriginalTitle, actual.OriginalTitle);
            CompareString(failures, source, nameof(Actor), Actor, actual.Actor);
            CompareString(failures, source, nameof(Tag), Tag, actual.Tag);
            CompareString(failures, source, nameof(Release), Release, actual.Release);
            CompareString(failures, source, nameof(Year), Year, actual.Year);
            CompareString(failures, source, nameof(Studio), Studio, actual.Studio);
            CompareString(failures, source, nameof(Publisher), Publisher, actual.Publisher);
            CompareString(failures, source, nameof(Series), Series, actual.Series);
            CompareString(failures, source, nameof(Runtime), Runtime, actual.Runtime);
            CompareString(failures, source, nameof(Director), Director, actual.Director);
            CompareString(failures, source, nameof(Source), Source, actual.Source);
            CompareString(failures, source, nameof(Mosaic), Mosaic, actual.Mosaic);
            CompareBool(failures, source, nameof(CoverUrlNonEmpty), CoverUrlNonEmpty, actual.CoverUrlNonEmpty);
            CompareBool(failures, source, nameof(PosterUrlNonEmpty), PosterUrlNonEmpty, actual.PosterUrlNonEmpty);
            CompareBool(failures, source, nameof(WebsiteNonEmpty), WebsiteNonEmpty, actual.WebsiteNonEmpty);
            CompareBool(failures, source, nameof(SearchUrlNonEmpty), SearchUrlNonEmpty, actual.SearchUrlNonEmpty);
            return failures;
        }

        private static void CompareString(List<string> failures, string source, string field, string? expected, string? actual)
        {
            // Treat null / empty / whitespace-only as equivalent so tiny formatting
            // differences don't produce noise; the parser bug we care about produces
            // visibly different content, not whitespace.
            var ne = string.IsNullOrWhiteSpace(expected) ? string.Empty : expected.Trim();
            var na = string.IsNullOrWhiteSpace(actual) ? string.Empty : actual.Trim();
            if (!string.Equals(ne, na, StringComparison.Ordinal))
            {
                failures.Add($"[{source}] {field}: expected={Show(expected)} actual={Show(actual)}");
            }
        }

        private static void CompareBool(List<string> failures, string source, string field, bool expected, bool actual)
        {
            if (expected != actual)
            {
                failures.Add($"[{source}] {field}: expected={expected} actual={actual}");
            }
        }

        private static string Show(string? s) => s is null ? "<null>" : $"'{s}'";
    }

    internal static class AvFixtureIO
    {
        public static async Task WriteAsync(string path, Dictionary<string, AvFixtureEntry> fixture)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var json = JsonConvert.SerializeObject(fixture, Formatting.Indented);
            await File.WriteAllTextAsync(path, json);
        }

        public static async Task<Dictionary<string, AvFixtureEntry>> ReadAsync(string path)
        {
            var json = await File.ReadAllTextAsync(path);
            return JsonConvert.DeserializeObject<Dictionary<string, AvFixtureEntry>>(json) ?? new();
        }
    }
}
