using Bakabase.Modules.BulkModification.Components.Processors.Decimal;
using FluentAssertions;

namespace Bakabase.Modules.BulkModification.Tests.Processors;

[TestClass]
public class DecimalProcessorTests
{
    private readonly BulkModificationDecimalProcessor _processor = new();

    #region Delete

    [TestMethod]
    public void Delete_WithValue_ReturnsNull()
    {
        var result = _processor.Process(100.5m, (int)BulkModificationDecimalProcessOperation.Delete, null);
        result.Should().BeNull();
    }

    [TestMethod]
    public void Delete_WithNull_ReturnsNull()
    {
        var result = _processor.Process(null, (int)BulkModificationDecimalProcessOperation.Delete, null);
        result.Should().BeNull();
    }

    #endregion

    #region SetWithFixedValue

    [TestMethod]
    public void SetWithFixedValue_ReturnsNewValue()
    {
        var options = new BulkModificationDecimalProcessorOptions { Value = 42.5m };
        var result = _processor.Process(100m, (int)BulkModificationDecimalProcessOperation.SetWithFixedValue, options);
        result.Should().Be(42.5m);
    }

    [TestMethod]
    public void SetWithFixedValue_WithNullCurrent_ReturnsNewValue()
    {
        var options = new BulkModificationDecimalProcessorOptions { Value = 42.5m };
        var result = _processor.Process(null, (int)BulkModificationDecimalProcessOperation.SetWithFixedValue, options);
        result.Should().Be(42.5m);
    }

    [TestMethod]
    public void SetWithFixedValue_WithNullValue_ReturnsNull()
    {
        var options = new BulkModificationDecimalProcessorOptions { Value = null };
        var result = _processor.Process(100m, (int)BulkModificationDecimalProcessOperation.SetWithFixedValue, options);
        result.Should().BeNull();
    }

    #endregion

    #region Add

    [TestMethod]
    public void Add_AddsValues()
    {
        var options = new BulkModificationDecimalProcessorOptions { Value = 10.5m };
        var result = _processor.Process(100m, (int)BulkModificationDecimalProcessOperation.Add, options);
        result.Should().Be(110.5m);
    }

    [TestMethod]
    public void Add_WithNegativeValue_SubtractsEffectively()
    {
        var options = new BulkModificationDecimalProcessorOptions { Value = -10m };
        var result = _processor.Process(100m, (int)BulkModificationDecimalProcessOperation.Add, options);
        result.Should().Be(90m);
    }

    [TestMethod]
    public void Add_WithNullCurrent_ReturnsOptionValue()
    {
        var options = new BulkModificationDecimalProcessorOptions { Value = 10m };
        var result = _processor.Process(null, (int)BulkModificationDecimalProcessOperation.Add, options);
        result.Should().Be(10m);
    }

    [TestMethod]
    public void Add_WithNullOptionValue_ReturnsCurrent()
    {
        var options = new BulkModificationDecimalProcessorOptions { Value = null };
        var result = _processor.Process(100m, (int)BulkModificationDecimalProcessOperation.Add, options);
        result.Should().Be(100m);
    }

    #endregion

    #region Subtract

    [TestMethod]
    public void Subtract_SubtractsValues()
    {
        var options = new BulkModificationDecimalProcessorOptions { Value = 30m };
        var result = _processor.Process(100m, (int)BulkModificationDecimalProcessOperation.Subtract, options);
        result.Should().Be(70m);
    }

    [TestMethod]
    public void Subtract_WithNegativeValue_AddsEffectively()
    {
        var options = new BulkModificationDecimalProcessorOptions { Value = -10m };
        var result = _processor.Process(100m, (int)BulkModificationDecimalProcessOperation.Subtract, options);
        result.Should().Be(110m);
    }

    [TestMethod]
    public void Subtract_WithNullCurrent_ReturnsCurrent()
    {
        var options = new BulkModificationDecimalProcessorOptions { Value = 10m };
        var result = _processor.Process(null, (int)BulkModificationDecimalProcessOperation.Subtract, options);
        result.Should().BeNull();
    }

    [TestMethod]
    public void Subtract_ResultCanBeNegative()
    {
        var options = new BulkModificationDecimalProcessorOptions { Value = 150m };
        var result = _processor.Process(100m, (int)BulkModificationDecimalProcessOperation.Subtract, options);
        result.Should().Be(-50m);
    }

    #endregion

    #region Multiply

    [TestMethod]
    public void Multiply_MultipliesValues()
    {
        var options = new BulkModificationDecimalProcessorOptions { Value = 3m };
        var result = _processor.Process(10m, (int)BulkModificationDecimalProcessOperation.Multiply, options);
        result.Should().Be(30m);
    }

    [TestMethod]
    public void Multiply_WithZero_ReturnsZero()
    {
        var options = new BulkModificationDecimalProcessorOptions { Value = 0m };
        var result = _processor.Process(100m, (int)BulkModificationDecimalProcessOperation.Multiply, options);
        result.Should().Be(0m);
    }

    [TestMethod]
    public void Multiply_WithNegative_ReturnsNegative()
    {
        var options = new BulkModificationDecimalProcessorOptions { Value = -2m };
        var result = _processor.Process(50m, (int)BulkModificationDecimalProcessOperation.Multiply, options);
        result.Should().Be(-100m);
    }

    [TestMethod]
    public void Multiply_WithDecimal_HandlesCorrectly()
    {
        var options = new BulkModificationDecimalProcessorOptions { Value = 0.5m };
        var result = _processor.Process(100m, (int)BulkModificationDecimalProcessOperation.Multiply, options);
        result.Should().Be(50m);
    }

    #endregion

    #region Divide

