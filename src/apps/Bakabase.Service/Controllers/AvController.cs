using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Network;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Modules.ThirdParty.ThirdParties.Av;
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
    IEnumerable<IAvClient> avClients,
    IAvSourceOptionsProvider avOptionsProvider)
    : Controller
{
    // Indexed by SourceId so input.Sources lookups stay O(1); the IEnumerable is
    // resolved fresh per request to honor DI scoping.
    private Dictionary<string, IAvClient> BuildClients() =>
        avClients.ToDictionary(c => c.SourceId, c => c);

    [HttpGet("sources")]
    [SwaggerOperation(OperationId = "GetAvSources")]
    public ListResponse<AvSourceInfoViewModel> GetSources()
    {
        var clients = BuildClients();
        var infos = clients.Keys.OrderBy(k => k).Select(k =>
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

        var clients = BuildClients();
        var sources = (input.Sources != null && input.Sources.Count > 0)
            ? input.Sources.Where(clients.ContainsKey).ToList()
            : clients.Keys.ToList();

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
                var detail = await clients[source].SearchAndParseVideo(input.Number, language: language);
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
