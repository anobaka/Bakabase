using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Modules.ThirdParty.ThirdParties.Airav;
using Bakabase.Modules.ThirdParty.ThirdParties.Av;
using Bakabase.Modules.ThirdParty.ThirdParties.Avsex;
using Bakabase.Modules.ThirdParty.ThirdParties.Avsox;
using Bakabase.Modules.ThirdParty.ThirdParties.CNMDB;
using Bakabase.Modules.ThirdParty.ThirdParties.Dahlia;
using Bakabase.Modules.ThirdParty.ThirdParties.FC2;
using Bakabase.Modules.ThirdParty.ThirdParties.Faleno;
using Bakabase.Modules.ThirdParty.ThirdParties.Fantastica;
using Bakabase.Modules.ThirdParty.ThirdParties.Fc2hub;
using Bakabase.Modules.ThirdParty.ThirdParties.Freejavbt;
using Bakabase.Modules.ThirdParty.ThirdParties.GetchuDl;
using Bakabase.Modules.ThirdParty.ThirdParties.Iqqtv;
using Bakabase.Modules.ThirdParty.ThirdParties.Jav321;
using Bakabase.Modules.ThirdParty.ThirdParties.Javbus;
using Bakabase.Modules.ThirdParty.ThirdParties.Javday;
using Bakabase.Modules.ThirdParty.ThirdParties.Javdb;
using Bakabase.Modules.ThirdParty.ThirdParties.Javlibrary;
using Bakabase.Modules.ThirdParty.ThirdParties.Lulubar;
using Bakabase.Modules.ThirdParty.ThirdParties.Mmtv;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Bakabase.Modules.ThirdParty.ThirdParties.DMM;

namespace Bakabase.Modules.ThirdParty.Tests
{
    /// <summary>
    /// Single integration test that fans out to every AV-source client in parallel
    /// for one number, prints a per-source summary, and fails if any returned detail
    /// shows the "AAA AAA"/"AAAAAA" duplication pattern. Replaces having to remove
    /// [Ignore] on each per-source test individually when the user just wants to
    /// confirm "no duplication anywhere".
    /// </summary>
    [TestClass]
    public class AllAvClientsTests
    {
        // Replace with a number you want to verify; a code that several sources index
        // produces the most useful coverage. The test is [Ignore]'d by default so CI
        // does not hit live sites.
        private const string DefaultNumber = "RBK-130";

        // The 20 AV-source clients dispatched by AvEnhancer / AvController.
        // Kept beside BuildDispatchers so the two are obviously paired.
        private static readonly Type[] AvClientTypes =
        {
            typeof(AiravClient), typeof(AvsexClient), typeof(AvsoxClient), typeof(CNMDBClient),
            typeof(DmmClient), typeof(DahliaClient), typeof(FC2Client), typeof(FalenoClient),
            typeof(FantasticaClient), typeof(Fc2hubClient), typeof(FreejavbtClient),
            typeof(GetchuDlClient), typeof(IqqtvClient), typeof(Jav321Client), typeof(JavbusClient),
            typeof(JavdayClient), typeof(JavdbClient), typeof(JavlibraryClient), typeof(LulubarClient),
            typeof(MmtvClient),
        };

        /// <summary>
        /// Regression guard for the dispatcher table: the keys registered with the
        /// dispatcher MUST exactly cover the canonical id list. Drift here is what
        /// allows "preferred source for target X" to silently drop everything.
        /// </summary>
        [TestMethod]
        public void Dispatcher_KeysExactlyMatchAvSourceIdsAll()
        {
            var dispatchers = BuildDispatchers(BuildServiceProvider());
            CollectionAssert.AreEquivalent(
                AvSourceIds.All.ToList(),
                dispatchers.Keys.ToList(),
                $"Dispatcher keys must match AvSourceIds.All exactly. " +
                $"Missing: [{string.Join(", ", AvSourceIds.All.Except(dispatchers.Keys))}]. " +
                $"Extra: [{string.Join(", ", dispatchers.Keys.Except(AvSourceIds.All))}].");
        }

