using System.Text.Json;
using Bakabase.Modules.Acquisition.Abstractions.Components;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Components;
using Bakabase.Modules.Acquisition.Components.Steps;
using Bakabase.Modules.Acquisition.Models.Domain;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Modules.Acquisition.Tests;

/// <summary>
/// Which of a post's three download links gets used, and what happens when the answer is not
/// obvious. The rule is a preference list rather than a policy: which drive is best depends on
/// where the user has an account and what is fast where they live.
/// </summary>
[TestClass]
public sealed class LinkSelectionTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed class FixedOptions<T>(T value) : IBOptions<T> where T : class
    {
        public T Value { get; } = value;
    }

    private static AcquisitionStepContext Context() => new(
        new ServiceCollection().BuildServiceProvider(), NullLogger.Instance,
        (_, _) => Task.CompletedTask, "/tmp/acquisition/1");

    private static AcquisitionStepContext Context(string configJson) => new(
        new ServiceCollection().BuildServiceProvider(), NullLogger.Instance,
        (_, _) => Task.CompletedTask, "/tmp/acquisition/1", configJson);

    private static AcquisitionWorkItem ItemWith(params AcquisitionLink[] links) => new()
    {
        ResourceId = 1,
        LeadKind = AcquisitionLeadKind.SharedPage,
        LeadValue = "https://example.com/thread/1",
        Links = links,
    };

    private static SelectLinkStep StepPreferring(params AcquisitionDriveKind[] preferred) =>
        new(new FixedOptions<AcquisitionOptions>(new AcquisitionOptions
        {
            PreferredDriveKinds = preferred.ToList()
        }));

    [TestMethod]
    public async Task ThePreferenceListDecides_AndOrderWithinThePostBreaksTies()
    {
        var step = StepPreferring(AcquisitionDriveKind.PikPak, AcquisitionDriveKind.Baidu);
        var item = ItemWith(
            new AcquisitionLink("https://pan.baidu.com/s/a", DriveKind: AcquisitionDriveKind.Baidu),
            new AcquisitionLink("https://mega.nz/b", DriveKind: AcquisitionDriveKind.Mega),
            new AcquisitionLink("https://mypikpak.com/c", DriveKind: AcquisitionDriveKind.PikPak));

        var outcome = await step.ExecuteAsync(Context(), item, CancellationToken.None);

        var kept = ((AcquisitionStepOutcome.Continue) outcome).Item;

        Assert.AreEqual(2, kept.SelectedLinkIndex, "PikPak is first in the preferences");
        Assert.AreEqual("https://mypikpak.com/c", kept.SelectedLink!.Url);
    }

    [TestMethod]
    public async Task WithNoUsablePreference_TheFirstLinkWins()
    {
        // Authors tend to put the one they recommend first, so position is not nothing.
        var step = StepPreferring(AcquisitionDriveKind.GoogleDrive);
        var item = ItemWith(
            new AcquisitionLink("https://mega.nz/a", DriveKind: AcquisitionDriveKind.Mega),
            new AcquisitionLink("https://pan.baidu.com/b", DriveKind: AcquisitionDriveKind.Baidu));

        var outcome = await step.ExecuteAsync(Context(), item, CancellationToken.None);

        Assert.AreEqual(0, ((AcquisitionStepOutcome.Continue) outcome).Item.SelectedLinkIndex);
    }

    /// <summary>
    /// A post with nothing downloadable in it is not a dead end: the user may have found the file
    /// elsewhere, and the answer can carry the link they found.
    /// </summary>
    [TestMethod]
    public async Task NoLinks_Suspends_AndTheAnswerMayCarryOne()
    {
        var step = StepPreferring();
        var item = ItemWith();

        var outcome = await step.ExecuteAsync(Context(), item, CancellationToken.None);
        var suspended = (AcquisitionStepOutcome.Suspend) outcome;

        Assert.AreEqual(AcquisitionWaitReason.NoLinks, suspended.Reason);

        var supplied = new SelectLinkStep.Signal(null,
            [new AcquisitionLink("https://mega.nz/found-it", DriveKind: AcquisitionDriveKind.Mega)]);
        var resumed = await step.ResumeAsync(Context(), item,
            new AcquisitionResumeSignal(AcquisitionWaitReason.NoLinks,
                JsonSerializer.Serialize(supplied, Json)), CancellationToken.None);

        var kept = ((AcquisitionStepOutcome.Continue) resumed).Item;

        Assert.AreEqual("https://mega.nz/found-it", kept.SelectedLink!.Url);
    }

    [TestMethod]
    public async Task AlwaysAsk_Suspends_AndThePromptListsWhatThePostOffered()
    {
        var step = StepPreferring(AcquisitionDriveKind.Baidu);
        var item = ItemWith(
            new AcquisitionLink("https://pan.baidu.com/s/a", "1234", DriveKind: AcquisitionDriveKind.Baidu),
            new AcquisitionLink("https://mega.nz/b", DriveKind: AcquisitionDriveKind.Mega));

        var outcome = await step.ExecuteAsync(Context("""{"alwaysAsk":true}"""), item, CancellationToken.None);
        var suspended = (AcquisitionStepOutcome.Suspend) outcome;

        Assert.AreEqual(AcquisitionWaitReason.ChooseLink, suspended.Reason);

        var prompt = JsonSerializer.Deserialize<SelectLinkStep.Prompt>(suspended.PromptJson!, Json)!;

        Assert.AreEqual(2, prompt.Links.Count);
        Assert.AreEqual("1234", prompt.Links[0].AccessCode, "the code the user will need is in the prompt");
        Assert.AreEqual(AcquisitionDriveKind.Mega, prompt.Links[1].DriveKind);

        var resumed = await step.ResumeAsync(Context("""{"alwaysAsk":true}"""), item,
            new AcquisitionResumeSignal(AcquisitionWaitReason.ChooseLink,
                JsonSerializer.Serialize(new SelectLinkStep.Signal(1, null), Json)), CancellationToken.None);

        Assert.AreEqual(1, ((AcquisitionStepOutcome.Continue) resumed).Item.SelectedLinkIndex);
    }

    [TestMethod]
    public async Task AnUnreadableAnswerFailsTheStepRatherThanPickingSomethingAtRandom()
    {
        var step = StepPreferring();
        var outcome = await step.ResumeAsync(Context(), ItemWith(
                new AcquisitionLink("https://mega.nz/a", DriveKind: AcquisitionDriveKind.Mega)),
            new AcquisitionResumeSignal(AcquisitionWaitReason.ChooseLink, "{ not json"),
            CancellationToken.None);

        Assert.IsInstanceOfType<AcquisitionStepOutcome.Fail>(outcome);
    }

    [TestMethod]
    public void DriveKindsAreInferredFromTheHost()
    {
        var cases = new Dictionary<string, AcquisitionDriveKind>
        {
            ["https://pan.baidu.com/s/1abc"] = AcquisitionDriveKind.Baidu,
            ["https://yun.baidu.com/s/1abc"] = AcquisitionDriveKind.Baidu,
            ["https://pan.xunlei.com/s/x"] = AcquisitionDriveKind.Xunlei,
            ["https://www.feimaoyun.com/s/x"] = AcquisitionDriveKind.Feimao,
            ["https://pub-1234.r2.dev/file.zip"] = AcquisitionDriveKind.Cloudflare,
            ["https://mega.nz/folder/abc"] = AcquisitionDriveKind.Mega,
            ["https://mega.co.nz/folder/abc"] = AcquisitionDriveKind.Mega,
            ["https://mypikpak.com/s/x"] = AcquisitionDriveKind.PikPak,
            ["https://drive.google.com/file/d/x"] = AcquisitionDriveKind.GoogleDrive,
            ["https://1drv.ms/u/s!abc"] = AcquisitionDriveKind.OneDrive,
            ["https://115.com/s/x"] = AcquisitionDriveKind.OneOneFive,
            ["https://anxia.com/s/x"] = AcquisitionDriveKind.OneOneFive,
            ["magnet:?xt=urn:btih:abc"] = AcquisitionDriveKind.Magnet,
            ["MAGNET:?xt=urn:btih:abc"] = AcquisitionDriveKind.Magnet,
            ["https://files.example.com/a.zip"] = AcquisitionDriveKind.DirectUrl,
            ["not a url at all"] = AcquisitionDriveKind.Unknown,
            [""] = AcquisitionDriveKind.Unknown,
        };

        foreach (var (url, expected) in cases)
        {
            Assert.AreEqual(expected, AcquisitionDriveKinds.Infer(url), url);
        }
    }

    /// <summary>
    /// Only a link that is the file itself can be fetched without a person. Everything behind a
    /// login or a captcha goes through the inbox, which is why the distinction is worth having.
    /// </summary>
    [TestMethod]
    public void OnlyDirectLinksCanBeFetchedWithoutAPerson()
    {
        Assert.IsTrue(AcquisitionDriveKind.DirectUrl.CanBeFetchedDirectly());
        Assert.IsTrue(AcquisitionDriveKind.Cloudflare.CanBeFetchedDirectly());
        Assert.IsFalse(AcquisitionDriveKind.Baidu.CanBeFetchedDirectly());
        Assert.IsFalse(AcquisitionDriveKind.Mega.CanBeFetchedDirectly());
        Assert.IsFalse(AcquisitionDriveKind.Unknown.CanBeFetchedDirectly());
    }
}
