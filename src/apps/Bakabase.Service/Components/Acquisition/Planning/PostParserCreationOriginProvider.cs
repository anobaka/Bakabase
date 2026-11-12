using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

public sealed class PostParserCreationOriginProvider(BakabaseDbContext db) : IAcquisitionCreationOriginProvider
{
    public string Module => "postParser";
    public async Task<IReadOnlyDictionary<WorkflowSourceRef, AcquisitionCreationOrigin>> ResolveAsync(
        IReadOnlyList<WorkflowSourceRef> references, CancellationToken ct)
    {
        var ids = references.Where(r => r.EntityKind == "task").Select(r => ParseId(r.EntityId)).Where(i => i.HasValue)
            .Select(i => i!.Value).Distinct().ToArray();
        // Do not load the original body, parser result or any credentials merely to show provenance.
        var posts = await db.PostParserTasks.AsNoTracking().Where(p => ids.Contains(p.Id))
            .Select(p => new {p.Id, p.Title, p.Link, p.IsDeleted}).ToDictionaryAsync(p => p.Id, ct);
        return references.ToDictionary(r => r, r =>
        {
            var id = r.EntityKind == "task" ? ParseId(r.EntityId) : null;
            var post = id.HasValue ? posts.GetValueOrDefault(id.Value) : null;
            return new AcquisitionCreationOrigin {Kind = "postParser", Module = r.Module, EntityKind = r.EntityKind,
                EntityId = r.EntityId, Revision = r.Revision, Title = post?.Title, Url = AcquisitionCreationOriginService.SafeUrl(post?.Link),
                SourceState = post != null ? post.IsDeleted ? "deleted" : "available" : id.HasValue ? "missing" : "unknown"};
        });
    }
    private static int? ParseId(string value) => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0 ? id : null;
}
