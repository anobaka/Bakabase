using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Modules.ThirdParty.ThirdParties.Airav;
using Bakabase.Modules.ThirdParty.ThirdParties.AiravCC;
using Bakabase.Modules.ThirdParty.ThirdParties.Avsex;
using Bakabase.Modules.ThirdParty.ThirdParties.Avsox;
using Bakabase.Modules.ThirdParty.ThirdParties.CableAV;
using Bakabase.Modules.ThirdParty.ThirdParties.CNMDB;
using Bakabase.Modules.ThirdParty.ThirdParties.Dahlia;
using Bakabase.Modules.ThirdParty.ThirdParties.Dmm;
using Bakabase.Modules.ThirdParty.ThirdParties.FC2;
using Bakabase.Modules.ThirdParty.ThirdParties.Faleno;
using Bakabase.Modules.ThirdParty.ThirdParties.Fantastica;
using Bakabase.Modules.ThirdParty.ThirdParties.Fc2club;
using Bakabase.Modules.ThirdParty.ThirdParties.Fc2hub;
using Bakabase.Modules.ThirdParty.ThirdParties.Fc2ppvdb;
using Bakabase.Modules.ThirdParty.ThirdParties.Freejavbt;
using Bakabase.Modules.ThirdParty.ThirdParties.Getchu;
using Bakabase.Modules.ThirdParty.ThirdParties.GetchuDl;
using Bakabase.Modules.ThirdParty.ThirdParties.Giga;
using Bakabase.Modules.ThirdParty.ThirdParties.Hdouban;
using Bakabase.Modules.ThirdParty.ThirdParties.Hscangku;
using Bakabase.Modules.ThirdParty.ThirdParties.Iqqtv;
using Bakabase.Modules.ThirdParty.ThirdParties.IqqtvNew;
using Bakabase.Modules.ThirdParty.ThirdParties.Jav321;
using Bakabase.Modules.ThirdParty.ThirdParties.Javbus;
using Bakabase.Modules.ThirdParty.ThirdParties.Javday;
using Bakabase.Modules.ThirdParty.ThirdParties.Javdb;
using Bakabase.Modules.ThirdParty.ThirdParties.Javlibrary;
using Bakabase.Modules.ThirdParty.ThirdParties.Kin8;
using Bakabase.Modules.ThirdParty.ThirdParties.Love6;
using Bakabase.Modules.ThirdParty.ThirdParties.Lulubar;
using Bakabase.Modules.ThirdParty.ThirdParties.Madouqu;
using Bakabase.Modules.ThirdParty.ThirdParties.Mdtv;
using Bakabase.Modules.ThirdParty.ThirdParties.Mgstage;
using Bakabase.Modules.ThirdParty.ThirdParties.Mmtv;
using Bakabase.Modules.ThirdParty.ThirdParties.Mywife;
using Bakabase.Modules.ThirdParty.ThirdParties.Official;
using Bakabase.Modules.ThirdParty.ThirdParties.Prestige;
using Bakabase.Modules.ThirdParty.ThirdParties.ThePornDB;
using Bakabase.Modules.ThirdParty.ThirdParties.ThePornDBMovies;
using Bakabase.Modules.ThirdParty.ThirdParties.Xcity;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;

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
        private const string DefaultNumber = "SSIS-001";

        [TestMethod]
        [Ignore("Manual integration test — hits every live AV source in parallel. Remove [Ignore] to run.")]
        public Task SearchAndParseVideo_AllSources_NoFieldDuplication()
            => RunAllSources(DefaultNumber);

        public static async Task RunAllSources(string number)
        {
            var sp = BuildServiceProvider();
            var dispatchers = BuildDispatchers(sp);

            var results = await Task.WhenAll(dispatchers.Select(async kvp =>
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

            di.AddSingleton<AiravClient>();
            di.AddSingleton<AiravCCClient>();
            di.AddSingleton<AvsexClient>();
            di.AddSingleton<AvsoxClient>();
            di.AddSingleton<CableAVClient>();
            di.AddSingleton<CNMDBClient>();
            di.AddSingleton<DmmClient>();
            di.AddSingleton<DahliaClient>();
            di.AddSingleton<FC2Client>();
            di.AddSingleton<FalenoClient>();
            di.AddSingleton<FantasticaClient>();
            di.AddSingleton<Fc2clubClient>();
            di.AddSingleton<Fc2hubClient>();
            di.AddSingleton<Fc2ppvdbClient>();
            di.AddSingleton<FreejavbtClient>();
            di.AddSingleton<GetchuClient>();
            di.AddSingleton<GetchuDlClient>();
            di.AddSingleton<GigaClient>();
            di.AddSingleton<HdoubanClient>();
            di.AddSingleton<HscangkuClient>();
            di.AddSingleton<IqqtvClient>();
            di.AddSingleton<IqqtvNewClient>();
            di.AddSingleton<Jav321Client>();
            di.AddSingleton<JavbusClient>();
            di.AddSingleton<JavdayClient>();
            di.AddSingleton<JavdbClient>();
            di.AddSingleton<JavlibraryClient>();
            di.AddSingleton<Kin8Client>();
            di.AddSingleton<Love6Client>();
            di.AddSingleton<LulubarClient>();
            di.AddSingleton<MadouquClient>();
            di.AddSingleton<MdtvClient>();
            di.AddSingleton<MgstageClient>();
            di.AddSingleton<MmtvClient>();
            di.AddSingleton<MywifeClient>();
            di.AddSingleton<OfficialClient>();
            di.AddSingleton<PrestigeClient>();
            di.AddSingleton<ThePornDBClient>();
            di.AddSingleton<ThePornDBMoviesClient>();
            di.AddSingleton<XcityClient>();

            return di.BuildServiceProvider();
        }

        private static Dictionary<string, Func<string, Task<IAvDetail?>>> BuildDispatchers(IServiceProvider sp)
        {
            // Mirrors AvEnhancer's dispatcher table — keep in sync when adding sources.
            return new Dictionary<string, Func<string, Task<IAvDetail?>>>
            {
                { "airav",          n => Wrap(sp.GetRequiredService<AiravClient>().SearchAndParseVideo(n)) },
                { "airavcc",        n => Wrap(sp.GetRequiredService<AiravCCClient>().SearchAndParseVideo(n)) },
                { "avsex",          n => Wrap(sp.GetRequiredService<AvsexClient>().SearchAndParseVideo(n)) },
                { "avsox",          n => Wrap(sp.GetRequiredService<AvsoxClient>().SearchAndParseVideo(n)) },
                { "cableav",        n => Wrap(sp.GetRequiredService<CableAVClient>().SearchAndParseVideo(n)) },
                { "cnmdb",          n => Wrap(sp.GetRequiredService<CNMDBClient>().SearchAndParseVideo(n)) },
                { "dmm",            n => Wrap(sp.GetRequiredService<DmmClient>().SearchAndParseVideo(n)) },
                { "dahlia",         n => Wrap(sp.GetRequiredService<DahliaClient>().SearchAndParseVideo(n)) },
                { "fc2",            n => Wrap(sp.GetRequiredService<FC2Client>().SearchAndParseVideo(n)) },
                { "faleno",         n => Wrap(sp.GetRequiredService<FalenoClient>().SearchAndParseVideo(n)) },
                { "fantastica",     n => Wrap(sp.GetRequiredService<FantasticaClient>().SearchAndParseVideo(n)) },
                { "fc2club",        n => Wrap(sp.GetRequiredService<Fc2clubClient>().SearchAndParseVideo(n)) },
                { "fc2hub",         n => Wrap(sp.GetRequiredService<Fc2hubClient>().SearchAndParseVideo(n)) },
                { "fc2ppvdb",       n => Wrap(sp.GetRequiredService<Fc2ppvdbClient>().SearchAndParseVideo(n)) },
                { "freejavbt",      n => Wrap(sp.GetRequiredService<FreejavbtClient>().SearchAndParseVideo(n)) },
                { "getchu",         n => Wrap(sp.GetRequiredService<GetchuClient>().SearchAndParseVideo(n)) },
                { "getchudl",       n => Wrap(sp.GetRequiredService<GetchuDlClient>().SearchAndParseVideo(n)) },
                { "giga",           n => Wrap(sp.GetRequiredService<GigaClient>().SearchAndParseVideo(n)) },
                { "hdouban",        n => Wrap(sp.GetRequiredService<HdoubanClient>().SearchAndParseVideo(n)) },
                { "hscangku",       n => Wrap(sp.GetRequiredService<HscangkuClient>().SearchAndParseVideo(n)) },
                { "iqqtv",          n => Wrap(sp.GetRequiredService<IqqtvClient>().SearchAndParseVideo(n)) },
                { "iqqtvnew",       n => Wrap(sp.GetRequiredService<IqqtvNewClient>().SearchAndParseVideo(n)) },
                { "jav321",         n => Wrap(sp.GetRequiredService<Jav321Client>().SearchAndParseVideo(n)) },
                { "javbus",         n => Wrap(sp.GetRequiredService<JavbusClient>().SearchAndParseVideo(n)) },
                { "javday",         n => Wrap(sp.GetRequiredService<JavdayClient>().SearchAndParseVideo(n)) },
                { "javdb",          n => Wrap(sp.GetRequiredService<JavdbClient>().SearchAndParseVideo(n)) },
                { "javlibrary",     n => Wrap(sp.GetRequiredService<JavlibraryClient>().SearchAndParseVideo(n)) },
                { "kin8",           n => Wrap(sp.GetRequiredService<Kin8Client>().SearchAndParseVideo(n)) },
                { "love6",          n => Wrap(sp.GetRequiredService<Love6Client>().SearchAndParseVideo(n)) },
                { "lulubar",        n => Wrap(sp.GetRequiredService<LulubarClient>().SearchAndParseVideo(n)) },
                { "madouqu",        n => Wrap(sp.GetRequiredService<MadouquClient>().SearchAndParseVideo(n)) },
                { "mdtv",           n => Wrap(sp.GetRequiredService<MdtvClient>().SearchAndParseVideo(n)) },
                { "mgstage",        n => Wrap(sp.GetRequiredService<MgstageClient>().SearchAndParseVideo(n)) },
                { "mmtv",           n => Wrap(sp.GetRequiredService<MmtvClient>().SearchAndParseVideo(n)) },
                { "mywife",         n => Wrap(sp.GetRequiredService<MywifeClient>().SearchAndParseVideo(n)) },
                { "official",       n => Wrap(sp.GetRequiredService<OfficialClient>().SearchAndParseVideo(n)) },
                { "prestige",       n => Wrap(sp.GetRequiredService<PrestigeClient>().SearchAndParseVideo(n)) },
                { "theporndb",      n => Wrap(sp.GetRequiredService<ThePornDBClient>().SearchAndParseVideo(n)) },
                { "theporndbmovies",n => Wrap(sp.GetRequiredService<ThePornDBMoviesClient>().SearchAndParseVideo(n)) },
                { "xcity",          n => Wrap(sp.GetRequiredService<XcityClient>().SearchAndParseVideo(n)) },
            };
        }

        private static async Task<IAvDetail?> Wrap<T>(Task<T?> task) where T : class, IAvDetail =>
            await task;
    }
}
