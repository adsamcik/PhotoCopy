using System;
using System.IO;
using AwesomeAssertions;
using PhotoCopy.Extensions;

namespace PhotoCopy.Tests.Extensions;

/// <summary>
/// Unit tests for PathSecurityHelper class.
/// </summary>
public class PathSecurityHelperTests
{
    #region IsReparsePoint Tests

    [Test]
    public async Task IsReparsePoint_NullPath_ReturnsFalse()
    {
        var result = PathSecurityHelper.IsReparsePoint(null!);
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task IsReparsePoint_EmptyPath_ReturnsFalse()
    {
        var result = PathSecurityHelper.IsReparsePoint(string.Empty);
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task IsReparsePoint_WhitespacePath_ReturnsFalse()
    {
        var result = PathSecurityHelper.IsReparsePoint("   ");
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task IsReparsePoint_NonExistentPath_ReturnsFalse()
    {
        var result = PathSecurityHelper.IsReparsePoint(@"C:\NonExistent\Path\That\Does\Not\Exist");
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task IsReparsePoint_RegularDirectory_ReturnsFalse()
    {
        // Use the temp directory which should not be a reparse point
        var tempPath = Path.GetTempPath();
        var result = PathSecurityHelper.IsReparsePoint(tempPath);
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task IsReparsePoint_RegularFile_ReturnsFalse()
    {
        // Create a temporary file
        var tempFile = Path.GetTempFileName();
        try
        {
            var result = PathSecurityHelper.IsReparsePoint(tempFile);
            await Assert.That(result).IsFalse();
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    #endregion

    #region IsPathSafe Tests

    [Test]
    public async Task IsPathSafe_NullPath_ReturnsFalse()
    {
        var result = PathSecurityHelper.IsPathSafe(null!);
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task IsPathSafe_EmptyPath_ReturnsFalse()
    {
        var result = PathSecurityHelper.IsPathSafe(string.Empty);
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task IsPathSafe_PathWithTraversal_ReturnsFalse()
    {
        var result = PathSecurityHelper.IsPathSafe(@"C:\temp\..\secret\file.txt");
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task IsPathSafe_RelativePath_ReturnsFalse()
    {
        var result = PathSecurityHelper.IsPathSafe(@"relative\path\file.txt");
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task IsPathSafe_ValidAbsolutePath_ReturnsTrue()
    {
        var result = PathSecurityHelper.IsPathSafe(@"C:\temp\valid\path\file.txt");
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task IsPathSafe_ValidUnixPath_ReturnsTrue()
    {
        // On Windows, Unix paths may not be rooted, but let's test the logic
        var path = "/home/user/file.txt";
        var result = PathSecurityHelper.IsPathSafe(path);
        
        // Path.IsPathRooted returns true for Unix-style absolute paths
        await Assert.That(result).IsTrue();
    }

    #endregion

    #region ThrowIfReparsePoint Tests

    [Test]
    public async Task ThrowIfReparsePoint_NullPath_DoesNotThrow()
    {
        // Act - should not throw
        Exception? exception = null;
        try
        {
            PathSecurityHelper.ThrowIfReparsePoint(null!);
        }
        catch (Exception ex)
        {
            exception = ex;
        }

        await Assert.That(exception).IsNull();
    }

    [Test]
    public async Task ThrowIfReparsePoint_EmptyPath_DoesNotThrow()
    {
        Exception? exception = null;
        try
        {
            PathSecurityHelper.ThrowIfReparsePoint(string.Empty);
        }
        catch (Exception ex)
        {
            exception = ex;
        }

        await Assert.That(exception).IsNull();
    }

    [Test]
    public async Task ThrowIfReparsePoint_NonExistentPath_DoesNotThrow()
    {
        Exception? exception = null;
        try
        {
            PathSecurityHelper.ThrowIfReparsePoint(@"C:\NonExistent\Path");
        }
        catch (Exception ex)
        {
            exception = ex;
        }

        await Assert.That(exception).IsNull();
    }

    [Test]
    public async Task ThrowIfReparsePoint_RegularFile_DoesNotThrow()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            Exception? exception = null;
            try
            {
                PathSecurityHelper.ThrowIfReparsePoint(tempFile);
            }
            catch (Exception ex)
            {
                exception = ex;
            }

            await Assert.That(exception).IsNull();
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Test]
    public async Task ThrowIfReparsePoint_RegularDirectory_DoesNotThrow()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        try
        {
            Exception? exception = null;
            try
            {
                PathSecurityHelper.ThrowIfReparsePoint(tempDir);
            }
            catch (Exception ex)
            {
                exception = ex;
            }

            await Assert.That(exception).IsNull();
        }
        finally
        {
            Directory.Delete(tempDir);
        }
    }

    #endregion
}
