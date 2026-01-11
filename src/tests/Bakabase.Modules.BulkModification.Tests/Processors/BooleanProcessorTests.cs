using Bakabase.Modules.BulkModification.Components.Processors.Boolean;
using FluentAssertions;

namespace Bakabase.Modules.BulkModification.Tests.Processors;

[TestClass]
public class BooleanProcessorTests
{
    private readonly BulkModificationBooleanProcessor _processor = new();

    #region Delete

    [TestMethod]
    public void Delete_WithTrue_ReturnsNull()
    {
        var result = _processor.Process(true, (int)BulkModificationBooleanProcessOperation.Delete, null);
        result.Should().BeNull();
    }

    [TestMethod]
    public void Delete_WithFalse_ReturnsNull()
    {
        var result = _processor.Process(false, (int)BulkModificationBooleanProcessOperation.Delete, null);
        result.Should().BeNull();
    }

    [TestMethod]
    public void Delete_WithNull_ReturnsNull()
    {
        var result = _processor.Process(null, (int)BulkModificationBooleanProcessOperation.Delete, null);
        result.Should().BeNull();
    }

    #endregion

    #region SetWithFixedValue

    [TestMethod]
    public void SetWithFixedValue_ToTrue_ReturnsTrue()
    {
        var options = new BulkModificationBooleanProcessorOptions { Value = true };
        var result = _processor.Process(false, (int)BulkModificationBooleanProcessOperation.SetWithFixedValue, options);
        ((bool?)result).Should().BeTrue();
    }

    [TestMethod]
    public void SetWithFixedValue_ToFalse_ReturnsFalse()
    {
        var options = new BulkModificationBooleanProcessorOptions { Value = false };
        var result = _processor.Process(true, (int)BulkModificationBooleanProcessOperation.SetWithFixedValue, options);
        ((bool?)result).Should().BeFalse();
    }

    [TestMethod]
    public void SetWithFixedValue_WithNullCurrent_ReturnsNewValue()
    {
        var options = new BulkModificationBooleanProcessorOptions { Value = true };
        var result = _processor.Process(null, (int)BulkModificationBooleanProcessOperation.SetWithFixedValue, options);
        ((bool?)result).Should().BeTrue();
    }

    [TestMethod]
    public void SetWithFixedValue_WithNullValue_ReturnsNull()
    {
        var options = new BulkModificationBooleanProcessorOptions { Value = null };
        var result = _processor.Process(true, (int)BulkModificationBooleanProcessOperation.SetWithFixedValue, options);
        result.Should().BeNull();
    }

    #endregion

    #region Toggle

    [TestMethod]
    public void Toggle_FromTrue_ReturnsFalse()
    {
        var result = _processor.Process(true, (int)BulkModificationBooleanProcessOperation.Toggle, null);
        ((bool?)result).Should().BeFalse();
    }

    [TestMethod]
    public void Toggle_FromFalse_ReturnsTrue()
    {
        var result = _processor.Process(false, (int)BulkModificationBooleanProcessOperation.Toggle, null);
        ((bool?)result).Should().BeTrue();
    }

    [TestMethod]
    public void Toggle_FromNull_ReturnsTrue()
    {
        // When null, toggle defaults to true
        var result = _processor.Process(null, (int)BulkModificationBooleanProcessOperation.Toggle, null);
        ((bool?)result).Should().BeTrue();
    }

    [TestMethod]
    public void Toggle_TwiceFromTrue_ReturnsTrue()
    {
        var first = _processor.Process(true, (int)BulkModificationBooleanProcessOperation.Toggle, null);
        var second = _processor.Process(first, (int)BulkModificationBooleanProcessOperation.Toggle, null);
        ((bool?)second).Should().BeTrue();
    }

    [TestMethod]
    public void Toggle_TwiceFromFalse_ReturnsFalse()
    {
        var first = _processor.Process(false, (int)BulkModificationBooleanProcessOperation.Toggle, null);
        var second = _processor.Process(first, (int)BulkModificationBooleanProcessOperation.Toggle, null);
        ((bool?)second).Should().BeFalse();
    }

    #endregion

    #region Edge Cases

    [TestMethod]
    public void Toggle_IgnoresOptions()
    {
        // Toggle should ignore any provided options
        var options = new BulkModificationBooleanProcessorOptions { Value = false };
        var result = _processor.Process(false, (int)BulkModificationBooleanProcessOperation.Toggle, options);
        ((bool?)result).Should().BeTrue(); // Should toggle, not set to options value
    }

    #endregion
}
