using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Modules.ThirdParty.ThirdParties.Airav;
using Bakabase.Modules.ThirdParty.ThirdParties.AiravCC;
using Bakabase.Modules.ThirdParty.ThirdParties.Av;
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
using Bakabase.Service.Models.Input;
using Bakabase.Service.Models.View;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

/// <summary>
/// Endpoints for exercising AV scrapers in isolation — used by the test page so
/// users can validate per-source configuration without running a full enhancement.
/// </summary>
[Route("~/av")]
public class AvController(
    AiravClient airavClient,
    AiravCCClient airavCCClient,
    AvsexClient avsexClient,
    AvsoxClient avsoxClient,
    CableAVClient cableAvClient,
    CNMDBClient cnmdbClient,
    DmmClient dmmClient,
    DahliaClient dahliaClient,
    FC2Client fc2Client,
    FalenoClient falenoClient,
    FantasticaClient fantasticaClient,
    Fc2clubClient fc2clubClient,
    Fc2hubClient fc2hubClient,
    Fc2ppvdbClient fc2ppvdbClient,
    FreejavbtClient freejavbtClient,
    GetchuClient getchuClient,
    GetchuDlClient getchuDlClient,
    GigaClient gigaClient,
    HdoubanClient hdoubanClient,
    HscangkuClient hscangkuClient,
    IqqtvClient iqqtvClient,
    IqqtvNewClient iqqtvNewClient,
    Jav321Client jav321Client,
    JavbusClient javbusClient,
    JavdayClient javdayClient,
    JavdbClient javdbClient,
    JavlibraryClient javlibraryClient,
    Kin8Client kin8Client,
    Love6Client love6Client,
    LulubarClient lulubarClient,
    MadouquClient madouquClient,
    MdtvClient mdtvClient,
    MgstageClient mgstageClient,
    MmtvClient mmtvClient,
    MywifeClient mywifeClient,
    OfficialClient officialClient,
    PrestigeClient prestigeClient,
    ThePornDBClient thePornDbClient,
    ThePornDBMoviesClient thePornDbMoviesClient,
    XcityClient xcityClient,
    IAvSourceOptionsProvider avOptionsProvider)
    : Controller
{
    private Dictionary<string, System.Func<string, System.Threading.CancellationToken, Task<IAvDetail?>>> BuildDispatchers() =>
        new()
        {
            ["airav"] = async (n, _) => await airavClient.SearchAndParseVideo(n),
            ["airavcc"] = async (n, _) => await airavCCClient.SearchAndParseVideo(n),
            ["avsex"] = async (n, _) => await avsexClient.SearchAndParseVideo(n),
            ["avsox"] = async (n, _) => await avsoxClient.SearchAndParseVideo(n),
            ["cableav"] = async (n, _) => await cableAvClient.SearchAndParseVideo(n),
            ["cnmdb"] = async (n, _) => await cnmdbClient.SearchAndParseVideo(n),
            ["dmm"] = async (n, _) => await dmmClient.SearchAndParseVideo(n),
            ["dahlia"] = async (n, _) => await dahliaClient.SearchAndParseVideo(n),
            ["fc2"] = async (n, _) => await fc2Client.SearchAndParseVideo(n),
            ["faleno"] = async (n, _) => await falenoClient.SearchAndParseVideo(n),
            ["fantastica"] = async (n, _) => await fantasticaClient.SearchAndParseVideo(n),
            ["fc2club"] = async (n, _) => await fc2clubClient.SearchAndParseVideo(n),
            ["fc2hub"] = async (n, _) => await fc2hubClient.SearchAndParseVideo(n),
            ["fc2ppvdb"] = async (n, _) => await fc2ppvdbClient.SearchAndParseVideo(n),
            ["freejavbt"] = async (n, _) => await freejavbtClient.SearchAndParseVideo(n),
            ["getchu"] = async (n, _) => await getchuClient.SearchAndParseVideo(n),
            ["getchudl"] = async (n, _) => await getchuDlClient.SearchAndParseVideo(n),
            ["giga"] = async (n, _) => await gigaClient.SearchAndParseVideo(n),
            ["hdouban"] = async (n, _) => await hdoubanClient.SearchAndParseVideo(n),
            ["hscangku"] = async (n, _) => await hscangkuClient.SearchAndParseVideo(n),
            ["iqqtv"] = async (n, _) => await iqqtvClient.SearchAndParseVideo(n),
            ["iqqtvnew"] = async (n, _) => await iqqtvNewClient.SearchAndParseVideo(n),
            ["jav321"] = async (n, _) => await jav321Client.SearchAndParseVideo(n),
            ["javbus"] = async (n, _) => await javbusClient.SearchAndParseVideo(n),
            ["javday"] = async (n, _) => await javdayClient.SearchAndParseVideo(n),
            ["javdb"] = async (n, _) => await javdbClient.SearchAndParseVideo(n),
            ["javlibrary"] = async (n, _) => await javlibraryClient.SearchAndParseVideo(n),
            ["kin8"] = async (n, _) => await kin8Client.SearchAndParseVideo(n),
            ["love6"] = async (n, _) => await love6Client.SearchAndParseVideo(n),
            ["lulubar"] = async (n, _) => await lulubarClient.SearchAndParseVideo(n),
            ["madouqu"] = async (n, _) => await madouquClient.SearchAndParseVideo(n),
            ["mdtv"] = async (n, _) => await mdtvClient.SearchAndParseVideo(n),
            ["mgstage"] = async (n, _) => await mgstageClient.SearchAndParseVideo(n),
            ["mmtv"] = async (n, _) => await mmtvClient.SearchAndParseVideo(n),
            ["mywife"] = async (n, _) => await mywifeClient.SearchAndParseVideo(n),
            ["official"] = async (n, _) => await officialClient.SearchAndParseVideo(n),
            ["prestige"] = async (n, _) => await prestigeClient.SearchAndParseVideo(n),
            ["theporndb"] = async (n, _) => await thePornDbClient.SearchAndParseVideo(n),
            ["theporndbmovies"] = async (n, _) => await thePornDbMoviesClient.SearchAndParseVideo(n),
            ["xcity"] = async (n, _) => await xcityClient.SearchAndParseVideo(n),
        };

    [HttpGet("sources")]
    [SwaggerOperation(OperationId = "GetAvSources")]
    public ListResponse<AvSourceInfoViewModel> GetSources()
    {
        var dispatchers = BuildDispatchers();
        var infos = dispatchers.Keys.OrderBy(k => k).Select(k =>
        {
            var resolved = avOptionsProvider.Resolve(k);
            AvSourceDefaults.DefaultBaseUrls.TryGetValue(k, out var defaultBaseUrl);
            AvSourceDefaults.DefaultCookies.TryGetValue(k, out var defaultCookie);
            return new AvSourceInfoViewModel
            {
                Id = k,
                DefaultBaseUrl = defaultBaseUrl,
                DefaultCookie = defaultCookie,
                ResolvedBaseUrl = resolved.BaseUrl,
                ResolvedCookie = resolved.Cookie,
                Enabled = resolved.Enabled,
            };
        }).ToList();
        return new ListResponse<AvSourceInfoViewModel>(infos);
    }

    [HttpPost("test")]
    [SwaggerOperation(OperationId = "TestAvSources")]
    public async Task<ListResponse<AvSourceTestResultViewModel>> Test([FromBody] AvSourceTestInputModel input,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.Number))
        {
            return new ListResponse<AvSourceTestResultViewModel>([]);
        }

        var dispatchers = BuildDispatchers();
        var sources = (input.Sources != null && input.Sources.Count > 0)
            ? input.Sources.Where(dispatchers.ContainsKey).ToList()
            : dispatchers.Keys.ToList();

        var tasks = sources.Select(async source =>
        {
            var sw = Stopwatch.StartNew();
            var resolved = avOptionsProvider.Resolve(source);
            if (!resolved.Enabled)
            {
                return new AvSourceTestResultViewModel
                {
                    Source = source,
                    Skipped = true,
                    DurationMs = 0
                };
            }

            try
            {
                var detail = await dispatchers[source](input.Number, ct);
                sw.Stop();
                return new AvSourceTestResultViewModel
                {
                    Source = source,
                    Detail = detail == null ? null : ToView(detail),
                    DurationMs = sw.ElapsedMilliseconds
                };
            }
            catch (System.Exception ex)
            {
                sw.Stop();
                return new AvSourceTestResultViewModel
                {
                    Source = source,
                    Error = ex.Message,
                    DurationMs = sw.ElapsedMilliseconds
                };
            }
        });

        var results = await Task.WhenAll(tasks);
        return new ListResponse<AvSourceTestResultViewModel>(results.OrderBy(r => r.Source).ToList());
    }

    private static AvSourceTestDetailViewModel ToView(IAvDetail d) => new()
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
        CoverUrl = d.CoverUrl,
        PosterUrl = d.PosterUrl,
        Website = d.Website,
        Mosaic = d.Mosaic,
        SearchUrl = d.SearchUrl,
    };
}
