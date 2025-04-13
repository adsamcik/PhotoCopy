using NSubstitute;
using PhotoCopy.Abstractions;
using PhotoCopy.Files;

namespace PhotoCopy.Tests.Abstractions;

public class FileOperationTests
{
    private readonly FileOperation _fileOperation;

    public FileOperationTests()
    {
        _fileOperation = new FileOperation();
    }

    [Fact]
    public void MoveFile_CallsMoveTo_OnFile()
    {
        // Arrange
        var file = Substitute.For<IFile>();
        var destination = "destination/path";
        var dryRun = false;

        // Act
        _fileOperation.MoveFile(file, destination, dryRun);

        // Assert
        file.Received(1).MoveTo(destination, dryRun);
    }

    [Fact]
    public void CopyFile_CallsCopyTo_OnFile()
    {
        // Arrange
        var file = Substitute.For<IFile>();
        var destination = "destination/path";
        var dryRun = false;

        // Act
        _fileOperation.CopyFile(file, destination, dryRun);

        // Assert
        file.Received(1).CopyTo(destination, dryRun);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MoveFile_RespectsIsDryRun(bool isDryRun)
    {
        // Arrange
        var file = Substitute.For<IFile>();
        var destination = "destination/path";

        // Act
        _fileOperation.MoveFile(file, destination, isDryRun);

        // Assert
        file.Received(1).MoveTo(destination, isDryRun);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CopyFile_RespectsIsDryRun(bool isDryRun)
    {
        // Arrange
        var file = Substitute.For<IFile>();
        var destination = "destination/path";

        // Act
        _fileOperation.CopyFile(file, destination, isDryRun);

        // Assert
        file.Received(1).CopyTo(destination, isDryRun);
    }
}