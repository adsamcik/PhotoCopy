using Microsoft.Extensions.Logging;
using PhotoCopy.Configuration;
using System;
using System.IO;

namespace PhotoCopy.Validators;

/// <summary>
/// Validates user input and configuration before command execution.
/// </summary>
public class InputValidator : IInputValidator
{
    private readonly ILogger<InputValidator> _logger;
    private readonly IConsoleInteraction _console;

    /// <summary>
    /// Creates a new input validator.
    /// </summary>
    /// <param name="logger">Logger for error messages.</param>
    /// <param name="console">Console interaction abstraction for user prompts.</param>
    public InputValidator(ILogger<InputValidator> logger, IConsoleInteraction console)
    {
        _logger = logger;
        _console = console;
    }

    /// <inheritdoc />
    public bool ValidateCopyConfiguration(PhotoCopyConfig config)
    {
        if (string.IsNullOrEmpty(config.Source))
        {
            _logger.LogError("Source path is required");
            return false;
        }

        if (string.IsNullOrEmpty(config.Destination))
        {
            _logger.LogError("Destination path is required");
            return false;
        }

        var sourceDir = new DirectoryInfo(config.Source);
        var isValid = true;

        if (!sourceDir.Exists)
        {
            _logger.LogError("Source {SourcePath} does not exist", sourceDir.FullName);
            isValid = false;
        }

        if (!config.DuplicatesFormat.Contains(DestinationVariables.Number))
        {
            _logger.LogError("Duplicates format does not contain {{number}}");
            isValid = false;
        }

        if (!config.Destination.Contains(DestinationVariables.Name) && 
            !config.Destination.Contains(DestinationVariables.NameNoExtension))
        {
            if (!ConfirmMissingFilenameVariable())
            {
                isValid = false;
            }
        }

        return isValid;
    }

    /// <inheritdoc />
    public bool ValidateSourceRequired(PhotoCopyConfig config)
    {
        if (string.IsNullOrEmpty(config.Source))
        {
            _logger.LogError("Source path is required");
            return false;
        }
        return true;
    }

    private bool ConfirmMissingFilenameVariable()
    {
        _console.WriteLine("Your destination path does not contain name or name without extension. " +
            "This will result in files losing their original name and is generally undesirable. " +
            "Are you absolutely sure about this? Write yes to confirm, anything else to abort.");

        // In non-interactive mode (piped input, CI), default to abort
        if (_console.IsInputRedirected)
        {
            _logger.LogWarning("Non-interactive mode detected, aborting due to missing filename variable in destination");
            return false;
        }

        var response = _console.ReadLine();
        return response == "yes";
    }
}
