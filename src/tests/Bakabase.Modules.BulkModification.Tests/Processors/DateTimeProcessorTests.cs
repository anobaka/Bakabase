using Bakabase.Modules.BulkModification.Components.Processors.DateTime;
using FluentAssertions;

namespace Bakabase.Modules.BulkModification.Tests.Processors;

[TestClass]
public class DateTimeProcessorTests
{
    private readonly BulkModificationDateTimeProcessor _processor = new();

    #region Delete

    [TestMethod]
    public void Delete_WithValue_ReturnsNull()
    {
        var result = _processor.Process(System.DateTime.Now, (int)BulkModificationDateTimeProcessOperation.Delete, null);
        result.Should().BeNull();
    }

    [TestMethod]
    public void Delete_WithNull_ReturnsNull()
    {
        var result = _processor.Process(null, (int)BulkModificationDateTimeProcessOperation.Delete, null);
        result.Should().BeNull();
    }

    #endregion

    #region SetWithFixedValue

    [TestMethod]
    public void SetWithFixedValue_ReturnsNewValue()
    {
        var targetDate = new System.DateTime(2024, 6, 15, 10, 30, 0);
        var options = new BulkModificationDateTimeProcessorOptions { Value = targetDate };
        var result = _processor.Process(System.DateTime.Now, (int)BulkModificationDateTimeProcessOperation.SetWithFixedValue, options);
        result.Should().Be(targetDate);
    }

    [TestMethod]
    public void SetWithFixedValue_WithNullCurrent_ReturnsNewValue()
    {
        var targetDate = new System.DateTime(2024, 6, 15);
        var options = new BulkModificationDateTimeProcessorOptions { Value = targetDate };
        var result = _processor.Process(null, (int)BulkModificationDateTimeProcessOperation.SetWithFixedValue, options);
        result.Should().Be(targetDate);
    }

    #endregion

    #region SetToNow

    [TestMethod]
    public void SetToNow_ReturnsCurrentTime()
    {
        var before = System.DateTime.Now;
        var result = _processor.Process(new System.DateTime(2000, 1, 1), (int)BulkModificationDateTimeProcessOperation.SetToNow, null);
        var after = System.DateTime.Now;

        result.Should().NotBeNull();
        ((System.DateTime?)result).Should().BeOnOrAfter(before);
        ((System.DateTime?)result).Should().BeOnOrBefore(after);
    }

    [TestMethod]
    public void SetToNow_WithNullCurrent_ReturnsCurrentTime()
    {
        var before = System.DateTime.Now;
        var result = _processor.Process(null, (int)BulkModificationDateTimeProcessOperation.SetToNow, null);
        var after = System.DateTime.Now;

        result.Should().NotBeNull();
        ((System.DateTime?)result).Should().BeOnOrAfter(before);
        ((System.DateTime?)result).Should().BeOnOrBefore(after);
    }

    #endregion

    #region AddDays

    [TestMethod]
    public void AddDays_AddsDays()
    {
        var baseDate = new System.DateTime(2024, 1, 15);
        var options = new BulkModificationDateTimeProcessorOptions { Amount = 10 };
        var result = _processor.Process(baseDate, (int)BulkModificationDateTimeProcessOperation.AddDays, options);
        result.Should().Be(new System.DateTime(2024, 1, 25));
    }

    [TestMethod]
    public void AddDays_CrossesMonthBoundary()
    {
        var baseDate = new System.DateTime(2024, 1, 25);
        var options = new BulkModificationDateTimeProcessorOptions { Amount = 10 };
        var result = _processor.Process(baseDate, (int)BulkModificationDateTimeProcessOperation.AddDays, options);
        result.Should().Be(new System.DateTime(2024, 2, 4));
    }

    [TestMethod]
    public void AddDays_WithNullCurrent_ReturnsNull()
    {
        var options = new BulkModificationDateTimeProcessorOptions { Amount = 10 };
        var result = _processor.Process(null, (int)BulkModificationDateTimeProcessOperation.AddDays, options);
        result.Should().BeNull();
    }

    [TestMethod]
    public void AddDays_WithZeroAmount_ReturnsSameDate()
    {
        var baseDate = new System.DateTime(2024, 1, 15);
        var options = new BulkModificationDateTimeProcessorOptions { Amount = 0 };
        var result = _processor.Process(baseDate, (int)BulkModificationDateTimeProcessOperation.AddDays, options);
        result.Should().Be(baseDate);
    }

    #endregion

    #region SubtractDays

    [TestMethod]
    public void SubtractDays_SubtractsDays()
    {
        var baseDate = new System.DateTime(2024, 1, 25);
        var options = new BulkModificationDateTimeProcessorOptions { Amount = 10 };
        var result = _processor.Process(baseDate, (int)BulkModificationDateTimeProcessOperation.SubtractDays, options);
        result.Should().Be(new System.DateTime(2024, 1, 15));
    }

