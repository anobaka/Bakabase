using System.Text.Json.Nodes;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Planning;
using Bakabase.Modules.DataSync.Wire;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Modules.DataSync.Tests.Abstractions;

[TestClass]
public class DataSyncKindCodecTests
{
    private sealed record TestContent(string Name);

    private sealed record OtherContent(string Name);

    /// <summary>A codec whose typed members do what each test sets up, so the base class's checks can be seen.</summary>
    private sealed class TestCodec : DataSyncKindCodec<TestContent>
    {
        public Func<JsonObject, CodecReadResult> OnRead { get; set; } =
            c => new CodecReadResult(new TestContent((string)c["name"]!), null, [], []);

        public Func<TestContent, object> OnPublish { get; set; } = c => c;
        public Func<TestContent?, TestContent, TestContent, DataSyncMerge3Input, object> OnMerge3 { get; set; } =
            (_, local, _, _) => local;
        public (TestContent? Base, TestContent Local, TestContent Remote)? LastMerge3 { get; private set; }
        public (TestContent? Base, TestContent Local, TestContent Remote)? LastCandidates { get; private set; }

        public override DataSyncKindDescriptor Descriptor { get; } = new("testKind", 2, [], typeof(TestContent), false,
            false, true, false, "child");

        public override int ComparisonFormVersion => 3;

        protected override CodecReadResult ReadCore(JsonObject content, DataSyncLimits limits) => OnRead(content);
        public override TestContent ReadLocal(JsonObject content) => new((string)content["name"]!);
        public override JsonObject Write(TestContent content) => new() { ["name"] = content.Name };
        public override string NameOf(TestContent content) => content.Name;
        public override int ChildCountOf(TestContent content) => 0;
        public override DataSyncNaturalMatch MatchNatural(TestContent incoming, TestContent local) => DataSyncNaturalMatch.None;
        public override EntityDiff Diff(TestContent local, TestContent incoming) => new([], [], 0, 0);

        public override MergeResult Merge(TestContent local, TestContent incoming, IReadOnlySet<string> acceptedChangeIds) =>
            new(local, new Dictionary<string, string>(), [], []);

        public override MergeResult PrepareCreate(TestContent incoming, string? nameOverride) =>
            new(new OtherContent("wrong"), new Dictionary<string, string>(), [], []);

        public override DataSyncPublishable Publish(TestContent localContent, DataSyncOverlay overlay, bool childrenLocal) =>
            new(OnPublish(localContent), 0, []);

        public override JsonObject ComparisonForm(TestContent publishedContent, string? orderKey, bool childrenLocal) =>
            new() { ["name"] = publishedContent.Name };

        protected override IReadOnlyList<string> ChildDeletionCandidates(TestContent? baseContent, TestContent local,
            TestContent remote, DataSyncChildCandidatesInput input)
        {
            LastCandidates = (baseContent, local, remote);
            return ["c1"];
        }

        protected override DataSyncMerge3Result Merge3(TestContent? baseContent, TestContent local, TestContent remote,
            DataSyncMerge3Input input)
        {
            LastMerge3 = (baseContent, local, remote);
            return new DataSyncMerge3Result(OnMerge3(baseContent, local, remote, input), [],
                new Dictionary<string, string>(), [], [], [], [], [], [], false);
        }

        public override IReadOnlyList<DataSyncChildInfo> ChildrenOf(TestContent content) =>
            [new DataSyncChildInfo("c1", null, new DataSyncDisplayValue(content.Name))];
    }

    private static readonly JsonObject Content = new() { ["name"] = "Genre" };

    // ---- Read ------------------------------------------------------------------------------

    [TestMethod]
    public void ReadReturnsTheCoreResult()
    {
        var codec = new TestCodec();
        var result = codec.Read(Content, DataSyncLimits.Default);
        Assert.AreEqual(new TestContent("Genre"), result.Content);
        Assert.IsNull(result.Held);
        Assert.IsNull(result.Unknown);
    }

    [TestMethod]
    public void CodecReadResultCarriesUnknownMembersWhenGiven()
    {
        var unknown = new JsonObject { ["x-future"] = 1 };
        var codec = new TestCodec { OnRead = _ => new CodecReadResult(new TestContent("a"), null, [], [], unknown) };
        Assert.AreSame(unknown, codec.Read(Content, DataSyncLimits.Default).Unknown);
    }