        /// <summary>
        /// Regression guard for the original bug (issue #1161): clients used to
        /// assign IAvDetail.Source via a string literal that drifted from the
        /// dispatcher key (e.g. MmtvClient set "7mmtv" while the dispatcher key
        /// was "mmtv"), causing AvEnhancer.OrderDetailsForTarget to silently
        /// drop the parsed detail when the user picked that source as preferred.
        /// Enforcing the AvSourceIds.* constant collapses the two strings into
        /// one source of truth.
        /// </summary>
        [TestMethod]
        public void AvClient_SourceFieldUsesAvSourceIdsConstant()
        {
            var thirdPartiesDir = LocateThirdPartiesDir();
            var failures = new List<string>();
            foreach (var type in AvClientTypes)
            {
                var matches = Directory.EnumerateFiles(thirdPartiesDir, $"{type.Name}.cs", SearchOption.AllDirectories).ToList();
                Assert.AreEqual(1, matches.Count, $"Expected exactly one file named {type.Name}.cs under {thirdPartiesDir}");
                var content = File.ReadAllText(matches[0]);
                var literal = Regex.Match(content, @"\bSource\s*=\s*""([^""]*)""");
                if (literal.Success)
                {
                    failures.Add($"{type.Name}: Source = \"{literal.Groups[1].Value}\" — use AvSourceIds.X instead");
                }
            }

            if (failures.Any())
            {
                Assert.Fail("AV client(s) assign IAvDetail.Source to a string literal:\n  " + string.Join("\n  ", failures));
            }
        }

        // Resolves the runtime path of the ThirdParties folder so the regression
        // test above can grep client source files. Uses [CallerFilePath] so the
        // location tracks the source tree, not the test bin directory.
        private static string LocateThirdPartiesDir([CallerFilePath] string? callerFile = null)
        {
            var testDir = Path.GetDirectoryName(callerFile)!;
            return Path.GetFullPath(Path.Combine(
                testDir, "..", "..", "modules", "Bakabase.Modules.ThirdParty", "ThirdParties"));
        }

        [TestMethod]
        [Ignore("Manual integration test — hits every live AV source in parallel. Remove [Ignore] to run.")]
        public Task SearchAndParseVideo_AllSources_NoFieldDuplication()
            => RunAllSources(DefaultNumber);

        // Snapshot-style fixture tests. Workflow:
        //   1) Unignore GenerateFixture, run once for each number you want to track.
        //      It hits every live source, builds an expected dict per source, and
        //      writes Fixtures/Av/<number>.json next to this test file.
        //   2) Inspect the generated JSON, prune entries you don't trust (or for
        //      sources that returned junk that day) — anything left in the file
        //      becomes the expected baseline.
        //   3) Unignore VerifyAgainstFixture and add a [DataRow] for the number.
        //      Each run dispatches all clients in parallel and compares parsed
        //      output to the fixture, aggregating per-source/per-field mismatches
        //      into a single Assert.Fail. URL fields are checked as non-empty
        //      booleans only so CDN/path drift doesn't constantly invalidate the
        //      fixture.
        [DataTestMethod]
        [DataRow(DefaultNumber)]
        [Ignore("Manual integration test — generates Fixtures/Av/<number>.json from live sources. Remove [Ignore] to refresh.")]
        public async Task GenerateFixture(string number)
        {
            var results = await DispatchAll(number);
            var fixture = results
                .Where(r => r.Detail != null)
                .ToDictionary(r => r.Source, r => AvFixtureEntry.FromDetail(r.Detail!));

            Console.WriteLine($"=== {number}: capturing {fixture.Count}/{results.Length} sources ===");
            foreach (var r in results.OrderBy(r => r.Source))
            {
                Console.WriteLine(r.Detail != null
                    ? $"  [OK]   {r.Source}"
                    : $"  [MISS] {r.Source} {(r.Error ?? "no result")}");
            }

            var path = ResolveFixturePath(number);
            await AvFixtureIO.WriteAsync(path, fixture);
            Console.WriteLine($"Fixture written: {path}");
        }

        [DataTestMethod]
        [DataRow(DefaultNumber)]
        [Ignore("Manual integration test — verifies live AV sources match Fixtures/Av/<number>.json. Remove [Ignore] to run.")]
        public async Task VerifyAgainstFixture(string number)
        {
            var path = ResolveFixturePath(number);
            Assert.IsTrue(File.Exists(path),
                $"Fixture missing: {path}. Run GenerateFixture for '{number}' first.");

            var expected = await AvFixtureIO.ReadAsync(path);
            var results = await DispatchAll(number);
            var actualBySource = results
                .Where(r => r.Detail != null)
                .ToDictionary(r => r.Source, r => AvFixtureEntry.FromDetail(r.Detail!));

            var failures = new List<string>();
            foreach (var (source, expectedEntry) in expected)
            {
                if (!actualBySource.TryGetValue(source, out var actualEntry))
                {
                    var error = results.FirstOrDefault(r => r.Source == source).Error;
                    failures.Add($"[{source}] missing — expected non-null detail but got: {error ?? "no result"}");
                    continue;
                }
                failures.AddRange(expectedEntry.CompareTo(actualEntry, source));
            }

            // Sources present in current run but absent from fixture are reported
            // as a notice, not a failure — the user may have added a source after
            // generating the fixture. Run GenerateFixture again to refresh.
            var newSources = actualBySource.Keys.Except(expected.Keys).ToList();
            if (newSources.Any())
            {
                Console.WriteLine($"Note: {newSources.Count} source(s) returned details that aren't in the fixture: {string.Join(", ", newSources)}. Re-run GenerateFixture to capture them.");
            }

            if (failures.Any())
            {
                Assert.Fail($"Fixture mismatch ({failures.Count} fields):\n  " + string.Join("\n  ", failures));
            }
        }

