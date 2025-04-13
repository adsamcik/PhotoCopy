using System.Collections.Generic;

namespace PhotoCopy.Validators
{
    internal interface IValidatorFactory
    {
        IReadOnlyCollection<IValidator> Create(Options options);
    }
}