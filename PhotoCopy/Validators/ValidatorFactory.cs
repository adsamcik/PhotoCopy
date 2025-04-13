using System.Collections.Generic;

namespace PhotoCopy.Validators;

internal class ValidatorFactory : IValidatorFactory
{

    public IReadOnlyCollection<IValidator> Create(Options options)
    {
        var filters = new List<IValidator>();
        if (options.MaxDate.HasValue)
        {
            filters.Add(new MaxDateValidator(options));
        }

        if (options.MinDate.HasValue)
        {
            filters.Add(new MinDateValidator(options));
        }

        return filters;
    }
}
