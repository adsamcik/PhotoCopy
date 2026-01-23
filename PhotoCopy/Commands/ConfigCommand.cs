using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PhotoCopy;
using PhotoCopy.Configuration;

namespace PhotoCopy.Commands;

/// <summary>
/// Command for showing configuration diagnostics.
/// </summary>
public class ConfigCommand : ICommand
{
    private readonly ILogger<ConfigCommand> _logger;
    private readonly PhotoCopyConfig _config;
    private readonly ConfigurationDiagnostics _diagnostics;
    private readonly bool _outputJson;

    public ConfigCommand(
        ILogger<ConfigCommand> logger,
        IOptions<PhotoCopyConfig> options,
        ConfigurationDiagnostics diagnostics,
        bool outputJson = false)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(diagnostics);

        _logger = logger;
        _config = options.Value;
        _diagnostics = diagnostics;
        _outputJson = outputJson;
    }

    public Task<int> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var report = _diagnostics.GenerateReport(_config);

            if (_outputJson)
            {
                Console.WriteLine(report.ToJson());
            }
            else
            {
                report.PrintToConsole();
            }

            return Task.FromResult((int)ExitCode.Success);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate configuration diagnostics");
            return Task.FromResult((int)ExitCode.Error);
        }
    }
}
