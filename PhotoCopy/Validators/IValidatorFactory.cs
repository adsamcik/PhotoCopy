using System.Collections.Generic;
using PhotoCopy.Configuration;
using PhotoCopy.Validators;

namespace PhotoCopy.Validators;

public interface IValidatorFactory
{
    IReadOnlyCollection<IValidator> Create(PhotoCopyConfig config);
}