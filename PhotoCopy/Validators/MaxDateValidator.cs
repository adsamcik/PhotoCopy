using PhotoCopy.Files;
using System;

namespace PhotoCopy.Validators;

internal class MaxDateValidator(DateTime date) : IValidator
{
    private readonly DateTime _date = date;

    public string Name => nameof(MaxDateValidator);

    public ValidationResult Validate(IFile file)
    {
        if (file.FileDateTime.DateTime <= _date)
        {
            return ValidationResult.Success(Name);
        }

        var reason = $"File date {file.FileDateTime.DateTime:u} exceeds configured max {_date:u}";
        return ValidationResult.Fail(Name, reason);
    }
}
