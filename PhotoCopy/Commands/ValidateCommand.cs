using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PhotoCopy;
using PhotoCopy.Abstractions;
using PhotoCopy.Configuration;
using PhotoCopy.Files;
using PhotoCopy.Validators;

namespace PhotoCopy.Commands;

/// <summary>
/// Command for validating files against configured rules without copying.
/// </summary>
public class ValidateCommand : ICommand
{
    private readonly ILogger<ValidateCommand> _logger;
    private readonly PhotoCopyConfig _config;
    private readonly IValidatorFactory _validatorFactory;
    private readonly IFileValidationService _fileValidationService;
    private readonly IFileSystem _fileSystem;

    public ValidateCommand(
        ILogger<ValidateCommand> logger,
        IOptions<PhotoCopyConfig> options,
        IValidatorFactory validatorFactory,
        IFileValidationService fileValidationService,
        IFileSystem fileSystem)
    {
        _logger = logger;
        _config = options.Value;
        _validatorFactory = validatorFactory;
        _fileValidationService = fileValidationService;
        _fileSystem = fileSystem;
    }

    public async Task<int> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Validating files in {Source}...", _config.Source);

            var validators = _validatorFactory.Create(_config);
            var files = _fileSystem.EnumerateFiles(_config.Source);

            var stats = new ValidationStatistics();
            var failures = new List<ValidationFailureInfo>();

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                stats.TotalFiles++;
                var validationResult = _fileValidationService.ValidateAll(file, validators);

                if (validationResult.IsValid)
                {
                    stats.ValidFiles++;
                    _logger.LogDebug("✓ {File}", file.File.Name);
                }
                else
                {
                    stats.InvalidFiles++;
                    _logger.LogWarning("✗ {File}", file.File.Name);
                    foreach (var failure in validationResult.Failures)
                    {
                        failures.Add(new ValidationFailureInfo(
                            file.File.FullName,
                            failure.ValidatorName ?? "Unknown",
                            failure.Reason ?? "Validation failed"));
                    }
                }
            }

            await Task.CompletedTask; // Satisfy async signature

            // Print summary
            _logger.LogInformation("");
            _logger.LogInformation("=== Validation Summary ===");
            _logger.LogInformation("Total files: {Total}", stats.TotalFiles);
            _logger.LogInformation("Valid files: {Valid}", stats.ValidFiles);
            _logger.LogInformation("Invalid files: {Invalid}", stats.InvalidFiles);

            if (failures.Count > 0)
            {
                _logger.LogInformation("");
                _logger.LogInformation("=== Validation Failures ===");
                foreach (var failure in failures)
                {
                    _logger.LogWarning(
                        "{File}: [{Validator}] {Reason}",
                        failure.FilePath,
                        failure.ValidatorName,
                        failure.Reason);
                }
            }

            return stats.InvalidFiles > 0 ? (int)ExitCode.Error : (int)ExitCode.Success;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Validation was cancelled");
            return (int)ExitCode.Cancelled;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Validation operation failed");
            return (int)ExitCode.Error;
        }
    }

    private sealed class ValidationStatistics
    {
        public int TotalFiles { get; set; }
        public int ValidFiles { get; set; }
        public int InvalidFiles { get; set; }
    }

    private sealed record ValidationFailureInfo(
        string FilePath,
        string ValidatorName,
        string Reason);
}
