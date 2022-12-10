using PhotoCopy.Files;
using System;

namespace PhotoCopy.Validators
{
    internal class MinDateValidator : IValidator
    {
        private readonly DateTime _date;

        public MinDateValidator(Options options)
        {
            _date = options.MinDate.Value;
        }

        public bool Validate(IFile file)
        {
            return file.FileDateTime.DateTime >= _date;
        }
    }
}
