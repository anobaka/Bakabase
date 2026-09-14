using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Db;
using Bakabase.InsideWorld.Business.Components.PostParser.Workflow;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Abstractions.Services;
using Bakabase.Modules.Acquisition.Extensions;
using Bakabase.Modules.Acquisition.Models.Input;
using Bakabase.Modules.PostParser.Models.Domain;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition;

public record PostParserAcquisitionResult(int ResourceId, bool Created, int LeadCount);

/// <summary>Imports the user's selected links without fetching, parsing or downloading again.</summary>
public class PostParserAcquisitionService(BakabaseDbContext db, IPlaceholderResourceService placeholders,
    IAcquisitionLeadService leads, IResourceService resources, PostParserTaskExecutionGate gate)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> FileExtensions = new(StringComparer.OrdinalIgnoreCase)
        {".zip", ".7z", ".rar", ".tar", ".gz", ".bz2", ".xz", ".pdf", ".epub", ".mp4", ".mkv", ".mp3", ".flac", ".iso"};

    public async Task<PostParserAcquisitionResult> ImportAsync(int taskId, int revision,
        string? title, IReadOnlyList<int> resourceIndices, CancellationToken ct)
    {
        await gate.Semaphore.WaitAsync(ct);
        try
        {
            var task = await db.Set<PostParserTaskDbModel>().AsNoTracking()
                .SingleOrDefaultAsync(t => t.Id == taskId && !t.IsDeleted, ct)
                ?? throw new ArgumentException("The parsed post no longer exists.");
            if (task.Revision != revision)
                throw new InvalidOperationException("The post was parsed again. Refresh its results before importing.");
            if (task.Error != null || string.IsNullOrWhiteSpace(task.Results))
                throw new ArgumentException("Parse this post successfully before importing its results.");
            var parsed = ReadResources(task.Results);
            if (resourceIndices.Count is 0 or > 500 || resourceIndices.Any(i => i < 0 || i >= parsed.Count))
                throw new ArgumentException("Select valid download links from the current result.");
            var selected = resourceIndices.Distinct().Select(i => parsed[i]).ToList();
            foreach (var link in selected)
            {
                if (string.IsNullOrWhiteSpace(link.Link) || link.Link.Length > 2048 ||
                    !Uri.TryCreate(link.Link, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http" or "magnet") ||
                    link.Code?.Length > 512 || link.Password?.Length > 2048)
                    throw new ArgumentException("The result contains an invalid or oversized download link or password.");
            }
            var name = (title ?? task.Title)?.Trim();
            if (string.IsNullOrWhiteSpace(name) || name.Length > 1024)
                throw new ArgumentException("Enter a resource name of at most 1024 characters.");

            var imports = new List<(PostDownloadResource Link, AcquisitionLeadKind Kind, AcquisitionLead? Existing)>();
            foreach (var group in selected.GroupBy(r => r.Link.NormalizeLeadValue()))
            {
                if (group.Select(r => (r.Code, r.Password)).Distinct().Count() > 1)
                    throw new ArgumentException("The same link has different codes or passwords. Select one version.");
                var link = group.First();
                var kind = Classify(link.Link);
                AcquisitionLead? existing = null;
                foreach (var possibleKind in AcquisitionLeadExtensions.StorableKinds)
                {
                    existing = await leads.FindByValue(possibleKind, link.Link);
                    if (existing != null) break;
                }
                if (existing != null &&
                    ((existing.AccessCode != null && link.Code != null && existing.AccessCode != link.Code) ||
                     (existing.Password != null && link.Password != null && existing.Password != link.Password)))
                    throw new InvalidOperationException("A selected link already has a different access code or password. Resolve the conflicting information before importing.");
                imports.Add((link, existing?.Kind ?? kind, existing));
            }
            var matches = imports.Where(i => i.Existing != null).Select(i => i.Existing!.ResourceId).Distinct().ToList();
            if (matches.Count > 1)
                throw new InvalidOperationException("These links already belong to different resources. Import them separately.");

            var matchedId = matches.SingleOrDefault();
            if (matchedId != 0 && (await resources.Get(matchedId))?.HasLocalPath == true)
                throw new InvalidOperationException("This resource already has local files.");
            var result = matchedId == 0
                ? await placeholders.CreateByTitle(name, ct)
                : new PlaceholderResourceResult(matchedId, false, name);
            if ((await resources.Get(result.ResourceId))?.HasLocalPath == true)
                throw new InvalidOperationException("A local resource already uses this name. Choose another name.");

            foreach (var (link, kind, _) in imports)
            {
                var added = await leads.Add(result.ResourceId, new AcquisitionLeadAddInputModel
                {
                    Kind = kind, Value = link.Link, Origin = AcquisitionLeadOrigin.PostParser,
                    AccessCode = link.Code, Password = link.Password,
                    IsResolved = true,
                    SourceReference = string.IsNullOrWhiteSpace(task.Text) && task.Link.Length <= 2048 ? task.Link : null
                });
                if (added.ConflictingResourceId != null)
                    throw new InvalidOperationException("A selected link was just attached to another resource. Refresh and try again.");
            }
            return new PostParserAcquisitionResult(result.ResourceId, result.Created, imports.Count);
        }
        finally { gate.Semaphore.Release(); }
    }

    internal static List<PostDownloadResource> ReadResources(string json)
    {
        const string invalidFormat = "The stored parsing result has an invalid format. Parse the post again.";
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new ArgumentException(invalidFormat);
            if (!root.TryGetProperty("DownloadInfo", out var data) && !root.TryGetProperty("1", out data))
                throw new ArgumentException("The post has no download-information result. Parse the post again.");
            if (data.ValueKind != JsonValueKind.Object)
                throw new ArgumentException(invalidFormat);
            if (data.TryGetProperty("data", out var nested) || data.TryGetProperty("Data", out nested))
            {
                if (nested.ValueKind != JsonValueKind.Object)
                    throw new ArgumentException(invalidFormat);
                data = nested;
            }
            var resources = data.Deserialize<PostDownloadInfo>(Json)?.Resources ?? [];
            if (resources.Any(resource => resource == null))
                throw new ArgumentException(invalidFormat);
            return resources;
        }
        catch (JsonException ex)
        {
            throw new ArgumentException(invalidFormat, ex);
        }
    }

    internal static AcquisitionLeadKind Classify(string link)
    {
        var uri = new Uri(link);
        if (uri.Scheme == "magnet") return AcquisitionLeadKind.Magnet;
        return FileExtensions.Contains(Path.GetExtension(uri.AbsolutePath))
            ? AcquisitionLeadKind.DirectUrl : AcquisitionLeadKind.SharedPage;
    }
}
