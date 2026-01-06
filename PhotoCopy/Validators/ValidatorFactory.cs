using System.Collections.Generic;
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

        return validators;
    }
}
