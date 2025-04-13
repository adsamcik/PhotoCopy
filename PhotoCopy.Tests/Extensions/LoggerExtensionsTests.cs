using Microsoft.Extensions.Logging;
using NSubstitute;
using PhotoCopy.Extensions;

namespace PhotoCopy.Tests.Extensions;

public class LoggerExtensionsTests : IClassFixture<ApplicationStateFixture>
{
    private readonly ApplicationStateFixture _fixture;

    public LoggerExtensionsTests(ApplicationStateFixture fixture)
    {
        _fixture = fixture;
        // Ensure each test starts with fresh options
        ApplicationState.Options = new Options();
    }

    [Theory]
    [InlineData(Options.LogLevel.verbose, LogLevel.Trace)]
    [InlineData(Options.LogLevel.important, LogLevel.Information)]
    [InlineData(Options.LogLevel.errorsOnly, LogLevel.Error)]
    public void Log_WithDifferentLevels_MapsToCorrectMicrosoftLogLevel(Options.LogLevel inputLevel, LogLevel expectedLevel)
    {
        // Arrange
        var logger = Substitute.For<ILogger>();
        var message = "Test message";
        ApplicationState.Options.Log = inputLevel;

        // Act
        logger.Log(message, inputLevel);

        // Assert
        logger.Received(1).Log(
            Arg.Is<LogLevel>(level => level == expectedLevel),
            Arg.Any<EventId>(),
            Arg.Is<object>(obj => obj != null && obj.ToString().Contains(message)),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>()
        );
    }

    [Theory]
    [InlineData(Options.LogLevel.verbose, Options.LogLevel.verbose, 1)]    // verbose threshold accepts verbose logs
    [InlineData(Options.LogLevel.verbose, Options.LogLevel.important, 1)]  // verbose threshold accepts important logs
    [InlineData(Options.LogLevel.verbose, Options.LogLevel.errorsOnly, 1)] // verbose threshold accepts error logs
    [InlineData(Options.LogLevel.important, Options.LogLevel.verbose, 0)]  // important threshold rejects verbose logs
    [InlineData(Options.LogLevel.important, Options.LogLevel.important, 1)] // important threshold accepts important logs
    [InlineData(Options.LogLevel.important, Options.LogLevel.errorsOnly, 1)] // important threshold accepts error logs
    [InlineData(Options.LogLevel.errorsOnly, Options.LogLevel.verbose, 0)]   // errors-only threshold rejects verbose logs
    [InlineData(Options.LogLevel.errorsOnly, Options.LogLevel.important, 0)] // errors-only threshold rejects important logs
    [InlineData(Options.LogLevel.errorsOnly, Options.LogLevel.errorsOnly, 1)] // errors-only threshold accepts error logs
    public void Log_WithDifferentThresholds_OnlyLogsWhenMeetsThreshold(
        Options.LogLevel threshold,
        Options.LogLevel inputLevel,
        int expectedCalls)
    {
        // Arrange
        var logger = Substitute.For<ILogger>();
        var message = "Test message";
        ApplicationState.Options.Log = threshold;

        // Act
        logger.Log(message, inputLevel);

        // Assert
        logger.Received(expectedCalls).Log(
            Arg.Any<LogLevel>(),
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>()
        );
    }

}