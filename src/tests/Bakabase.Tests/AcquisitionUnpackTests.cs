using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.Modules.Acquisition.Abstractions.Components;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Models.Domain;
using Bakabase.Service.Components.Acquisition.Steps;
using Bakabase.TestKit.Utils;
using Bootstrap.Components.Configuration.Abstractions;
using CliWrap;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests;

/// <summary>
/// Opening what was downloaded. The password is the part that goes wrong — it might be in the post,
/// in the file name, or nowhere — so what these cover is the order the candidates are tried in, and
/// what happens when none of them fit.
/// </summary>
[TestClass]
public sealed class AcquisitionUnpackTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private IServiceProvider _sp = null!;
    private string _root = null!;
    private string _working = null!;
    private string? _sevenZip;

    [TestInitialize]
    public async Task Setup()
    {
        _sevenZip = Find7z();
        _root = Path.Combine(Path.GetTempPath(), $"BakabaseUnpack_{Guid.NewGuid():N}");
        _working = Path.Combine(_root, "working");
        Directory.CreateDirectory(_working);

        _sp = await TestServiceBuilder.BuildServiceProvider(services =>
        {
            if (_sevenZip == null) return;

            // The real one looks the executable up through the dependency manager, which is not
            // running here.
            services.RemoveAll<CompressedFileService>();
            services.AddSingleton<CompressedFileService>(new TestableCompressedFileService(_sevenZip));
            services.RemoveAll<IArchiveExtractionService>();
            services.AddSingleton<IArchiveExtractionService>(sp =>
                new ArchiveExtractionService(sp.GetRequiredService<CompressedFileService>()));
        });
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_root, true); }
        catch { /* best effort */ }
    }

    private void Require7z()
    {
        if (_sevenZip == null) Assert.Inconclusive("7z executable not found. Skipping integration test.");
    }

    private static string? Find7z()
    {
        foreach (var path in new[]
                 {
                     "7z", "7za", "/usr/bin/7z", "/usr/local/bin/7z", "/opt/homebrew/bin/7z",
                     @"C:\Program Files\7-Zip\7z.exe", @"C:\Program Files (x86)\7-Zip\7z.exe"
                 })
        {
            try
            {
                var result = Cli.Wrap(path).WithArguments("--help")
                    .WithValidation(CommandResultValidation.None).ExecuteAsync().GetAwaiter().GetResult();

                if (result.ExitCode == 0) return path;
            }
            catch
            {
                // Not this one.
            }
        }

        return null;
    }

    private AcquisitionStepContext Context(string? configJson = null) => new(
        _sp, NullLogger.Instance, (_, _) => Task.CompletedTask, _working, configJson);

    private static AcquisitionWorkItem Item(string? archivePassword = null) => new()
    {
        ResourceId = 1,
        LeadKind = AcquisitionLeadKind.SharedPage,
        LeadValue = "https://example.com/thread/1",
        Links = archivePassword == null
            ? []
            : [new AcquisitionLink("https://example.com/file", ArchivePassword: archivePassword)],
        SelectedLinkIndex = archivePassword == null ? null : 0,
    };

    /// <summary>Builds an archive out of one text file, optionally with a password.</summary>
    private async Task<string> MakeArchive(string archiveName, string? password = null,
        string contentName = "content.txt")
    {
        var source = Path.Combine(_root, $"src_{Guid.NewGuid():N}");

        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, contentName), "the payload");

        var archivePath = Path.Combine(_working, archiveName);
        var args = new List<string> {"a", archivePath, Path.Combine(source, "*")};

        if (password != null) args.Add($"-p{password}");

        var result = await Cli.Wrap(_sevenZip!).WithArguments(args)
            .WithValidation(CommandResultValidation.None).ExecuteAsync();

        Assert.AreEqual(0, result.ExitCode, "7z could not build the fixture archive");

        return archivePath;
    }

    [TestMethod]
    public async Task AnArchiveWithNoPasswordJustOpens()
    {
        Require7z();
        await MakeArchive("plain.7z");

        var outcome = await new UnpackStep().ExecuteAsync(Context(), Item(), CancellationToken.None);

        Assert.IsInstanceOfType<AcquisitionStepOutcome.Continue>(outcome, DescribeOutcome(outcome));
        Assert.IsTrue(File.Exists(Path.Combine(_working, "content.txt")),
            "and its contents are lifted up, not left in a folder named after the archive");
        Assert.IsFalse(File.Exists(Path.Combine(_working, "plain.7z")), "the archive is cleared away");
    }

    /// <summary>
    /// The post said the password. That is the first thing to try after "no password at all", and
    /// it is the case the whole pipeline is built around.
    /// </summary>
    [TestMethod]
    public async Task ThePasswordFromTheSharedContentIsTried()
    {
        Require7z();
        await MakeArchive("locked.7z", "hunter2");

        var outcome = await new UnpackStep().ExecuteAsync(Context(), Item("hunter2"), CancellationToken.None);

        Assert.IsInstanceOfType<AcquisitionStepOutcome.Continue>(outcome, DescribeOutcome(outcome));
        Assert.IsTrue(File.Exists(Path.Combine(_working, "content.txt")));
    }

    /// <summary>Some sites put the password in square brackets in the file name.</summary>
    [TestMethod]
    public async Task ThePasswordInTheFileNameIsTried()
    {
        Require7z();
        await MakeArchive("A Work [swordfish].7z", "swordfish");

        var outcome = await new UnpackStep().ExecuteAsync(Context(), Item(), CancellationToken.None);

        Assert.IsInstanceOfType<AcquisitionStepOutcome.Continue>(outcome, DescribeOutcome(outcome));
    }

    /// <summary>
    /// Nothing known fits. Asking is far better than a failed run somebody has to go and read a log
    /// about — and answering it carries straight on.
    /// </summary>
    [TestMethod]
    public async Task AnUnknownPasswordSuspends_AndTheAnswerFinishesTheJob()
    {
        Require7z();
        await MakeArchive("secret.7z", "correct-horse");

        var step = new UnpackStep();
        var item = Item("wrong-password");

        var outcome = await step.ExecuteAsync(Context(), item, CancellationToken.None);
        var suspended = outcome as AcquisitionStepOutcome.Suspend;

        Assert.IsNotNull(suspended, DescribeOutcome(outcome));
        Assert.AreEqual(AcquisitionWaitReason.PasswordUnknown, suspended!.Reason);

        var prompt = JsonSerializer.Deserialize<UnpackStep.Prompt>(suspended.PromptJson!, Json)!;

        Assert.AreEqual("secret.7z", prompt.ArchiveName);
        CollectionAssert.Contains(prompt.Tried.ToArray(), "wrong-password",
            "the user is shown what has already been ruled out");

        var resumed = await step.ResumeAsync(Context(), item,
            new AcquisitionResumeSignal(AcquisitionWaitReason.PasswordUnknown,
                JsonSerializer.Serialize(new UnpackStep.PasswordSignal("correct-horse"), Json)),
            CancellationToken.None);

        Assert.IsInstanceOfType<AcquisitionStepOutcome.Continue>(resumed, DescribeOutcome(resumed));
        Assert.IsTrue(File.Exists(Path.Combine(_working, "content.txt")));
    }

    /// <summary>A zip inside a rar is common enough to be worth unwrapping without being asked.</summary>
    [TestMethod]
    public async Task ANestedArchiveIsUnwrappedTwice()
    {
        Require7z();

        var inner = Path.Combine(_root, "inner");

        Directory.CreateDirectory(inner);
        await File.WriteAllTextAsync(Path.Combine(inner, "deep.txt"), "buried");
        var innerArchive = Path.Combine(_root, "inner.zip");

        await Cli.Wrap(_sevenZip!).WithArguments(["a", innerArchive, Path.Combine(inner, "*")])
            .WithValidation(CommandResultValidation.None).ExecuteAsync();
        await Cli.Wrap(_sevenZip!)
            .WithArguments(["a", Path.Combine(_working, "outer.7z"), innerArchive])
            .WithValidation(CommandResultValidation.None).ExecuteAsync();

        var outcome = await new UnpackStep().ExecuteAsync(Context(), Item(), CancellationToken.None);

        Assert.IsInstanceOfType<AcquisitionStepOutcome.Continue>(outcome, DescribeOutcome(outcome));
        Assert.IsTrue(File.Exists(Path.Combine(_working, "deep.txt")),
            "both layers came off");
    }

    [TestMethod]
    public async Task AFolderOfPlainFilesIsSkipped()
    {
        await File.WriteAllTextAsync(Path.Combine(_working, "already-a-file.txt"), "nothing to open");

        var outcome = await new UnpackStep().ExecuteAsync(Context(), Item(), CancellationToken.None);

        // Skip rather than Fail: plenty of downloads are just a folder of files, and the recipe
        // should carry straight on to placing them.
        Assert.IsInstanceOfType<AcquisitionStepOutcome.Skip>(outcome, DescribeOutcome(outcome));
    }

    [TestMethod]
    public async Task KeepingTheArchiveIsAnOption()
    {
        Require7z();
        await MakeArchive("keepme.7z");

        var outcome = await new UnpackStep().ExecuteAsync(Context("""{"deleteArchive":false}"""),
            Item(), CancellationToken.None);

        Assert.IsInstanceOfType<AcquisitionStepOutcome.Continue>(outcome, DescribeOutcome(outcome));
        Assert.IsTrue(File.Exists(Path.Combine(_working, "keepme.7z")));
    }

    /// <summary>
    /// The recent-password list is a convenience, not something to run through someone's whole
    /// password history without them asking.
    /// </summary>
    [TestMethod]
    public async Task RecentPasswordsAreOnlyTriedWhenAskedFor()
    {
        Require7z();
        _sp.GetRequiredService<IBOptions<AcquisitionOptions>>().Value.TryRecentPasswords = false;
        await MakeArchive("secret.7z", "never-guessed");

        var outcome = await new UnpackStep().ExecuteAsync(Context(), Item(), CancellationToken.None);
        var suspended = outcome as AcquisitionStepOutcome.Suspend;

        Assert.IsNotNull(suspended, DescribeOutcome(outcome));

        var prompt = JsonSerializer.Deserialize<UnpackStep.Prompt>(suspended!.PromptJson!, Json)!;

        Assert.AreEqual(0, prompt.Tried.Count,
            "with nothing in the content and nothing in the name, there was nothing to try");
    }

    private static string DescribeOutcome(AcquisitionStepOutcome outcome) => outcome switch
    {
        AcquisitionStepOutcome.Fail f => $"failed: {f.Message}",
        AcquisitionStepOutcome.Skip s => $"skipped: {s.Why}",
        AcquisitionStepOutcome.Suspend sp => $"suspended: {sp.Reason}",
        _ => outcome.ToString() ?? "",
    };
}
