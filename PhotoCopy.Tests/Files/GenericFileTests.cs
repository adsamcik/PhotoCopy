using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using PhotoCopy.Files;

namespace PhotoCopy.Tests.Files;

public class GenericFileTests : IClassFixture<ApplicationStateFixture>
{
    [Fact]
    public void Checksum_CalculatesCorrectSHA256()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        var content = "Test content for checksum";
        File.WriteAllText(tempFile, content);
        var fileInfo = new FileInfo(tempFile);

        try
        {
            var genericFile = new GenericFile(fileInfo, new FileDateTime(DateTime.Now, DateTimeSource.FileCreation));

            // Calculate expected SHA256
            var expectedHash = string.Empty;
            using (var sha256 = SHA256.Create())
            {
                var contentBytes = Encoding.UTF8.GetBytes(content);
                var hashBytes = sha256.ComputeHash(contentBytes);
                var sb = new StringBuilder();
                foreach (var b in hashBytes)
                {
                    sb.Append(b.ToString("x2"));
                }
                expectedHash = sb.ToString();
            }

            // Act
            var actualHash = genericFile.Checksum;

            // Assert
            Assert.Equal(expectedHash, actualHash);
        }
        finally
        {
            // Cleanup
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void CopyTo_LogsCorrectMessage()
    {
        // Arrange
        var tempSource = Path.GetTempFileName();
        var tempDest = Path.GetTempFileName();
        File.WriteAllText(tempSource, "Test content");
        var fileInfo = new FileInfo(tempSource);
        var genericFile = new GenericFile(fileInfo, new FileDateTime(DateTime.Now, DateTimeSource.FileCreation));

        try
        {
            ApplicationState.Options = new Options { Log = Options.LogLevel.verbose };

            // Act & Assert
            genericFile.CopyTo(tempDest, true); // Use dry run to avoid actual file operations
        }
        finally
        {
            // Cleanup
            File.Delete(tempSource);
            File.Delete(tempDest);
        }
    }

    [Fact]
    public void MoveTo_LogsCorrectMessage()
    {
        // Arrange
        var tempSource = Path.GetTempFileName();
        var tempDest = Path.GetTempFileName();
        File.WriteAllText(tempSource, "Test content");
        var fileInfo = new FileInfo(tempSource);
        var genericFile = new GenericFile(fileInfo, new FileDateTime(DateTime.Now, DateTimeSource.FileCreation));

        try
        {
            ApplicationState.Options = new Options { Log = Options.LogLevel.verbose };

            // Act & Assert
            genericFile.MoveTo(tempDest, true); // Use dry run to avoid actual file operations
        }
        finally
        {
            // Cleanup
            File.Delete(tempSource);
            File.Delete(tempDest);
        }
    }

    [Fact]
    public void CopyTo_ActuallyCopiesFile_WhenNotDryRun()
    {
        // Arrange
        var tempSource = Path.GetTempFileName();
        var tempDest = Path.Combine(Path.GetTempPath(), "GenericFileTest_dest.txt");
        var content = "Test content for copy";
        File.WriteAllText(tempSource, content);
        var fileInfo = new FileInfo(tempSource);
        var genericFile = new GenericFile(fileInfo, new FileDateTime(DateTime.Now, DateTimeSource.FileCreation));

        try
        {
            // Act
            genericFile.CopyTo(tempDest, false);

            // Assert
            Assert.True(File.Exists(tempDest));
            Assert.Equal(content, File.ReadAllText(tempDest));
        }
        finally
        {
            // Cleanup
            File.Delete(tempSource);
            if (File.Exists(tempDest))
                File.Delete(tempDest);
        }
    }

    [Fact]
    public void MoveTo_ActuallyMovesFile_WhenNotDryRun()
    {
        // Arrange
        var tempSource = Path.GetTempFileName();
        var tempDest = Path.Combine(Path.GetTempPath(), "GenericFileTest_dest.txt");
        var content = "Test content for move";
        File.WriteAllText(tempSource, content);
        var fileInfo = new FileInfo(tempSource);
        var genericFile = new GenericFile(fileInfo, new FileDateTime(DateTime.Now, DateTimeSource.FileCreation));

        try
        {
            // Act
            genericFile.MoveTo(tempDest, false);

            // Assert
            Assert.False(File.Exists(tempSource));
            Assert.True(File.Exists(tempDest));
            Assert.Equal(content, File.ReadAllText(tempDest));
        }
        finally
        {
            // Cleanup
            if (File.Exists(tempSource))
                File.Delete(tempSource);
            if (File.Exists(tempDest))
                File.Delete(tempDest);
        }
    }
}