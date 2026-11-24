using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.DownloadIntents;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

/// <summary>Download Center source keys, projected from saved choices without resolving links,
/// reading files, initializing downloaders or materializing full execution checkpoints.</summary>
internal sealed class AcquisitionDownloadSourceFilter(BakabaseDbContext db)
{
    internal static bool IsValid(string? source) => source == null || source is "http" or "bt" ||
        source.StartsWith("site:", StringComparison.Ordinal) && int.TryParse(source[5..], NumberStyles.None,
            CultureInfo.InvariantCulture, out var id) && Enum.IsDefined((ThirdPartyId)id) &&
        source == "site:" + id.ToString(CultureInfo.InvariantCulture);

    internal async Task<Dictionary<int, HashSet<string>>> ReadAsync(IReadOnlyCollection<int> taskIds, CancellationToken ct)
    {
        var sources = new Dictionary<int, HashSet<string>>();
        foreach (var batch in taskIds.Distinct().Chunk(100))
        {
            var rows = await db.Database.SqlQueryRaw<SourceFact>(SourcesSql, JsonSerializer.Serialize(batch)).ToListAsync(ct);
            foreach (var task in rows.GroupBy(r => r.TaskId))
            {
                // The saved execution choice wins over a plan, which wins over unselected
                // draft alternatives. A malformed fixed selection never revives its mirrors.
                var tier = task.Min(r => r.Tier);
                sources.Add(task.Key, task.Where(r => r.Tier == tier).Select(Key).OfType<string>().ToHashSet(StringComparer.Ordinal));
            }
        }
        return sources;
    }

    private static string? Key(SourceFact source)
    {
        if (source.LeadKind == (int)AcquisitionLeadKind.PlatformHolding)
        {
            var prefix = source.Url?.Split(':', 2)[0];
            if (Enum.TryParse<ResourceSource>(prefix, out var platform) && Enum.IsDefined(platform) &&
                Enum.TryParse<ThirdPartyId>(platform.ToString(), out var site)) return Site(site);
            return null;
        }
        if (source.LeadKind is (int)AcquisitionLeadKind.Magnet or (int)AcquisitionLeadKind.Torrent) return "bt";
        if (source.DriveKind == (int)AcquisitionDriveKind.Magnet) return "bt";
        var kind = source.Kind ?? (source.LeadKind == (int)AcquisitionLeadKind.DirectUrl ||
            source.DriveKind == (int)AcquisitionDriveKind.DirectUrl ? "http" : "auto");
        if (string.IsNullOrWhiteSpace(source.Url))
            return source.ProviderId == "exHentai" ? Site(ThirdPartyId.ExHentai) : null;
        string? provider;
        try
        {
            provider = DownloadSourceResolver.Normalize(new()
            {
                Id = "filter", Url = source.Url, Kind = kind,
                ProviderId = kind == "remote" ? source.ProviderId : null
            }).ProviderId;
        }
        catch (ArgumentException)
        {
            // Readiness, missing tools/accounts and malformed legacy method hints do not
            // change the source category. Recognition remains conservative for generic pages.
            provider = kind == "remote" ? source.ProviderId : CloudSourceClassifier.Classify(source.Url);
            if (kind == "share" && provider is "http" or "torrent") provider = "unknown";
        }
        if (provider is "magnet" or "torrent") return "bt";
        if (provider == "http") return "http";
        if (provider == "exHentai") return Site(ThirdPartyId.ExHentai);
        return CloudSites.Sources.FirstOrDefault(s => s.ProviderId == provider) is { } cloud ? Site(cloud.ThirdPartyId) : null;
    }

    private static string Site(ThirdPartyId site) => "site:" + ((int)site).ToString(CultureInfo.InvariantCulture);
    private sealed class SourceFact
    {
        public int TaskId { get; set; }
        public int Tier { get; set; }
        public string? Url { get; set; }
        public string? Kind { get; set; }
        public string? ProviderId { get; set; }
        public string? MethodId { get; set; }
        public int? DriveKind { get; set; }
        public int? LeadKind { get; set; }
    }

