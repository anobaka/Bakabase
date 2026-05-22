using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.StandardValue.Tests;

/// <summary>
/// Boundary coverage for IStandardValueHandler's value-shaping methods:
/// Optimize (normalization), ValidateType, and Compare — for the String and
/// ListString handlers.
/// </summary>
[TestClass]
public sealed class ValueHandlers
{
    private static readonly IStandardValueHandlerProbe String = new(StandardValueType.String);
    private static readonly IStandardValueHandlerProbe ListStringHandler = new(StandardValueType.ListString);

    // Thin wrapper so the tests read cleanly.
    private sealed class IStandardValueHandlerProbe(StandardValueType type)
    {
        private readonly Abstractions.Components.IStandardValueHandler _handler = StandardValueSystem.GetHandler(type);
        public object? Optimize(object? v) => _handler.Optimize(v);
        public bool ValidateType(object? v) => _handler.ValidateType(v);
        public bool Compare(object? a, object? b) => _handler.Compare(a, b);
    }

    #region String handler

    [TestMethod]
    public void String_Optimize_TrimsAndNullsEmpty()
    {
        Assert.AreEqual("hi", String.Optimize("  hi  "));
        Assert.IsNull(String.Optimize(""));
        Assert.IsNull(String.Optimize("   "));
        Assert.IsNull(String.Optimize(null));
    }

    [TestMethod]
    public void String_Optimize_WrongClrType_ReturnsNull()
        => Assert.IsNull(String.Optimize(123));

    [TestMethod]
    public void String_ValidateType()
    {
        Assert.IsTrue(String.ValidateType("x"));
        Assert.IsFalse(String.ValidateType(123));
        Assert.IsFalse(String.ValidateType(null));
    }

    [TestMethod]
    public void String_Compare()
    {
        Assert.IsTrue(String.Compare("a", "a"));
        Assert.IsFalse(String.Compare("a", "b"));
        Assert.IsTrue(String.Compare(null, null));
        Assert.IsFalse(String.Compare("a", null));
    }

    #endregion

    #region ListString handler

    [TestMethod]
    public void ListString_Optimize_RemovesEmptyEntries()
    {
        var result = (List<string>?)ListStringHandler.Optimize(new List<string> { "a", "", "b" });
        CollectionAssert.AreEqual(new List<string> { "a", "b" }, result);
    }

    [TestMethod]
    public void ListString_Optimize_EmptyOrAllBlank_ReturnsNull()
    {
        Assert.IsNull(ListStringHandler.Optimize(new List<string>()));
        Assert.IsNull(ListStringHandler.Optimize(new List<string> { "", "  " }));
    }

    [TestMethod]
    public void ListString_Optimize_WrongClrType_ReturnsNull()
        => Assert.IsNull(ListStringHandler.Optimize("not a list"));

    [TestMethod]
    public void ListString_Compare_IsOrderSensitive()
    {
        Assert.IsTrue(ListStringHandler.Compare(
            new List<string> { "a", "b" }, new List<string> { "a", "b" }));
        Assert.IsFalse(ListStringHandler.Compare(
            new List<string> { "a", "b" }, new List<string> { "b", "a" }));
        Assert.IsFalse(ListStringHandler.Compare(
            new List<string> { "a" }, new List<string> { "a", "b" }));
    }

    [TestMethod]
    public void ListString_ValidateType()
    {
        Assert.IsTrue(ListStringHandler.ValidateType(new List<string>()));
        Assert.IsFalse(ListStringHandler.ValidateType("x"));
    }

    #endregion
}
