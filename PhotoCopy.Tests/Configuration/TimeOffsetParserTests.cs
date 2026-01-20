using System;
using PhotoCopy.Configuration;

namespace PhotoCopy.Tests.Configuration;

public class TimeOffsetParserTests
{
    #region Parse - Valid Formats

    [Test]
    public async Task Parse_PositiveHoursMinutes_ReturnsCorrectTimeSpan()
    {
        var result = TimeOffsetParser.Parse("+2:00");
        await Assert.That(result).IsEqualTo(TimeSpan.FromHours(2));
    }

    [Test]
    public async Task Parse_NegativeHoursMinutes_ReturnsCorrectTimeSpan()
    {
        var result = TimeOffsetParser.Parse("-1:30");
        await Assert.That(result).IsEqualTo(TimeSpan.FromMinutes(-90));
    }

    [Test]
    public async Task Parse_PositiveDays_ReturnsCorrectTimeSpan()
    {
        var result = TimeOffsetParser.Parse("+1d");
        await Assert.That(result).IsEqualTo(TimeSpan.FromDays(1));
    }

    [Test]
    public async Task Parse_NegativeDays_ReturnsCorrectTimeSpan()
    {
        var result = TimeOffsetParser.Parse("-2d");
        await Assert.That(result).IsEqualTo(TimeSpan.FromDays(-2));
    }

    [Test]
    public async Task Parse_DaysAndTime_ReturnsCorrectTimeSpan()
    {
        var result = TimeOffsetParser.Parse("+1d2:30");
        await Assert.That(result).IsEqualTo(new TimeSpan(1, 2, 30, 0));
    }

    [Test]
    public async Task Parse_NegativeDaysAndTime_ReturnsCorrectTimeSpan()
    {
        var result = TimeOffsetParser.Parse("-1d2:30");
        await Assert.That(result).IsEqualTo(new TimeSpan(-1, -2, -30, 0));
    }

    [Test]
    public async Task Parse_ZeroMinutes_ReturnsCorrectTimeSpan()
    {
        var result = TimeOffsetParser.Parse("+0:15");
        await Assert.That(result).IsEqualTo(TimeSpan.FromMinutes(15));
    }

    [Test]
    public async Task Parse_SingleDigitHour_ReturnsCorrectTimeSpan()
    {
        var result = TimeOffsetParser.Parse("+5:00");
        await Assert.That(result).IsEqualTo(TimeSpan.FromHours(5));
    }

    [Test]
    public async Task Parse_MultipleDays_ReturnsCorrectTimeSpan()
    {
        var result = TimeOffsetParser.Parse("+10d");
        await Assert.That(result).IsEqualTo(TimeSpan.FromDays(10));
    }

    [Test]
    public async Task Parse_WithLeadingWhitespace_ReturnsCorrectTimeSpan()
    {
        var result = TimeOffsetParser.Parse("  +2:00");
        await Assert.That(result).IsEqualTo(TimeSpan.FromHours(2));
    }

    [Test]
    public async Task Parse_WithTrailingWhitespace_ReturnsCorrectTimeSpan()
    {
        var result = TimeOffsetParser.Parse("+2:00  ");
        await Assert.That(result).IsEqualTo(TimeSpan.FromHours(2));
    }

    #endregion

    #region Parse - Invalid Formats

    [Test]
    public async Task Parse_NullString_ThrowsFormatException()
    {
        await Assert.ThrowsAsync<FormatException>(() => Task.FromResult(TimeOffsetParser.Parse(null!)));
    }

    [Test]
    public async Task Parse_EmptyString_ThrowsFormatException()
    {
        await Assert.ThrowsAsync<FormatException>(() => Task.FromResult(TimeOffsetParser.Parse("")));
    }

    [Test]
    public async Task Parse_NoSign_ThrowsFormatException()
    {
        await Assert.ThrowsAsync<FormatException>(() => Task.FromResult(TimeOffsetParser.Parse("2:00")));
    }

    [Test]
    public async Task Parse_InvalidHours_ThrowsFormatException()
    {
        await Assert.ThrowsAsync<FormatException>(() => Task.FromResult(TimeOffsetParser.Parse("+25:00")));
    }

