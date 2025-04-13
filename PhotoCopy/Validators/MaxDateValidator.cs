using PhotoCopy.Files;
using System;

namespace PhotoCopy.Validators;

internal class MaxDateValidator(Options options) : IValidator
{
    private readonly DateTime _date = options.MaxDate.Value;

    public bool Validate(IFile file)
    {
        return file.FileDateTime.DateTime <= _date;
    }
}
