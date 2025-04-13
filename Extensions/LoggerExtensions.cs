using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PhotoCopy.Extensions;

public static class LoggerExtensions
{
    /// <summary>
    /// Logs a message if the provided log level is above or equal to the threshold specified in <see cref="ApplicationState.Options.Log"/>.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="message">The message to log.</param>
    /// <param name="logLevel">The custom log level.</param>
    public static void Log(this ILogger logger, string message, Options.LogLevel logLevel)
    {
        // If Options is null, create a new one with default values
        if (ApplicationState.Options == null)
        {
            ApplicationState.Options = new Options();
        }
        
        if ((int)logLevel >= (int)ApplicationState.Options.Log)
        {
            logger.Log(
                MapToMicrosoftLogLevel(logLevel),
                new EventId(),
                (object)message,
                null,
                (state, ex) => state.ToString()
            );
        }
    }

    /// <summary>
    /// Maps the custom <see cref="Options.LogLevel"/> to <see cref="Microsoft.Extensions.Logging.LogLevel"/>.
    /// </summary>
    /// <param name="logLevel">The custom log level.</param>
    /// <returns>The corresponding Microsoft log level.</returns>
    private static LogLevel MapToMicrosoftLogLevel(Options.LogLevel logLevel)
    {
        return logLevel switch
        {
            Options.LogLevel.verbose => LogLevel.Trace,
            Options.LogLevel.important => LogLevel.Information,
            Options.LogLevel.errorsOnly => LogLevel.Error,
            _ => LogLevel.Information,
        };
    }
}
