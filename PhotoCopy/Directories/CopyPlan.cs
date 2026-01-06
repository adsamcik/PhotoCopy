using System.Collections.Generic;
using PhotoCopy.Files;

namespace PhotoCopy.Directories;

public sealed record ValidationFailure(IFile File, string ValidatorName, string? Reason);

public sealed record RelatedFilePlan(IFile File, string DestinationPath);

public sealed record FileCopyPlan(IFile File, string DestinationPath, IReadOnlyCollection<RelatedFilePlan> RelatedFiles);

public sealed class CopyPlan
{
    public CopyPlan(
        IReadOnlyList<FileCopyPlan> operations,
        IReadOnlyList<ValidationFailure> skipped,
        IReadOnlyCollection<string> directories,
        long totalBytes)
    {
        Operations = operations;
        SkippedFiles = skipped;
        DirectoriesToCreate = directories;
        TotalBytes = totalBytes;
    }

    public IReadOnlyList<FileCopyPlan> Operations { get; }

    public IReadOnlyList<ValidationFailure> SkippedFiles { get; }

    public IReadOnlyCollection<string> DirectoriesToCreate { get; }

    public long TotalBytes { get; }
}