    [TestMethod]
    public void ReadTurnsOddPeerInputExceptionsIntoHeldInvalid()
    {
        var exceptions = new Exception[]
        {
            new System.Text.Json.JsonException("json"), new FormatException("format"),
            new InvalidOperationException("op"), new ArgumentException("arg"), new InvalidCastException("cast"),
            new OverflowException("overflow"), new KeyNotFoundException("key"), new NullReferenceException("null"),
            new IndexOutOfRangeException("index"),
        };
        foreach (var exception in exceptions)
        {
            var codec = new TestCodec { OnRead = _ => throw exception };
            var result = codec.Read(Content, DataSyncLimits.Default);
            Assert.IsNull(result.Content, exception.GetType().Name);
            Assert.AreEqual(DataSyncHeldReason.Invalid, result.Held, exception.GetType().Name);
            CollectionAssert.AreEqual(new[] { exception.Message }, result.Errors.ToArray());
        }
    }

    [TestMethod]
    public void ReadLetsOtherExceptionsThrough()
    {
        var codec = new TestCodec { OnRead = _ => throw new NotSupportedException("bug") };
        Assert.ThrowsException<NotSupportedException>(() => codec.Read(Content, DataSyncLimits.Default));
    }

    [TestMethod]
    public void ReadRejectsAResultThatBreaksTheContract()
    {
        var wrongType = new TestCodec { OnRead = _ => new CodecReadResult(new OtherContent("x"), null, [], []) };
        Assert.ThrowsException<InvalidOperationException>(() => wrongType.Read(Content, DataSyncLimits.Default));

        var both = new TestCodec
            { OnRead = _ => new CodecReadResult(new TestContent("x"), DataSyncHeldReason.Invalid, [], []) };
        Assert.ThrowsException<InvalidOperationException>(() => both.Read(Content, DataSyncLimits.Default));

        var neither = new TestCodec { OnRead = _ => new CodecReadResult(null, null, [], []) };
        Assert.ThrowsException<InvalidOperationException>(() => neither.Read(Content, DataSyncLimits.Default));
    }

    // ---- Upgrade ---------------------------------------------------------------------------

    [TestMethod]
    public void UpgradeIsTheIdentityAtTheCurrentVersionAndHoldsOtherwise()
    {
        var codec = new TestCodec();
        Assert.AreSame(Content, codec.Upgrade(Content, 2));
        Assert.AreEqual(DataSyncHeldReason.NewerSchema,
            Assert.ThrowsException<DataSyncHeldException>(() => codec.Upgrade(Content, 3)).Reason);
        Assert.AreEqual(DataSyncHeldReason.Invalid,
            Assert.ThrowsException<DataSyncHeldException>(() => codec.Upgrade(Content, 1)).Reason);
    }

    // ---- casting ---------------------------------------------------------------------------

    [TestMethod]
    public void AWrongContentTypeNamesTheExpectedType()
    {
        IDataSyncKindCodec codec = new TestCodec();
        var other = new OtherContent("x");
        var e = Assert.ThrowsException<ArgumentException>(() => codec.NameOf(other));
        StringAssert.Contains(e.Message, nameof(TestContent));
        Assert.ThrowsException<ArgumentException>(() => codec.Write(other));
        Assert.ThrowsException<ArgumentException>(() => codec.Diff(new TestContent("a"), other));
        Assert.ThrowsException<ArgumentException>(() => codec.Publish(other, DataSyncOverlay.None, false));
        Assert.ThrowsException<ArgumentException>(() => codec.ComparisonForm(other, null, false));
        Assert.ThrowsException<ArgumentException>(() => codec.ChildrenOf(other));
    }

    [TestMethod]
    public void NonGenericMembersDelegate()
    {
        IDataSyncKindCodec codec = new TestCodec();
        var content = new TestContent("Genre");
        Assert.AreEqual(3, codec.ComparisonFormVersion);
        Assert.AreEqual("Genre", codec.NameOf(content));
        Assert.IsNull(codec.SubtypeOf(content));
        Assert.AreEqual(new TestContent("Genre"), codec.ReadLocal(Content));
        Assert.AreEqual("{\"name\":\"Genre\"}", codec.Write(content).ToJsonString());
        Assert.AreSame(content, codec.Publish(content, DataSyncOverlay.None, false).Content);
        Assert.AreEqual("Genre", (string)codec.ComparisonForm(content, "a0", false)["name"]!);
        Assert.AreEqual("c1", codec.ChildrenOf(content).Single().Id);
    }

    [TestMethod]
    public void MergeResultsMustHaveTheCodecsType()
    {
        IDataSyncKindCodec codec = new TestCodec();
        Assert.ThrowsException<InvalidOperationException>(() => codec.PrepareCreate(new TestContent("a"), null));
        Assert.AreEqual(new TestContent("a"),
            codec.Merge(new TestContent("a"), new TestContent("b"), new HashSet<string>()).Content);
    }