    [TestMethod]
    public void SubtractDays_CrossesMonthBoundary()
    {
        var baseDate = new System.DateTime(2024, 2, 5);
        var options = new BulkModificationDateTimeProcessorOptions { Amount = 10 };
        var result = _processor.Process(baseDate, (int)BulkModificationDateTimeProcessOperation.SubtractDays, options);
        result.Should().Be(new System.DateTime(2024, 1, 26));
    }

    #endregion

    #region AddMonths

    [TestMethod]
    public void AddMonths_AddsMonths()
    {
        var baseDate = new System.DateTime(2024, 3, 15);
        var options = new BulkModificationDateTimeProcessorOptions { Amount = 2 };
        var result = _processor.Process(baseDate, (int)BulkModificationDateTimeProcessOperation.AddMonths, options);
        result.Should().Be(new System.DateTime(2024, 5, 15));
    }

    [TestMethod]
    public void AddMonths_CrossesYearBoundary()
    {
        var baseDate = new System.DateTime(2024, 11, 15);
        var options = new BulkModificationDateTimeProcessorOptions { Amount = 3 };
        var result = _processor.Process(baseDate, (int)BulkModificationDateTimeProcessOperation.AddMonths, options);
        result.Should().Be(new System.DateTime(2025, 2, 15));
    }

    [TestMethod]
    public void AddMonths_AdjustsForShorterMonth()
    {
        var baseDate = new System.DateTime(2024, 1, 31); // Jan 31
        var options = new BulkModificationDateTimeProcessorOptions { Amount = 1 };
        var result = _processor.Process(baseDate, (int)BulkModificationDateTimeProcessOperation.AddMonths, options);
        result.Should().Be(new System.DateTime(2024, 2, 29)); // Feb 29 (leap year)
    }

    #endregion

    #region SubtractMonths

    [TestMethod]
    public void SubtractMonths_SubtractsMonths()
    {
        var baseDate = new System.DateTime(2024, 5, 15);
        var options = new BulkModificationDateTimeProcessorOptions { Amount = 2 };
        var result = _processor.Process(baseDate, (int)BulkModificationDateTimeProcessOperation.SubtractMonths, options);
        result.Should().Be(new System.DateTime(2024, 3, 15));
    }

    [TestMethod]
    public void SubtractMonths_CrossesYearBoundary()
    {
        var baseDate = new System.DateTime(2024, 2, 15);
        var options = new BulkModificationDateTimeProcessorOptions { Amount = 3 };
        var result = _processor.Process(baseDate, (int)BulkModificationDateTimeProcessOperation.SubtractMonths, options);
        result.Should().Be(new System.DateTime(2023, 11, 15));
    }

    #endregion

    #region AddYears

    [TestMethod]
    public void AddYears_AddsYears()
    {
        var baseDate = new System.DateTime(2024, 6, 15);
        var options = new BulkModificationDateTimeProcessorOptions { Amount = 5 };
        var result = _processor.Process(baseDate, (int)BulkModificationDateTimeProcessOperation.AddYears, options);
        result.Should().Be(new System.DateTime(2029, 6, 15));
    }

    [TestMethod]
    public void AddYears_LeapYearToNonLeapYear()
    {
        var baseDate = new System.DateTime(2024, 2, 29); // Leap year
        var options = new BulkModificationDateTimeProcessorOptions { Amount = 1 };
        var result = _processor.Process(baseDate, (int)BulkModificationDateTimeProcessOperation.AddYears, options);
        result.Should().Be(new System.DateTime(2025, 2, 28)); // Non-leap year
    }

    #endregion

    #region SubtractYears

    [TestMethod]
    public void SubtractYears_SubtractsYears()
    {
        var baseDate = new System.DateTime(2024, 6, 15);
        var options = new BulkModificationDateTimeProcessorOptions { Amount = 5 };
        var result = _processor.Process(baseDate, (int)BulkModificationDateTimeProcessOperation.SubtractYears, options);
        result.Should().Be(new System.DateTime(2019, 6, 15));
    }

    #endregion

    #region Edge Cases

    [TestMethod]
    public void Process_PreservesTimeComponent()
    {
        var baseDate = new System.DateTime(2024, 1, 15, 14, 30, 45);
        var options = new BulkModificationDateTimeProcessorOptions { Amount = 1 };
        var result = _processor.Process(baseDate, (int)BulkModificationDateTimeProcessOperation.AddDays, options);
        result.Should().Be(new System.DateTime(2024, 1, 16, 14, 30, 45));
    }

    [TestMethod]
    public void Process_WithNullOptions_UsesDefaultAmount()
    {
        var baseDate = new System.DateTime(2024, 1, 15);
        var result = _processor.Process(baseDate, (int)BulkModificationDateTimeProcessOperation.AddDays, null);
        result.Should().Be(baseDate); // Amount defaults to 0
    }

    #endregion
}