    // SQLite reads bounded documents and returns only source routing fields, never passwords,
    // file manifests, source history or the serialized execution payload. Malformed JSON and
    // non-object array entries degrade to an unknown category instead of breaking the list.
    // The byte-size gate can reject oversized UTF-8 text from column metadata before length()/JSON reads its pages.
    private const string SourcesSql = """
        WITH candidates AS (
            SELECT t.Id TaskId, t.BuilderId, t.LeadKind, t.LeadValue, t.PlanJson IS NOT NULL HasPlan,
                CASE WHEN octet_length(t.PlanJson)<=4194304 AND length(t.PlanJson)<=1048576 AND json_valid(t.PlanJson) THEN t.PlanJson ELSE '{{}}' END Plan,
                CASE WHEN octet_length(t.BuilderStateJson)<=4194304 AND length(t.BuilderStateJson)<=1048576 AND json_valid(t.BuilderStateJson) THEN t.BuilderStateJson ELSE '{{}}' END Draft,
                CASE WHEN octet_length(t.PreparedContextJson)<=4194304 AND length(t.PreparedContextJson)<=1048576 AND json_valid(t.PreparedContextJson) THEN t.PreparedContextJson ELSE '{{}}' END Prepared,
                CASE WHEN octet_length(r.CurrentItemJson)<=4194304 AND length(r.CurrentItemJson)<=1048576 AND json_valid(r.CurrentItemJson) THEN r.CurrentItemJson ELSE '{{}}' END Checkpoint
            FROM AcquisitionTasks t LEFT JOIN WorkflowRuns r ON r.Id=t.WorkflowRunId
            WHERE t.DeletedAt IS NULL AND t.Id IN (SELECT value FROM json_each({0}))
        ), bodies AS (
            SELECT *, CASE WHEN json_type(Checkpoint,'$.item')='object' THEN json_extract(Checkpoint,'$.item') ELSE Checkpoint END Body
            FROM candidates
        ), worksets AS (
            SELECT *, CASE WHEN json_type(Body,'$.variables."download.workset"')='text' AND
                    json_valid(json_extract(Body,'$.variables."download.workset"'))
                THEN json_extract(Body,'$.variables."download.workset"') ELSE Body END Workset
            FROM bodies
        ), targets AS (
            SELECT w.TaskId, CASE WHEN p.type='object' THEN p.value ELSE '{{}}' END Part
            FROM worksets w, json_each(CASE WHEN json_type(Workset,'$.targets')='array' AND
                json_array_length(Workset,'$.targets') BETWEEN 1 AND 100 THEN json_extract(Workset,'$.targets') ELSE '[]' END) p
        ), planned AS (
            SELECT w.TaskId, CASE WHEN p.type='object' THEN p.value ELSE '{{}}' END Part
            FROM worksets w, json_each(CASE WHEN json_type(Plan,'$.parts')='array' AND
                json_array_length(Plan,'$.parts') BETWEEN 1 AND 100 THEN json_extract(Plan,'$.parts') ELSE '[]' END) p
        ), documents AS (
            SELECT p.TaskId, 0 Tier, CASE WHEN s.type='object' THEN s.value ELSE '{{}}' END Source
            FROM targets p LEFT JOIN json_each(CASE WHEN json_type(Part,'$.sources')='array' AND
                json_array_length(Part,'$.sources')<=100 THEN json_extract(Part,'$.sources') ELSE '[]' END) s
                ON json_extract(CASE WHEN s.type='object' THEN s.value ELSE '{{}}' END,'$.id')=json_extract(Part,'$.selectedSourceId')
            WHERE json_type(Part,'$.selectedSourceId')='text' AND COALESCE(json_extract(Part,'$.excluded'),0)=0
            UNION ALL
            SELECT p.TaskId, 1, CASE WHEN s.type='object' THEN s.value ELSE '{{}}' END
            FROM planned p LEFT JOIN json_each(CASE WHEN json_type(Part,'$.sources')='array' AND
                json_array_length(Part,'$.sources')<=100 THEN json_extract(Part,'$.sources') ELSE '[]' END) s
                ON json_extract(CASE WHEN s.type='object' THEN s.value ELSE '{{}}' END,'$.id')=json_extract(Part,'$.selectedSourceId')
            UNION ALL
            SELECT TaskId, 2, json_extract(Body,'$.variables."download.resolvedSource"') FROM worksets
            WHERE json_type(Body,'$.variables."download.resolvedSource"')='text' AND json_valid(json_extract(Body,'$.variables."download.resolvedSource"'))
            UNION ALL
            SELECT w.TaskId, 3, CASE WHEN link.type='object' THEN link.value ELSE '{{}}' END
            FROM worksets w, json_each(CASE WHEN json_type(Body,'$.links')='array' AND json_array_length(Body,'$.links')<=500
                THEN json_extract(Body,'$.links') ELSE '[]' END) link
            WHERE json_type(Body,'$.selectedLinkIndex')='integer' AND link.key=json_extract(Body,'$.selectedLinkIndex')
            UNION ALL
            SELECT TaskId, 4, json_extract(Prepared,'$.variables."download.resolvedSource"') FROM worksets
            WHERE NOT HasPlan AND json_type(Prepared,'$.variables."download.resolvedSource"')='text' AND json_valid(json_extract(Prepared,'$.variables."download.resolvedSource"'))
            UNION ALL
            SELECT TaskId, 5, json_extract(Prepared,'$.variables."download.candidate"') FROM worksets
            WHERE NOT HasPlan AND json_type(Prepared,'$.variables."download.candidate"')='text' AND json_valid(json_extract(Prepared,'$.variables."download.candidate"'))
            UNION ALL
            SELECT w.TaskId, 6, CASE WHEN s.type='object' THEN s.value ELSE '{{}}' END
            FROM worksets w, json_each(CASE WHEN NOT HasPlan AND json_type(Draft,'$.draftSources')='array' AND
                json_array_length(Draft,'$.draftSources')<=500 THEN json_extract(Draft,'$.draftSources') ELSE '[]' END) s
        )
        SELECT TaskId,Tier,
            CASE WHEN json_type(Source,'$.url')='text' AND length(json_extract(Source,'$.url'))<=8192 THEN json_extract(Source,'$.url') END Url,
            CASE WHEN json_type(Source,'$.kind')='text' AND length(json_extract(Source,'$.kind'))<=32 THEN json_extract(Source,'$.kind') END Kind,
            CASE WHEN json_type(Source,'$.providerId')='text' AND length(json_extract(Source,'$.providerId'))<=128 THEN json_extract(Source,'$.providerId') END ProviderId,
            CASE WHEN json_type(Source,'$.methodId')='text' AND length(json_extract(Source,'$.methodId'))<=128 THEN json_extract(Source,'$.methodId') END MethodId,
            CASE WHEN json_type(Source,'$.driveKind')='integer' THEN json_extract(Source,'$.driveKind') END DriveKind,
            NULL LeadKind
        FROM documents
        UNION ALL
        SELECT TaskId,7,NULL,NULL,'exHentai',NULL,NULL,NULL FROM candidates
        WHERE NOT HasPlan AND BuilderId='exhentai.search'
        UNION ALL
        SELECT TaskId,8,LeadValue,NULL,NULL,NULL,NULL,LeadKind FROM candidates
        WHERE NOT HasPlan AND BuilderId IS NULL AND LeadKind IN (1,4,5,7)
        """;
}
