using System;

namespace PhotoCopy.Progress;

/// <summary>
/// A no-op progress reporter for when progress reporting is disabled.
/// </summary>
public class NullProgressReporter : IProgressReporter
{
    public static readonly NullProgressReporter Instance = new();

    private NullProgressReporter() { }

    public void Report(CopyProgress progress) { }

    public void Complete(CopyProgress finalProgress) { }

    public void ReportError(string fileName, Exception exception) { }
}