        // Resolves the on-disk fixture path next to this test file. Uses
        // [CallerFilePath] so the path tracks where the source actually lives,
        // not the test bin output directory.
        private static string ResolveFixturePath(string number, [CallerFilePath] string? callerFile = null)
        {
            var dir = Path.GetDirectoryName(callerFile)!;
            return Path.Combine(dir, "Fixtures", "Av", $"{number}.json");
        }

        private static async Task<SourceResult[]> DispatchAll(string number)
        {
            var sp = BuildServiceProvider();
            var dispatchers = BuildDispatchers(sp);
            return await Task.WhenAll(dispatchers.Select(async kvp =>
            {
                try
                {
                    var detail = await kvp.Value(number);
                    return new SourceResult(kvp.Key, detail, Error: null);
                }
                catch (Exception ex)
                {
                    return new SourceResult(kvp.Key, Detail: null, Error: ex.Message);
                }
            }));
        }

        public static async Task RunAllSources(string number)
        {
            var results = await DispatchAll(number);
            var hits = results.Where(r => r.Detail != null).ToList();
            var misses = results.Where(r => r.Detail == null).ToList();

            Console.WriteLine($"=== {number}: {hits.Count}/{results.Length} sources returned details ===");
            foreach (var r in results.OrderBy(r => r.Source))
            {
                if (r.Detail != null)
                {
                    Console.WriteLine($"  [OK]   {r.Source,-18} series='{r.Detail.Series}' studio='{r.Detail.Studio}' publisher='{r.Detail.Publisher}' director='{r.Detail.Director}'");
                }
                else
                {
                    Console.WriteLine($"  [MISS] {r.Source,-18} {(r.Error ?? "no result")}");
                }
            }

            // Dump full JSON for inspection
            var debugDir = Path.Combine(Path.GetTempPath(), "bakabase_av_test");
            Directory.CreateDirectory(debugDir);
            var debugPath = Path.Combine(debugDir, $"{number}_{DateTime.Now:yyyyMMdd_HHmmss}.json");
            await File.WriteAllTextAsync(debugPath,
                JsonConvert.SerializeObject(hits.Select(h => new { h.Source, h.Detail }), Formatting.Indented));
            Console.WriteLine($"Per-source detail JSON written to {debugPath}");

            // Aggregate field-level duplication failures across all sources so users see
            // every offender at once instead of one-at-a-time test failures.
            var failures = new List<string>();
            foreach (var (source, detail, _) in hits)
            {
                if (detail == null) continue;
                if (!string.Equals(detail.Source, source, StringComparison.Ordinal))
                {
                    failures.Add($"[{source}] IAvDetail.Source='{detail.Source}' does not match dispatcher key '{source}' — preferred-source filtering will silently drop this client's results");
                }
                CheckField(source, "Series", detail.Series, number, failures);
                CheckField(source, "Title", detail.Title, number, failures);
                CheckField(source, "OriginalTitle", detail.OriginalTitle, number, failures);
                CheckField(source, "Studio", detail.Studio, number, failures);
                CheckField(source, "Publisher", detail.Publisher, number, failures);
                CheckField(source, "Director", detail.Director, number, failures);
            }

            if (failures.Any())
            {
                Assert.Fail("Duplication found in parsed fields:\n  " + string.Join("\n  ", failures));
            }
        }

        private static void CheckField(string source, string field, string? value, string number, List<string> failures)
        {
            try
            {
                ParsingDuplicationAssert.NotDuplicated(value, field, number);
            }
            catch (AssertFailedException ex)
            {
                failures.Add($"[{source}] {ex.Message}");
            }
        }

        private record SourceResult(string Source, IAvDetail? Detail, string? Error);

