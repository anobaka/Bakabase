using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Property.Components;

namespace Bakabase.Modules.Property.Tests;

/// <summary>
/// Boundary coverage for MediaLibraryV2Adapter — the SingleChoice &lt;-&gt;
/// MultipleChoice facade used during the MediaLibraryV2 -> MediaLibraryV2Multi
/// migration: wrapping/unwrapping values, normalizing mixed storage formats,
/// and the legacy-property detection / redirection.
/// </summary>
[TestClass]
public sealed class MediaLibraryV2AdapterTests
{
    #region Write: Single -> Multi

    [TestMethod]
    public void ToMultiDbValue_NonEmpty_WrapsInSingletonList()
        => CollectionAssert.AreEqual(new List<string> { "v-1" }, MediaLibraryV2Adapter.ToMultiDbValue("v-1"));

    [TestMethod]
    public void ToMultiDbValue_NullOrEmpty_ReturnsNull()
    {
        Assert.IsNull(MediaLibraryV2Adapter.ToMultiDbValue(null));
        Assert.IsNull(MediaLibraryV2Adapter.ToMultiDbValue(""));
    }

    #endregion

    #region Read: Multi -> Single

    [TestMethod]
    public void ToSingleDbValue_List_ReturnsFirstValue()
        => Assert.AreEqual("a", MediaLibraryV2Adapter.ToSingleDbValue(new List<string> { "a", "b" }));

    [TestMethod]
    public void ToSingleDbValue_EmptyOrNullList_ReturnsNull()
    {
        Assert.IsNull(MediaLibraryV2Adapter.ToSingleDbValue(new List<string>()));
        Assert.IsNull(MediaLibraryV2Adapter.ToSingleDbValue((List<string>?)null));
    }

    [TestMethod]
    public void ToSingleDbValue_Object_HandlesStringListAndOtherTypes()
    {
        Assert.AreEqual("legacy", MediaLibraryV2Adapter.ToSingleDbValue((object)"legacy"));
        Assert.AreEqual("a", MediaLibraryV2Adapter.ToSingleDbValue((object)new List<string> { "a", "b" }));
        Assert.IsNull(MediaLibraryV2Adapter.ToSingleDbValue((object?)null));
        Assert.IsNull(MediaLibraryV2Adapter.ToSingleDbValue((object)123));
    }

    #endregion

    #region Unified read (handles both formats)

    [TestMethod]
    public void ReadAsMulti_LegacyString_WrapsInList()
        => CollectionAssert.AreEqual(new List<string> { "x" }, MediaLibraryV2Adapter.ReadAsMulti("x"));

    [TestMethod]
    public void ReadAsMulti_List_ReturnsList()
        => CollectionAssert.AreEqual(
            new List<string> { "a", "b" }, MediaLibraryV2Adapter.ReadAsMulti(new List<string> { "a", "b" }));

    [TestMethod]
    public void ReadAsMulti_EmptyNullOrOtherType_ReturnsNull()
    {
        Assert.IsNull(MediaLibraryV2Adapter.ReadAsMulti(""));
        Assert.IsNull(MediaLibraryV2Adapter.ReadAsMulti(new List<string>()));
        Assert.IsNull(MediaLibraryV2Adapter.ReadAsMulti(null));
        Assert.IsNull(MediaLibraryV2Adapter.ReadAsMulti(123));
    }

    [TestMethod]
    public void ReadAsSingle_HandlesStringListAndNull()
    {
        Assert.AreEqual("x", MediaLibraryV2Adapter.ReadAsSingle("x"));
        Assert.AreEqual("a", MediaLibraryV2Adapter.ReadAsSingle(new List<string> { "a", "b" }));
        Assert.IsNull(MediaLibraryV2Adapter.ReadAsSingle(new List<string>()));
        Assert.IsNull(MediaLibraryV2Adapter.ReadAsSingle(null));
    }

    #endregion

    #region Detection & redirection

    [TestMethod]
    public void IsLegacyProperty_OnlyForInternalPoolAndLegacyId()
    {
        Assert.IsTrue(MediaLibraryV2Adapter.IsLegacyProperty(
            PropertyPool.Internal, MediaLibraryV2Adapter.LegacyPropertyId));
        Assert.IsFalse(MediaLibraryV2Adapter.IsLegacyProperty(
            PropertyPool.Custom, MediaLibraryV2Adapter.LegacyPropertyId));
        Assert.IsFalse(MediaLibraryV2Adapter.IsLegacyProperty(PropertyPool.Internal, -9999));
    }

    [TestMethod]
    public void IsCurrentProperty_OnlyForInternalPoolAndCurrentId()
    {
        Assert.IsTrue(MediaLibraryV2Adapter.IsCurrentProperty(
            PropertyPool.Internal, MediaLibraryV2Adapter.CurrentPropertyId));
        Assert.IsFalse(MediaLibraryV2Adapter.IsCurrentProperty(
            PropertyPool.Custom, MediaLibraryV2Adapter.CurrentPropertyId));
    }

    [TestMethod]
    public void IsMediaLibraryProperty_TrueForBothGenerations()
    {
        Assert.IsTrue(MediaLibraryV2Adapter.IsMediaLibraryProperty(
            PropertyPool.Internal, MediaLibraryV2Adapter.LegacyPropertyId));
        Assert.IsTrue(MediaLibraryV2Adapter.IsMediaLibraryProperty(
            PropertyPool.Internal, MediaLibraryV2Adapter.CurrentPropertyId));
        Assert.IsFalse(MediaLibraryV2Adapter.IsMediaLibraryProperty(PropertyPool.Custom, 1));
    }

    [TestMethod]
    public void GetActualPropertyId_LegacyRedirectsToCurrent_OthersUnchanged()
    {
        Assert.AreEqual(MediaLibraryV2Adapter.CurrentPropertyId, MediaLibraryV2Adapter.GetActualPropertyId(
            PropertyPool.Internal, MediaLibraryV2Adapter.LegacyPropertyId));
        Assert.AreEqual(42, MediaLibraryV2Adapter.GetActualPropertyId(PropertyPool.Custom, 42));
    }

    #endregion
}