    [Test]
    public async Task Parse_InvalidMinutes_ThrowsFormatException()
    {
        await Assert.ThrowsAsync<FormatException>(() => Task.FromResult(TimeOffsetParser.Parse("+2:60")));
    }

    [Test]
    public async Task Parse_NoValue_ThrowsFormatException()
    {
        await Assert.ThrowsAsync<FormatException>(() => Task.FromResult(TimeOffsetParser.Parse("+")));
    }

    [Test]
    public async Task Parse_RandomText_ThrowsFormatException()
    {
        await Assert.ThrowsAsync<FormatException>(() => Task.FromResult(TimeOffsetParser.Parse("invalid")));
    }

    [Test]
    public async Task Parse_MissingMinutes_ThrowsFormatException()
    {
        await Assert.ThrowsAsync<FormatException>(() => Task.FromResult(TimeOffsetParser.Parse("+2:")));
    }

    #endregion

    #region TryParse

    [Test]
    public async Task TryParse_ValidFormat_ReturnsTrue()
    {
        var success = TimeOffsetParser.TryParse("+2:00", out var result, out var error);
        await Assert.That(success).IsTrue();
        await Assert.That(result).IsEqualTo(TimeSpan.FromHours(2));
        await Assert.That(error).IsNull();
    }

    [Test]
    public async Task TryParse_InvalidFormat_ReturnsFalseWithError()
    {
        var success = TimeOffsetParser.TryParse("invalid", out var result, out var error);
        await Assert.That(success).IsFalse();
        await Assert.That(result).IsEqualTo(TimeSpan.Zero);
        await Assert.That(error).IsNotNull();
    }

    [Test]
    public async Task TryParse_Null_ReturnsFalseWithError()
    {
        var success = TimeOffsetParser.TryParse(null, out var result, out var error);
        await Assert.That(success).IsFalse();
        await Assert.That(error).IsNotNull();
    }

    #endregion

    #region Format

    [Test]
    public async Task Format_PositiveHours_ReturnsCorrectString()
    {
        var result = TimeOffsetParser.Format(TimeSpan.FromHours(2));
        await Assert.That(result).IsEqualTo("+2:00");
    }

    [Test]
    public async Task Format_NegativeHours_ReturnsCorrectString()
    {
        var result = TimeOffsetParser.Format(TimeSpan.FromHours(-1.5));
        await Assert.That(result).IsEqualTo("-1:30");
    }

    [Test]
    public async Task Format_Days_ReturnsCorrectString()
    {
        var result = TimeOffsetParser.Format(TimeSpan.FromDays(2));
        await Assert.That(result).IsEqualTo("+2d");
    }

    [Test]
    public async Task Format_DaysAndTime_ReturnsCorrectString()
    {
        var result = TimeOffsetParser.Format(new TimeSpan(1, 2, 30, 0));
        await Assert.That(result).IsEqualTo("+1d2:30");
    }

    [Test]
    public async Task Format_Zero_ReturnsCorrectString()
    {
        var result = TimeOffsetParser.Format(TimeSpan.Zero);
        await Assert.That(result).IsEqualTo("+0:00");
    }

    [Test]
    public async Task Format_NegativeDays_ReturnsCorrectString()
    {
        var result = TimeOffsetParser.Format(TimeSpan.FromDays(-1));
        await Assert.That(result).IsEqualTo("-1d");
    }

    #endregion

    #region Round-trip

    [Test]
    [Arguments("+2:00")]
    [Arguments("-1:30")]
    [Arguments("+1d")]
    [Arguments("-2d")]
    [Arguments("+1d2:30")]
    [Arguments("+0:15")]
    [Arguments("-0:45")]
    public async Task Parse_ThenFormat_RoundTrips(string original)
    {
        var parsed = TimeOffsetParser.Parse(original);
        var formatted = TimeOffsetParser.Format(parsed);
        var reparsed = TimeOffsetParser.Parse(formatted);
        await Assert.That(reparsed).IsEqualTo(parsed);
    }

    #endregion
}
