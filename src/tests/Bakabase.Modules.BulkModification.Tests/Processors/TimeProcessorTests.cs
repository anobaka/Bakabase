using Bakabase.Modules.BulkModification.Components.Processors.Time;
using FluentAssertions;

namespace Bakabase.Modules.BulkModification.Tests.Processors;

[TestClass]
public class TimeProcessorTests
{
    private readonly BulkModificationTimeProcessor _processor = new();

    #region Delete

    [TestMethod]
    public void Delete_WithValue_ReturnsNull()
    {
        var result = _processor.Process(TimeSpan.FromHours(5), (int)BulkModificationTimeProcessOperation.Delete, null);
        result.Should().BeNull();
    }

    [TestMethod]
    public void Delete_WithNull_ReturnsNull()
    {
        var result = _processor.Process(null, (int)BulkModificationTimeProcessOperation.Delete, null);
        result.Should().BeNull();
    }

    #endregion

    #region SetWithFixedValue

    [TestMethod]
    public void SetWithFixedValue_ReturnsNewValue()
    {
        var targetTime = TimeSpan.FromHours(10);
        var options = new BulkModificationTimeProcessorOptions { Value = targetTime };
        var result = _processor.Process(TimeSpan.FromHours(5), (int)BulkModificationTimeProcessOperation.SetWithFixedValue, options);
        result.Should().Be(targetTime);
    }

    [TestMethod]
    public void SetWithFixedValue_WithNullCurrent_ReturnsNewValue()
    {
        var targetTime = TimeSpan.FromMinutes(90);
        var options = new BulkModificationTimeProcessorOptions { Value = targetTime };
        var result = _processor.Process(null, (int)BulkModificationTimeProcessOperation.SetWithFixedValue, options);
        result.Should().Be(targetTime);
    }

    #endregion

    #region AddHours

    [TestMethod]
    public void AddHours_AddsHours()
    {
        var baseTime = TimeSpan.FromHours(5);
        var options = new BulkModificationTimeProcessorOptions { Amount = 3 };
        var result = _processor.Process(baseTime, (int)BulkModificationTimeProcessOperation.AddHours, options);
        result.Should().Be(TimeSpan.FromHours(8));
    }

    [TestMethod]
    public void AddHours_WithNullCurrent_ReturnsNull()
    {
        var options = new BulkModificationTimeProcessorOptions { Amount = 3 };
        var result = _processor.Process(null, (int)BulkModificationTimeProcessOperation.AddHours, options);
        result.Should().BeNull();
    }

    [TestMethod]
    public void AddHours_CanExceed24Hours()
    {
        var baseTime = TimeSpan.FromHours(20);
        var options = new BulkModificationTimeProcessorOptions { Amount = 10 };
        var result = _processor.Process(baseTime, (int)BulkModificationTimeProcessOperation.AddHours, options);
        result.Should().Be(TimeSpan.FromHours(30)); // 30 hours total
    }

    #endregion

    #region SubtractHours

    [TestMethod]
    public void SubtractHours_SubtractsHours()
    {
        var baseTime = TimeSpan.FromHours(10);
        var options = new BulkModificationTimeProcessorOptions { Amount = 3 };
        var result = _processor.Process(baseTime, (int)BulkModificationTimeProcessOperation.SubtractHours, options);
        result.Should().Be(TimeSpan.FromHours(7));
    }

    [TestMethod]
    public void SubtractHours_CanBeNegative()
    {
        var baseTime = TimeSpan.FromHours(2);
        var options = new BulkModificationTimeProcessorOptions { Amount = 5 };
        var result = _processor.Process(baseTime, (int)BulkModificationTimeProcessOperation.SubtractHours, options);
        result.Should().Be(TimeSpan.FromHours(-3));
    }

    #endregion

    #region AddMinutes

    [TestMethod]
    public void AddMinutes_AddsMinutes()
    {
        var baseTime = TimeSpan.FromMinutes(30);
        var options = new BulkModificationTimeProcessorOptions { Amount = 45 };
        var result = _processor.Process(baseTime, (int)BulkModificationTimeProcessOperation.AddMinutes, options);
        result.Should().Be(TimeSpan.FromMinutes(75));
    }

    [TestMethod]
    public void AddMinutes_ConvertToHours()
    {
        var baseTime = TimeSpan.Zero;
        var options = new BulkModificationTimeProcessorOptions { Amount = 90 };
        var result = _processor.Process(baseTime, (int)BulkModificationTimeProcessOperation.AddMinutes, options);
        result.Should().Be(TimeSpan.FromHours(1.5));
    }

    #endregion

    #region SubtractMinutes

    [TestMethod]
    public void SubtractMinutes_SubtractsMinutes()
    {
        var baseTime = TimeSpan.FromMinutes(60);
        var options = new BulkModificationTimeProcessorOptions { Amount = 30 };
        var result = _processor.Process(baseTime, (int)BulkModificationTimeProcessOperation.SubtractMinutes, options);
        result.Should().Be(TimeSpan.FromMinutes(30));
    }

    #endregion

    #region AddSeconds

    [TestMethod]
    public void AddSeconds_AddsSeconds()
    {
        var baseTime = TimeSpan.FromSeconds(30);
        var options = new BulkModificationTimeProcessorOptions { Amount = 45 };
        var result = _processor.Process(baseTime, (int)BulkModificationTimeProcessOperation.AddSeconds, options);
        result.Should().Be(TimeSpan.FromSeconds(75));
    }

    [TestMethod]
    public void AddSeconds_ConvertToMinutes()
    {
        var baseTime = TimeSpan.Zero;
        var options = new BulkModificationTimeProcessorOptions { Amount = 120 };
        var result = _processor.Process(baseTime, (int)BulkModificationTimeProcessOperation.AddSeconds, options);
        result.Should().Be(TimeSpan.FromMinutes(2));
    }

    #endregion

    #region SubtractSeconds

    [TestMethod]
    public void SubtractSeconds_SubtractsSeconds()
    {
        var baseTime = TimeSpan.FromSeconds(60);
        var options = new BulkModificationTimeProcessorOptions { Amount = 30 };
        var result = _processor.Process(baseTime, (int)BulkModificationTimeProcessOperation.SubtractSeconds, options);
        result.Should().Be(TimeSpan.FromSeconds(30));
    }

    #endregion

    #region Edge Cases

    [TestMethod]
    public void Process_WithNullAmount_UsesDefaultZero()
    {
        var baseTime = TimeSpan.FromHours(5);
        var options = new BulkModificationTimeProcessorOptions { Amount = null };
        var result = _processor.Process(baseTime, (int)BulkModificationTimeProcessOperation.AddHours, options);
        result.Should().Be(TimeSpan.FromHours(5));
    }

    [TestMethod]
    public void Process_WithComplexTimeSpan()
    {
        var baseTime = new TimeSpan(2, 30, 45); // 2h 30m 45s
        var options = new BulkModificationTimeProcessorOptions { Amount = 1 };
        var result = _processor.Process(baseTime, (int)BulkModificationTimeProcessOperation.AddHours, options);
        result.Should().Be(new TimeSpan(3, 30, 45)); // 3h 30m 45s
    }

    #endregion
}