    [TestMethod]
    public void PublishMustReturnTheCodecsType()
    {
        IDataSyncKindCodec codec = new TestCodec { OnPublish = _ => new OtherContent("x") };
        Assert.ThrowsException<InvalidOperationException>(() =>
            codec.Publish(new TestContent("a"), DataSyncOverlay.None, false));
    }

    // ---- Merge3 and ChildDeletionCandidates ---------------------------------------------------

    private static DataSyncMerge3Input Merge3Input(object? baseContent, object local, object remote,
        DataSyncMerge3Mode mode3) =>
        new(baseContent, local, DataSyncOverlay.None, remote, mode3, new Dictionary<string, string>(), false, false,
            DataSyncLinkMode.TwoWay, true, DataSyncMergeSide.Local, new Dictionary<string, int>(),
            DataSyncChildDeletionMode.Normal);

    [TestMethod]
    public void Merge3CastsItsContents()
    {
        var codec = new TestCodec();
        var (b, l, r) = (new TestContent("b"), new TestContent("l"), new TestContent("r"));
        var result = codec.Merge3(Merge3Input(b, l, r, DataSyncMerge3Mode.ThreeWay));
        Assert.AreSame(l, result.Merged);
        Assert.AreEqual((b, l, r), codec.LastMerge3);

        codec.Merge3(Merge3Input(null, l, r, DataSyncMerge3Mode.NoBase));
        Assert.AreEqual(((TestContent?)null, l, r), codec.LastMerge3);
        codec.Merge3(Merge3Input(null, l, r, DataSyncMerge3Mode.FastForward));
        Assert.AreEqual(((TestContent?)null, l, r), codec.LastMerge3);
    }

    [TestMethod]
    public void Merge3RefusesWrongInputs()
    {
        var codec = new TestCodec();
        var content = new TestContent("a");
        var other = new OtherContent("x");
        Assert.ThrowsException<ArgumentException>(() => codec.Merge3(Merge3Input(content, other, content, DataSyncMerge3Mode.ThreeWay)));
        Assert.ThrowsException<ArgumentException>(() => codec.Merge3(Merge3Input(content, content, other, DataSyncMerge3Mode.ThreeWay)));
        Assert.ThrowsException<ArgumentException>(() => codec.Merge3(Merge3Input(other, content, content, DataSyncMerge3Mode.ThreeWay)));
        Assert.ThrowsException<ArgumentException>(() => codec.Merge3(Merge3Input(null, content, content, DataSyncMerge3Mode.ThreeWay)));
        Assert.ThrowsException<ArgumentException>(() => codec.Merge3(Merge3Input(null, content, content, DataSyncMerge3Mode.Convert)));
        Assert.ThrowsException<ArgumentNullException>(() => codec.Merge3(null!));
    }

    [TestMethod]
    public void Merge3MustReturnTheCodecsType()
    {
        var codec = new TestCodec { OnMerge3 = (_, _, _, _) => new OtherContent("x") };
        var content = new TestContent("a");
        Assert.ThrowsException<InvalidOperationException>(() =>
            codec.Merge3(Merge3Input(content, content, content, DataSyncMerge3Mode.ThreeWay)));
    }

    [TestMethod]
    public void ChildDeletionCandidatesCastsAndChecksLikeMerge3()
    {
        var codec = new TestCodec();
        var (b, l, r) = (new TestContent("b"), new TestContent("l"), new TestContent("r"));
        DataSyncChildCandidatesInput Input(object? baseContent, object local, DataSyncMerge3Mode mode3) =>
            new(baseContent, local, DataSyncOverlay.None, r, mode3, new Dictionary<string, string>(), false);

        CollectionAssert.AreEqual(new[] { "c1" }, codec.ChildDeletionCandidates(Input(b, l, DataSyncMerge3Mode.ThreeWay)).ToArray());
        Assert.AreEqual((b, l, r), codec.LastCandidates);
        Assert.ThrowsException<ArgumentException>(() =>
            codec.ChildDeletionCandidates(Input(b, new OtherContent("x"), DataSyncMerge3Mode.ThreeWay)));
        Assert.ThrowsException<ArgumentException>(() =>
            codec.ChildDeletionCandidates(Input(null, l, DataSyncMerge3Mode.ThreeWay)));
    }

    // ---- overlays --------------------------------------------------------------------------

    [TestMethod]
    public void HiddenChildIdsUnitesLocalOnlyAndHeldChildren()
    {
        var overlay = new DataSyncOverlay(["a", "b"], [new DataSyncHeldChild("b", 1), new DataSyncHeldChild("c", 2),
            new DataSyncHeldChild("c", 3)]);
        CollectionAssert.AreEqual(new[] { "a", "b", "c" }, overlay.HiddenChildIds.ToArray());
        Assert.AreEqual(0, DataSyncOverlay.None.HiddenChildIds.Count());
    }
}
