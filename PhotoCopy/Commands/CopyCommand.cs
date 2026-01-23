using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PhotoCopy;
using PhotoCopy.Configuration;
using PhotoCopy.Directories;
using PhotoCopy.Progress;
using PhotoCopy.Statistics;
using PhotoCopy.Validators;

namespace PhotoCopy.Commands;

/// <summary>
/// Command for copying/moving files from source to destination.
/// </summary>
public class CopyCommand : ICommand
{
    private readonly ILogger<CopyCommand> _logger;
    private readonly PhotoCopyConfig _config;
    private readonly IDirectoryCopier _directoryCopier;
    private readonly IDirectoryCopierAsync _directoryCopierAsync;
    private readonly IValidatorFactory _validatorFactory;
    private readonly IProgressReporter _progressReporter;
    private readonly StatisticsReporter _statisticsReporter;

    public CopyCommand(
        ILogger<CopyCommand> logger,
        IOptions<PhotoCopyConfig> options,
        IDirectoryCopier directoryCopier,
        IDirectoryCopierAsync directoryCopierAsync,
        IValidatorFactory validatorFactory,
        IProgressReporter progressReporter)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(directoryCopier);
        ArgumentNullException.ThrowIfNull(directoryCopierAsync);
        ArgumentNullException.ThrowIfNull(validatorFactory);
        ArgumentNullException.ThrowIfNull(progressReporter);

        _logger = logger;
        _config = options.Value;
        _directoryCopier = directoryCopier;
        _directoryCopierAsync = directoryCopierAsync;
        _validatorFactory = validatorFactory;
        _progressReporter = progressReporter;
        _statisticsReporter = new StatisticsReporter();
    }

    public async Task<int> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Starting {Mode} operation from {Source}",
                _config.Mode, _config.Source);

            var validators = _validatorFactory.Create(_config);

            CopyResult result;
            if (_config.UseAsync)
            {
                result = await _directoryCopierAsync.CopyAsync(
                    validators, _progressReporter, cancellationToken);
            }
            else
            {
                // Use synchronous copier (wrap in task for consistency)
                result = await Task.Run(() => _directoryCopier.Copy(validators), cancellationToken);
            }
            
            LogResult(result);
            OutputStatisticsSummary(result);
            OutputUnknownFilesReport(result);
            
            // Return appropriate exit code based on results
            if (result.FilesFailed > 0 && result.FilesProcessed > 0)
            {
                // Some files succeeded, some failed
                return (int)ExitCode.PartialSuccess;
            }
            else if (result.FilesFailed > 0)
            {
                // All files failed
                return (int)ExitCode.Error;
            }
            
            return (int)ExitCode.Success;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Operation was cancelled");
            return (int)ExitCode.Cancelled;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Copy operation failed due to permission error");
            return (int)ExitCode.IOError;
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "Copy operation failed due to I/O error");
            return (int)ExitCode.IOError;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Copy operation failed");
            return (int)ExitCode.Error;
        }
    }

    private void LogResult(CopyResult result)
    {
        _logger.LogInformation(
            "Operation complete: {Processed} files processed, {Failed} failed, {Skipped} skipped",
            result.FilesProcessed, result.FilesFailed, result.FilesSkipped);

        foreach (var error in result.Errors)
        {
            _logger.LogError("Failed to process {File}: {Error}",
                error.File.File.Name, error.ErrorMessage);
        }
    }

    private void OutputStatisticsSummary(CopyResult result)
    {
        // Don't output statistics if log level is ErrorsOnly or if there are no statistics
        if (_config.LogLevel == OutputLevel.ErrorsOnly || result.Statistics == null)
        {
            return;
        }

        // Only show summary if files were actually processed
        if (result.Statistics.TotalFiles == 0 && result.FilesProcessed == 0)
        {
            return;
        }

        var report = _statisticsReporter.GenerateReport(result.Statistics);
        
        // Output to console directly for visibility (after progress is complete)
        Console.WriteLine();
        Console.WriteLine(report);
    }

    private void OutputUnknownFilesReport(CopyResult result)
    {
        if (_config.UnknownReport == UnknownReportLevel.None || result.UnknownFilesReport == null)
        {
            return;
        }

        var report = result.UnknownFilesReport;
        if (report.Count == 0)
        {
            return;
        }

        var includeDetailedList = _config.UnknownReport == UnknownReportLevel.Detailed;
        var reportText = report.GenerateReport(includeDetailedList);
        
        // Output to console directly for visibility
        Console.WriteLine();
        Console.WriteLine(reportText);
    }
}
