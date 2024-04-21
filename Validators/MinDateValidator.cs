using PhotoCopy.Files;
using System;

namespace PhotoCopy.Validators;

internal class MinDateValidator(Options options) : IValidator
{
    private readonly DateTime _date = options.MinDate.Value;

    public bool Validate(IFile file)
    {
        return file.FileDateTime.DateTime >= _date;
    }
}
