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

    #region IsPathWithinBounds Tests

    [Test]
    public async Task IsPathWithinBounds_PathWithinRoot_ReturnsTrue()
    {
        var root = @"C:\Photos";
        var path = @"C:\Photos\2024\01\photo.jpg";
        
        var result = PathSecurityHelper.IsPathWithinBounds(path, root);
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task IsPathWithinBounds_PathEqualsRoot_ReturnsTrue()
    {
        var root = @"C:\Photos";
        var path = @"C:\Photos";
        
        var result = PathSecurityHelper.IsPathWithinBounds(path, root);
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task IsPathWithinBounds_PathOutsideRoot_ReturnsFalse()
    {
        var root = @"C:\Photos";
        var path = @"C:\Documents\secret.txt";
        
        var result = PathSecurityHelper.IsPathWithinBounds(path, root);
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task IsPathWithinBounds_PathWithTraversalEscapingRoot_ReturnsFalse()
    {
        var root = @"C:\Photos";
        // Path.GetFullPath will resolve this to C:\secret.txt
        var path = @"C:\Photos\..\secret.txt";
        
        var result = PathSecurityHelper.IsPathWithinBounds(path, root);
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task IsPathWithinBounds_SimilarPrefixDifferentDirectory_ReturnsFalse()
    {
        var root = @"C:\Photos";
        // This should NOT match because "PhotosEvil" is not the same as "Photos"
        var path = @"C:\PhotosEvil\hack.jpg";
        
        var result = PathSecurityHelper.IsPathWithinBounds(path, root);
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task IsPathWithinBounds_NullPath_ReturnsFalse()
    {
        var result = PathSecurityHelper.IsPathWithinBounds(null!, @"C:\Photos");
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task IsPathWithinBounds_NullRoot_ReturnsFalse()
    {
        var result = PathSecurityHelper.IsPathWithinBounds(@"C:\Photos\photo.jpg", null!);
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task IsPathWithinBounds_EmptyPath_ReturnsFalse()
    {
        var result = PathSecurityHelper.IsPathWithinBounds(string.Empty, @"C:\Photos");
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task IsPathWithinBounds_RootWithTrailingSeparator_WorksCorrectly()
    {
        var root = @"C:\Photos\";
        var path = @"C:\Photos\2024\photo.jpg";
        
        var result = PathSecurityHelper.IsPathWithinBounds(path, root);
        await Assert.That(result).IsTrue();
    }

    #endregion

    #region ValidateGeneratedPath Tests

    [Test]
    public async Task ValidateGeneratedPath_SafePath_ReturnsValid()
    {
        var path = @"C:\Photos\2024\01\photo.jpg";
        var root = @"C:\Photos";
        
        var (isValid, errorMessage) = PathSecurityHelper.ValidateGeneratedPath(path, root);
        
        await Assert.That(isValid).IsTrue();
        await Assert.That(errorMessage).IsNull();
    }

    [Test]
    public async Task ValidateGeneratedPath_PathWithTraversal_ReturnsInvalid()
    {
        // Path with ".." as a complete segment (actual path traversal)
        var path = @"C:\Photos\..\secret.txt";
        var root = @"C:\Photos";
        
        var (isValid, errorMessage) = PathSecurityHelper.ValidateGeneratedPath(path, root);
        
        await Assert.That(isValid).IsFalse();
        await Assert.That(errorMessage).IsNotNull();
        await Assert.That(errorMessage!).Contains("..");
    }

    [Test]
    public async Task ValidateGeneratedPath_PathWithDotsInFilename_ReturnsValid()
    {
        // Path with ".." as part of a filename (not traversal)
        var path = @"C:\Photos\City..Name\photo.jpg";
        var root = @"C:\Photos";
        
        var (isValid, errorMessage) = PathSecurityHelper.ValidateGeneratedPath(path, root);
        
        // This should be valid - ".." is part of the directory name, not a traversal
        await Assert.That(isValid).IsTrue();
        await Assert.That(errorMessage).IsNull();
    }

    [Test]
    public async Task ValidateGeneratedPath_RelativePath_ReturnsInvalid()
    {
        var path = @"2024\01\photo.jpg";
        var root = @"C:\Photos";
        
        var (isValid, errorMessage) = PathSecurityHelper.ValidateGeneratedPath(path, root);
        
        await Assert.That(isValid).IsFalse();
        await Assert.That(errorMessage).IsNotNull();
        await Assert.That(errorMessage!).Contains("not an absolute path");
    }

    [Test]
    public async Task ValidateGeneratedPath_PathOutsideRoot_ReturnsInvalid()
    {
        var path = @"C:\Documents\photo.jpg";
        var root = @"C:\Photos";
        
        var (isValid, errorMessage) = PathSecurityHelper.ValidateGeneratedPath(path, root);
        
        await Assert.That(isValid).IsFalse();
        await Assert.That(errorMessage).IsNotNull();
        await Assert.That(errorMessage!).Contains("escapes destination root");
    }

    [Test]
    public async Task ValidateGeneratedPath_EmptyPath_ReturnsInvalid()
    {
        var (isValid, errorMessage) = PathSecurityHelper.ValidateGeneratedPath(string.Empty, @"C:\Photos");
        
        await Assert.That(isValid).IsFalse();
        await Assert.That(errorMessage).IsNotNull();
    }

    [Test]
    public async Task ValidateGeneratedPath_EmptyRoot_ReturnsInvalid()
    {
        var (isValid, errorMessage) = PathSecurityHelper.ValidateGeneratedPath(@"C:\Photos\photo.jpg", string.Empty);
        
        await Assert.That(isValid).IsFalse();
        await Assert.That(errorMessage).IsNotNull();
    }

    #endregion

    #region ThrowIfPathUnsafe Tests

    [Test]
    public async Task ThrowIfPathUnsafe_SafePath_DoesNotThrow()
    {
        var path = @"C:\Photos\2024\01\photo.jpg";
        var root = @"C:\Photos";
        
        Exception? exception = null;
        try
        {
            PathSecurityHelper.ThrowIfPathUnsafe(path, root);
        }
        catch (Exception ex)
        {
            exception = ex;
        }
        
        await Assert.That(exception).IsNull();
    }

    [Test]
    public async Task ThrowIfPathUnsafe_UnsafePath_ThrowsInvalidOperationException()
    {
        var path = @"C:\Photos\..\secret.txt";
        var root = @"C:\Photos";
        
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
        {
            PathSecurityHelper.ThrowIfPathUnsafe(path, root);
            return Task.CompletedTask;
        });
    }

    [Test]
    public async Task ThrowIfPathUnsafe_PathOutsideRoot_ThrowsInvalidOperationException()
    {
        var path = @"C:\Documents\photo.jpg";
        var root = @"C:\Photos";
        
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
        {
            PathSecurityHelper.ThrowIfPathUnsafe(path, root);
            return Task.CompletedTask;
        });
    }

    #endregion

    #region ExtractDestinationRoot Tests

    [Test]
    public async Task ExtractDestinationRoot_PatternWithTrailingSeparatorBeforeVariable_ReturnsFullPath()
    {
        // Pattern with trailing separator before variable
        var pattern = @"C:\Photos\{year}\{month}\{name}{ext}";
        
        var result = PathSecurityHelper.ExtractDestinationRoot(pattern);
        
        // The function extracts everything before the first variable
        // Since there's a trailing \ before {year}, we get C:\Photos
        await Assert.That(result).IsEqualTo(Path.GetFullPath(@"C:\Photos"));
    }

    [Test]
    public async Task ExtractDestinationRoot_PatternWithoutTrailingSeparator_ReturnsParentDirectory()
    {
        // Pattern without trailing separator - like "C:\Dest\photo{ext}"
        var pattern = @"C:\Dest\photo{ext}";
        
        var result = PathSecurityHelper.ExtractDestinationRoot(pattern);
        
        // Should return C:\Dest since "photo" is a file prefix, not a directory
        await Assert.That(result).IsEqualTo(Path.GetFullPath(@"C:\Dest"));
    }

    [Test]
    public async Task ExtractDestinationRoot_PatternStartingWithVariable_ReturnsCurrentDirectory()
    {
        var pattern = @"{year}\{month}\{name}{ext}";
        
        var result = PathSecurityHelper.ExtractDestinationRoot(pattern);
        
        await Assert.That(result).IsEqualTo(Directory.GetCurrentDirectory());
    }

    [Test]
    public async Task ExtractDestinationRoot_PatternWithNoVariables_ReturnsFullPath()
    {
        var pattern = @"C:\Photos\backup";
        
        var result = PathSecurityHelper.ExtractDestinationRoot(pattern);
        
        await Assert.That(result).IsEqualTo(Path.GetFullPath(@"C:\Photos\backup"));
    }

    [Test]
    public async Task ExtractDestinationRoot_EmptyPattern_ReturnsCurrentDirectory()
    {
        var result = PathSecurityHelper.ExtractDestinationRoot(string.Empty);
        
        await Assert.That(result).IsEqualTo(Directory.GetCurrentDirectory());
    }

    [Test]
    public async Task ExtractDestinationRoot_NullPattern_ReturnsCurrentDirectory()
    {
        var result = PathSecurityHelper.ExtractDestinationRoot(null!);
        
        await Assert.That(result).IsEqualTo(Directory.GetCurrentDirectory());
    }

    [Test]
    public async Task ExtractDestinationRoot_DeepNestedPattern_ReturnsCorrectRoot()
    {
        var pattern = @"D:\Archive\Photos\Sorted\{year}\{month}\{day}\{city}\{name}{ext}";
        
        var result = PathSecurityHelper.ExtractDestinationRoot(pattern);
        
        await Assert.That(result).IsEqualTo(Path.GetFullPath(@"D:\Archive\Photos\Sorted"));
    }

    #endregion
}
