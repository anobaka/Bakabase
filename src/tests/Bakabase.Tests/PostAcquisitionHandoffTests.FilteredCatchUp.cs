using Bakabase.InsideWorld.Business.Components.PostParser.Models.Db;
using Bakabase.InsideWorld.Business.Components.PostParser.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

public sealed partial class PostAcquisitionHandoffTests
{
    [TestMethod]
    public async Task FilteredCatchUpIncludesAllMatchingPagesAndReportsExistingAndRejectedWithoutWrites()
    {
        var selected = new List<int>();
        for (var index = 0; index < 24; index++)
        {
            var saved = await Saved(complete: index != 23);
            saved.Post.Title = "batch source " + index;
            selected.Add(saved.Post.Id);
            if (index == 0) await Stage(saved);
        }
        var outside = await Saved(); outside.Post.Title = "unrelated";
        var deleted = await Saved(); deleted.Post.Title = "batch deleted"; deleted.Post.IsDeleted = true;
        await Db.SaveChangesAsync();
        var originals = (await Db.PostParserTasks.AsNoTracking().ToListAsync())
            .ToDictionary(p => p.Id, p => (p.Revision, p.WorkflowRunId, p.Results, p.ContentSnapshotJson));
        var page = await _services.GetRequiredService<IPostParserTaskService>().Search(new()
            {Keyword = "batch source", PageIndex = 2, PageSize = 5});
        Assert.AreEqual(24, page.TotalCount); Assert.AreEqual(5, page.Items.Count);
        var filtered = await Worker.PrepareFilteredSavedResultsAsync(new() {Keyword = "batch source"}, default);
        Assert.AreEqual(24, filtered.MatchedCount); Assert.IsFalse(filtered.LimitExceeded);
        Assert.AreEqual(1000, filtered.MaxTasks);
        var preview = filtered.Preview!;
        Assert.AreEqual(22, preview.EligibleCount); Assert.AreEqual(1, preview.ExistingCount);
        Assert.AreEqual(1, preview.RejectedCount);
        CollectionAssert.AreEquivalent(selected, preview.Items.Select(i => i.TaskId).ToList());
        Assert.IsTrue(preview.Items.All(i => i.Link == "https://example.test/post"));
        Assert.AreEqual(1, await Db.AcquisitionHandoffs.CountAsync(), "Preview must not enqueue acquisition or retry an existing receipt.");
        Assert.AreEqual(0, await Db.AcquisitionTasks.CountAsync());
        foreach (var post in await Db.PostParserTasks.AsNoTracking().ToListAsync())
            Assert.AreEqual(originals[post.Id], (post.Revision, post.WorkflowRunId, post.Results, post.ContentSnapshotJson));
    }

    [TestMethod]
    public async Task FilteredCatchUpConfirmationUsesFrozenEligibleSourcesAndDoesNotAddLaterMatches()
    {
        var first = await Saved(); var changed = await Saved();
        first.Post.Title = "frozen batch"; changed.Post.Title = "frozen batch";
        await Db.SaveChangesAsync();
        var preview = (await Worker.PrepareFilteredSavedResultsAsync(new()
            {Keyword = "frozen batch", Status = "success"}, default)).Preview!;
        changed.Post.Results += " "; await Db.SaveChangesAsync();
        var later = await Saved(); later.Post.Title = "frozen batch"; await Db.SaveChangesAsync();
        var confirmed = await Worker.ConfirmSavedResultsAsync(new(preview.Items.Select(i => i.Selection!).ToList()), default);
        Assert.AreEqual(2, confirmed.RequestedCount); Assert.AreEqual(1, confirmed.AcceptedCount);
        Assert.AreEqual(1, confirmed.RejectedCount);
        Assert.AreEqual("HANDOFF_SOURCE_CHANGED", confirmed.Items.Single(i => i.TaskId == changed.Post.Id).Code);
        Assert.AreEqual(first.Post.Id.ToString(), (await Db.AcquisitionHandoffs.SingleAsync()).SourceId);
        Assert.AreEqual(0, await Db.AcquisitionTasks.CountAsync());
        var replay = await Worker.ConfirmSavedResultsAsync(new([preview.Items.Single(i => i.TaskId == first.Post.Id).Selection!]), default);
        Assert.AreEqual(1, replay.ExistingCount); Assert.AreEqual(1, await Db.AcquisitionHandoffs.CountAsync());
    }

    [TestMethod]
    public async Task FilteredCatchUpReturnsWholeLimitOrExplicitOverflowAndEmptyFilterHasNoSelection()
    {
        var source = await Saved();
        Db.PostParserTasks.AddRange(Enumerable.Range(1, 1000).Select(i => new PostParserTaskDbModel
        {
            Title = "overflow source " + i, Revision = 1, Results = source.Post.Results,
            ParsingState = "complete", Link = "https://example.test/" + i
        }));
        await Db.SaveChangesAsync();
        var overflow = await Worker.PrepareFilteredSavedResultsAsync(new(), default);
        Assert.AreEqual(1001, overflow.MatchedCount); Assert.IsTrue(overflow.LimitExceeded);
        Assert.AreEqual(1000, overflow.MaxTasks); Assert.IsNull(overflow.Preview, "Do not present the first 1000 as the whole scope.");
        var bounded = await Worker.PrepareFilteredSavedResultsAsync(new() {Keyword = "overflow source"}, default);
        Assert.AreEqual(1000, bounded.MatchedCount); Assert.IsFalse(bounded.LimitExceeded);
        Assert.AreEqual(1000, bounded.Preview!.EligibleCount);
        var empty = await Worker.PrepareFilteredSavedResultsAsync(new() {Keyword = "no such source"}, default);
        Assert.AreEqual(0, empty.MatchedCount); Assert.IsFalse(empty.LimitExceeded);
        Assert.IsNotNull(empty.Preview); Assert.AreEqual(0, empty.Preview.RequestedCount);
        Assert.AreEqual(0, empty.Preview.Items.Count);
        Assert.AreEqual(0, await Db.AcquisitionHandoffs.CountAsync());
        Assert.AreEqual(0, await Db.AcquisitionTasks.CountAsync());
    }
}