        private static IServiceProvider BuildServiceProvider()
        {
            var di = new ServiceCollection();
            di.AddLogging();
            di.AddHttpClient(InternalOptions.HttpClientNames.Default,
                c => c.DefaultRequestHeaders.Add("User-Agent", InternalOptions.DefaultHttpUserAgent));

            di.AddSingleton<Bakabase.Modules.ThirdParty.ThirdParties.Av.IAvSourceOptionsProvider,
                Bakabase.Modules.ThirdParty.ThirdParties.Av.DefaultAvSourceOptionsProvider>();

            di.AddSingleton<AiravClient>();
            di.AddSingleton<AvsexClient>();
            di.AddSingleton<AvsoxClient>();
            di.AddSingleton<CNMDBClient>();
            di.AddSingleton<DmmClient>();
            di.AddSingleton<DahliaClient>();
            di.AddSingleton<FC2Client>();
            di.AddSingleton<FalenoClient>();
            di.AddSingleton<FantasticaClient>();
            di.AddSingleton<Fc2hubClient>();
            di.AddSingleton<FreejavbtClient>();
            di.AddSingleton<GetchuDlClient>();
            di.AddSingleton<IqqtvClient>();
            di.AddSingleton<Jav321Client>();
            di.AddSingleton<JavbusClient>();
            di.AddSingleton<JavdayClient>();
            di.AddSingleton<JavdbClient>();
            di.AddSingleton<JavlibraryClient>();
            di.AddSingleton<LulubarClient>();
            di.AddSingleton<MmtvClient>();

            return di.BuildServiceProvider();
        }

        private static Dictionary<string, Func<string, Task<IAvDetail?>>> BuildDispatchers(IServiceProvider sp)
        {
            // Mirrors AvEnhancer's dispatcher table — keep in sync when adding sources.
            return new Dictionary<string, Func<string, Task<IAvDetail?>>>
            {
                { AvSourceIds.Airav,      n => Wrap(sp.GetRequiredService<AiravClient>().SearchAndParseVideo(n)) },
                { AvSourceIds.Avsex,      n => Wrap(sp.GetRequiredService<AvsexClient>().SearchAndParseVideo(n)) },
                { AvSourceIds.Avsox,      n => Wrap(sp.GetRequiredService<AvsoxClient>().SearchAndParseVideo(n)) },
                { AvSourceIds.Cnmdb,      n => Wrap(sp.GetRequiredService<CNMDBClient>().SearchAndParseVideo(n)) },
                { AvSourceIds.Dmm,        n => Wrap(sp.GetRequiredService<DmmClient>().SearchAndParseVideo(n)) },
                { AvSourceIds.Dahlia,     n => Wrap(sp.GetRequiredService<DahliaClient>().SearchAndParseVideo(n)) },
                { AvSourceIds.Fc2,        n => Wrap(sp.GetRequiredService<FC2Client>().SearchAndParseVideo(n)) },
                { AvSourceIds.Faleno,     n => Wrap(sp.GetRequiredService<FalenoClient>().SearchAndParseVideo(n)) },
                { AvSourceIds.Fantastica, n => Wrap(sp.GetRequiredService<FantasticaClient>().SearchAndParseVideo(n)) },
                { AvSourceIds.Fc2Hub,     n => Wrap(sp.GetRequiredService<Fc2hubClient>().SearchAndParseVideo(n)) },
                { AvSourceIds.Freejavbt,  n => Wrap(sp.GetRequiredService<FreejavbtClient>().SearchAndParseVideo(n)) },
                { AvSourceIds.GetchuDl,   n => Wrap(sp.GetRequiredService<GetchuDlClient>().SearchAndParseVideo(n)) },
                { AvSourceIds.Iqqtv,      n => Wrap(sp.GetRequiredService<IqqtvClient>().SearchAndParseVideo(n)) },
                { AvSourceIds.Jav321,     n => Wrap(sp.GetRequiredService<Jav321Client>().SearchAndParseVideo(n)) },
                { AvSourceIds.Javbus,     n => Wrap(sp.GetRequiredService<JavbusClient>().SearchAndParseVideo(n)) },
                { AvSourceIds.Javday,     n => Wrap(sp.GetRequiredService<JavdayClient>().SearchAndParseVideo(n)) },
                { AvSourceIds.Javdb,      n => Wrap(sp.GetRequiredService<JavdbClient>().SearchAndParseVideo(n)) },
                { AvSourceIds.Javlibrary, n => Wrap(sp.GetRequiredService<JavlibraryClient>().SearchAndParseVideo(n)) },
                { AvSourceIds.Lulubar,    n => Wrap(sp.GetRequiredService<LulubarClient>().SearchAndParseVideo(n)) },
                { AvSourceIds.Mmtv,       n => Wrap(sp.GetRequiredService<MmtvClient>().SearchAndParseVideo(n)) },
            };
        }

        private static async Task<IAvDetail?> Wrap<T>(Task<T?> task) where T : class, IAvDetail =>
            await task;
    }
}
