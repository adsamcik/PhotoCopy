using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using PhotoCopy.Configuration;

namespace PhotoCopy.Validators;

public class ValidatorFactory : IValidatorFactory
{
    private readonly ILogger _logger;

    public ValidatorFactory(ILogger<ValidatorFactory> logger)
    {
        _logger = logger;
    }

    public IReadOnlyCollection<IValidator> Create(PhotoCopyConfig config)
    {
        var validators = new List<IValidator>();

        if (config.MaxDate.HasValue)
        {
            _logger.LogDebug("Creating MaxDateValidator with date {MaxDate}", config.MaxDate.Value);
            validators.Add(new MaxDateValidator(config.MaxDate.Value));
        }

        if (config.MinDate.HasValue)
        {
            _logger.LogDebug("Creating MinDateValidator with date {MinDate}", config.MinDate.Value);
            validators.Add(new MinDateValidator(config.MinDate.Value));
        }

        if (config.ExcludePatterns.Count > 0)
        {
            _logger.LogDebug("Creating ExcludePatternMatcher with {Count} patterns: {Patterns}", 
                config.ExcludePatterns.Count, 
                string.Join(", ", config.ExcludePatterns));
            validators.Add(new ExcludePatternMatcher(config.ExcludePatterns, config.Source));
        }

        return validators;
    }
}