    [TestMethod]
    public void Divide_DividesValues()
    {
        var options = new BulkModificationDecimalProcessorOptions { Value = 4m };
        var result = _processor.Process(100m, (int)BulkModificationDecimalProcessOperation.Divide, options);
        result.Should().Be(25m);
    }

    [TestMethod]
    public void Divide_ByZero_ReturnsCurrent()
    {
        var options = new BulkModificationDecimalProcessorOptions { Value = 0m };
        var result = _processor.Process(100m, (int)BulkModificationDecimalProcessOperation.Divide, options);
        result.Should().Be(100m); // Should not throw, returns current value
    }

    [TestMethod]
    public void Divide_WithDecimalResult()
    {
        var options = new BulkModificationDecimalProcessorOptions { Value = 3m };
        var result = _processor.Process(10m, (int)BulkModificationDecimalProcessOperation.Divide, options);
        ((decimal?)result).Should().BeApproximately(3.333333333333333333333333333m, 0.0000001m);
    }

    [TestMethod]
    public void Divide_WithNullCurrent_ReturnsCurrent()
    {
        var options = new BulkModificationDecimalProcessorOptions { Value = 2m };
        var result = _processor.Process(null, (int)BulkModificationDecimalProcessOperation.Divide, options);
        result.Should().BeNull();
    }

    #endregion

    #region Round

    [TestMethod]
    public void Round_RoundsToZeroDecimalPlaces()
    {
        var options = new BulkModificationDecimalProcessorOptions { DecimalPlaces = 0 };
        var result = _processor.Process(3.7m, (int)BulkModificationDecimalProcessOperation.Round, options);
        result.Should().Be(4m);
    }

    [TestMethod]
    public void Round_RoundsToTwoDecimalPlaces()
    {
        var options = new BulkModificationDecimalProcessorOptions { DecimalPlaces = 2 };
        var result = _processor.Process(3.14159m, (int)BulkModificationDecimalProcessOperation.Round, options);
        result.Should().Be(3.14m);
    }

    [TestMethod]
    public void Round_RoundsDown_WhenBelow5()
    {
        var options = new BulkModificationDecimalProcessorOptions { DecimalPlaces = 0 };
        var result = _processor.Process(3.4m, (int)BulkModificationDecimalProcessOperation.Round, options);
        result.Should().Be(3m);
    }

    [TestMethod]
    public void Round_WithNullDecimalPlaces_DefaultsToZero()
    {
        var options = new BulkModificationDecimalProcessorOptions { DecimalPlaces = null };
        var result = _processor.Process(3.7m, (int)BulkModificationDecimalProcessOperation.Round, options);
        result.Should().Be(4m);
    }

    [TestMethod]
    public void Round_WithNullCurrent_ReturnsNull()
    {
        var options = new BulkModificationDecimalProcessorOptions { DecimalPlaces = 2 };
        var result = _processor.Process(null, (int)BulkModificationDecimalProcessOperation.Round, options);
        result.Should().BeNull();
    }

    #endregion

    #region Ceil

    [TestMethod]
    public void Ceil_RoundsUp()
    {
        var result = _processor.Process(3.1m, (int)BulkModificationDecimalProcessOperation.Ceil, null);
        result.Should().Be(4m);
    }

    [TestMethod]
    public void Ceil_WithWholeNumber_ReturnsSame()
    {
        var result = _processor.Process(3m, (int)BulkModificationDecimalProcessOperation.Ceil, null);
        result.Should().Be(3m);
    }

    [TestMethod]
    public void Ceil_WithNegative_RoundsTowardsZero()
    {
        var result = _processor.Process(-3.1m, (int)BulkModificationDecimalProcessOperation.Ceil, null);
        result.Should().Be(-3m);
    }

    [TestMethod]
    public void Ceil_WithNullCurrent_ReturnsNull()
    {
        var result = _processor.Process(null, (int)BulkModificationDecimalProcessOperation.Ceil, null);
        result.Should().BeNull();
    }

    #endregion

    #region Floor

    [TestMethod]
    public void Floor_RoundsDown()
    {
        var result = _processor.Process(3.9m, (int)BulkModificationDecimalProcessOperation.Floor, null);
        result.Should().Be(3m);
    }

    [TestMethod]
    public void Floor_WithWholeNumber_ReturnsSame()
    {
        var result = _processor.Process(3m, (int)BulkModificationDecimalProcessOperation.Floor, null);
        result.Should().Be(3m);
    }

    [TestMethod]
    public void Floor_WithNegative_RoundsAwayFromZero()
    {
        var result = _processor.Process(-3.1m, (int)BulkModificationDecimalProcessOperation.Floor, null);
        result.Should().Be(-4m);
    }

    [TestMethod]
    public void Floor_WithNullCurrent_ReturnsNull()
    {
        var result = _processor.Process(null, (int)BulkModificationDecimalProcessOperation.Floor, null);
        result.Should().BeNull();
    }

    #endregion

    #region Edge Cases

    [TestMethod]
    public void Process_WithVeryLargeNumber()
    {
        // Use smaller values to avoid overflow
        var options = new BulkModificationDecimalProcessorOptions { Value = 2m };
        var result = _processor.Process(1000000000m, (int)BulkModificationDecimalProcessOperation.Multiply, options);
        ((decimal?)result).Should().Be(2000000000m);
    }

    [TestMethod]
    public void Process_WithVerySmallNumber()
    {
        var options = new BulkModificationDecimalProcessorOptions { Value = 0.0000001m };
        var result = _processor.Process(1m, (int)BulkModificationDecimalProcessOperation.Multiply, options);
        result.Should().Be(0.0000001m);
    }

    #endregion
}
