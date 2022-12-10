using System.Collections.Generic;

namespace PhotoCopy.Validators
{
    internal static class ValidatorFactory
    {

        public static IReadOnlyCollection<IValidator> Create(Options options)
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
}
