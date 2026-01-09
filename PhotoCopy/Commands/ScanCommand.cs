using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PhotoCopy;
using PhotoCopy.Abstractions;
using PhotoCopy.Configuration;
using PhotoCopy.Extensions;
using PhotoCopy.Files;
using PhotoCopy.Validators;

namespace PhotoCopy.Commands;

/// <summary>
/// Command for scanning source directory and displaying file information.
/// </summary>
public class ScanCommand : ICommand
{
    private readonly ILogger<ScanCommand> _logger;
    private readonly PhotoCopyConfig _config;
    private readonly IDirectoryScanner _directoryScanner;
    private readonly IValidatorFactory _validatorFactory;
    private readonly IFileValidationService _fileValidationService;
    private readonly IFileFactory _fileFactory;
    private readonly IFileSystem _fileSystem;
    private readonly bool _outputJson;

    public ScanCommand(
        ILogger<ScanCommand> logger,
        IOptions<PhotoCopyConfig> options,
        IDirectoryScanner directoryScanner,
        IValidatorFactory validatorFactory,
        IFileValidationService fileValidationService,
        IFileFactory fileFactory,
        IFileSystem fileSystem,
        bool outputJson = false)
    {
        _logger = logger;
        _config = options.Value;
        _directoryScanner = directoryScanner;
        _validatorFactory = validatorFactory;
        _fileValidationService = fileValidationService;
        _fileFactory = fileFactory;
        _fileSystem = fileSystem;
        _outputJson = outputJson;
    }

    public async Task<int> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Scanning {Source}...", _config.Source);

            var validators = _validatorFactory.Create(_config);
            var files = _fileSystem.EnumerateFiles(_config.Source);

            var stats = new ScanStatistics();
            var results = new System.Collections.Generic.List<FileScanResult>();

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var validationResult = _fileValidationService.ValidateFirstFailure(file, validators);
                var isValid = validationResult.IsValid;
                var rejectionReason = validationResult.FirstRejectionReason;

                stats.TotalFiles++;
                stats.TotalBytes += SafeFileLength(file);

                if (isValid)
                {
                    stats.ValidFiles++;
                }
                else
                {
                    stats.SkippedFiles++;
                }

                if (_outputJson)
                {
                    results.Add(new FileScanResult(
                        file.File.FullName,
                        file.File.Name,
                        SafeFileLength(file),
                        file.FileDateTime.DateTime,
                        isValid,
                        rejectionReason));
                }
                else
                {
                    var status = isValid ? "✓" : "✗";
                    var reason = rejectionReason is not null ? $" ({rejectionReason})" : "";
                    _logger.LogDebug("{Status} {File}{Reason}", status, file.File.Name, reason);
                }
            }

            await Task.CompletedTask; // Satisfy async signature

            if (_outputJson)
            {
                var output = new ScanOutput(stats, results);
                Console.WriteLine(JsonSerializer.Serialize(output, new JsonSerializerOptions
                {
                    WriteIndented = true
                }));
            }
            else
            {
                _logger.LogInformation(
                    "Scan complete: {Total} files, {Valid} valid, {Skipped} skipped, {Bytes} total",
                    stats.TotalFiles, stats.ValidFiles, stats.SkippedFiles, FormatBytes(stats.TotalBytes));
            }

            return (int)ExitCode.Success;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Scan was cancelled");
            return (int)ExitCode.Cancelled;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Scan operation failed");
            return (int)ExitCode.Error;
        }
    }

    private static long SafeFileLength(IFile file) => ByteFormatter.SafeFileLength(file);

    private static string FormatBytes(long bytes) => ByteFormatter.FormatBytes(bytes);

    private sealed record ScanStatistics
    {
        public int TotalFiles { get; set; }
        public int ValidFiles { get; set; }
        public int SkippedFiles { get; set; }
        public long TotalBytes { get; set; }
    }

    private sealed record FileScanResult(
        string FullPath,
        string FileName,
        long Size,
        DateTime? DateTime,
        bool IsValid,
        string? RejectionReason);

    private sealed record ScanOutput(
        ScanStatistics Statistics,
        System.Collections.Generic.List<FileScanResult> Files);
}
