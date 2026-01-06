namespace PhotoCopy.Configuration;

public enum OperationMode
{
    None,
    Copy,
    Move
}

public enum OutputLevel
{
    Verbose,
    Important,
    ErrorsOnly
}

public enum RelatedFileLookup
{
    None,
    Strict,
    Loose
}
