using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Network;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Modules.ThirdParty.ThirdParties.Airav;
using Bakabase.Modules.ThirdParty.ThirdParties.Av;
using Bakabase.Modules.ThirdParty.ThirdParties.Avsex;
using Bakabase.Modules.ThirdParty.ThirdParties.Avsox;
using Bakabase.Modules.ThirdParty.ThirdParties.CNMDB;
using Bakabase.Modules.ThirdParty.ThirdParties.Dahlia;
using Bakabase.Modules.ThirdParty.ThirdParties.DMM;
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
    AvsexClient avsexClient,
    AvsoxClient avsoxClient,
    CNMDBClient cnmdbClient,
    DmmClient dmmClient,
    DahliaClient dahliaClient,
    FC2Client fc2Client,
    FalenoClient falenoClient,
    FantasticaClient fantasticaClient,
    Fc2hubClient fc2hubClient,
    FreejavbtClient freejavbtClient,
    GetchuDlClient getchuDlClient,
    IqqtvClient iqqtvClient,
    Jav321Client jav321Client,
    JavbusClient javbusClient,
    JavdayClient javdayClient,
    JavdbClient javdbClient,
    JavlibraryClient javlibraryClient,
    LulubarClient lulubarClient,
    MmtvClient mmtvClient,
    IAvSourceOptionsProvider avOptionsProvider)
    : Controller
{
    private Dictionary<string, System.Func<string, string, System.Threading.CancellationToken, Task<IAvDetail?>>> BuildDispatchers() =>
        new()
        {
            // (number, language, ct) — language only matters for sources whose URL embeds the locale.
            ["airav"] = async (n, lang, _) => await airavClient.SearchAndParseVideo(n, language: lang),
            ["avsex"] = async (n, _, _) => await avsexClient.SearchAndParseVideo(n),
            ["avsox"] = async (n, _, _) => await avsoxClient.SearchAndParseVideo(n),
            ["cnmdb"] = async (n, _, _) => await cnmdbClient.SearchAndParseVideo(n),
            ["dmm"] = async (n, _, _) => await dmmClient.SearchAndParseVideo(n),
            ["dahlia"] = async (n, _, _) => await dahliaClient.SearchAndParseVideo(n),
            ["fc2"] = async (n, _, _) => await fc2Client.SearchAndParseVideo(n),
            ["faleno"] = async (n, _, _) => await falenoClient.SearchAndParseVideo(n),
            ["fantastica"] = async (n, _, _) => await fantasticaClient.SearchAndParseVideo(n),
            ["fc2hub"] = async (n, _, _) => await fc2hubClient.SearchAndParseVideo(n),
            ["freejavbt"] = async (n, _, _) => await freejavbtClient.SearchAndParseVideo(n),
            ["getchudl"] = async (n, _, _) => await getchuDlClient.SearchAndParseVideo(n),
            ["iqqtv"] = async (n, lang, _) => await iqqtvClient.SearchAndParseVideo(n, language: lang),
            ["jav321"] = async (n, _, _) => await jav321Client.SearchAndParseVideo(n),
            ["javbus"] = async (n, _, _) => await javbusClient.SearchAndParseVideo(n),
            ["javday"] = async (n, _, _) => await javdayClient.SearchAndParseVideo(n),
            ["javdb"] = async (n, _, _) => await javdbClient.SearchAndParseVideo(n),
            ["javlibrary"] = async (n, lang, _) => await javlibraryClient.SearchAndParseVideo(n, language: lang),
            ["lulubar"] = async (n, _, _) => await lulubarClient.SearchAndParseVideo(n),
            ["mmtv"] = async (n, _, _) => await mmtvClient.SearchAndParseVideo(n),
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

        var language = NormalizeLanguage(input.Language);

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

            using var captureScope = HttpInteractionCapture.Begin();
            try
            {
                var detail = await dispatchers[source](input.Number, language, ct);
                sw.Stop();
                return new AvSourceTestResultViewModel
                {
                    Source = source,
                    Detail = detail == null ? null : ToView(detail),
                    DurationMs = sw.ElapsedMilliseconds,
                    Interactions = ToInteractionViews(HttpInteractionCapture.Current),
                };
            }
            catch (System.Exception ex)
            {
                sw.Stop();
                return new AvSourceTestResultViewModel
                {
                    Source = source,
                    Error = ex.Message,
                    DurationMs = sw.ElapsedMilliseconds,
                    Interactions = ToInteractionViews(HttpInteractionCapture.Current),
                };
            }
        });

        var results = await Task.WhenAll(tasks);
        return new ListResponse<AvSourceTestResultViewModel>(results.OrderBy(r => r.Source).ToList());
    }

    /// <summary>
    /// Map frontend i18n codes (zh-CN, en-US, ...) to the underscore form used by the
    /// language-aware AV clients (zh_cn, en, ...). Unknown values fall through unchanged
    /// so each client can apply its own default.
    /// </summary>
    private static string NormalizeLanguage(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "zh_cn";
        var lower = raw.Trim().ToLowerInvariant().Replace('-', '_');
        return lower switch
        {
            "zh_cn" or "cn" or "zh" or "zh_hans" => "zh_cn",
            "zh_tw" or "tw" or "zh_hant" => "zh_tw",
            "ja" or "jp" or "ja_jp" => "ja",
            "en" or "en_us" or "en_gb" => "en",
            _ => lower,
        };
    }

    private static List<AvSourceHttpInteractionViewModel>? ToInteractionViews(List<HttpInteractionRecord>? records)
    {
        if (records == null || records.Count == 0) return null;
        return records.Select(r => new AvSourceHttpInteractionViewModel
        {
            Method = r.Method,
            Url = r.Url,
            RequestHeaders = r.RequestHeaders,
            RequestBody = r.RequestBody,
            RequestContentType = r.RequestContentType,
            ResponseStatusCode = r.ResponseStatusCode,
            ResponseReasonPhrase = r.ResponseReasonPhrase,
            ResponseHeaders = r.ResponseHeaders,
            ResponseContentType = r.ResponseContentType,
            ResponseContentLength = r.ResponseContentLength,
            Error = r.Error,
            DurationMs = r.DurationMs,
        }).ToList();
    }

    private static AvSourceTestDetailViewModel ToView(IAvDetail d) => new()
    {
        Number = d.Number,
        Title = d.Title,
        OriginalTitle = d.OriginalTitle,
        Actor = d.Actor,
        Outline = d.Outline,
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
