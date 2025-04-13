using PhotoCopy.Validators;
using System.Collections.Generic;

namespace PhotoCopy.Directories
{
    internal interface IDirectoryCopier
    {
        void Copy(Options options, IReadOnlyCollection<IValidator> validators);
    }
}