using PhotoCopy.Files;
using PhotoCopy.Validators;
using System;
using System.IO;

namespace PhotoCopy.Tests.Validators;

public class ExcludePatternMatcherTests
{
    private readonly string _testSourceRoot;

    public ExcludePatternMatcherTests()
    {
        _testSourceRoot = Path.Combine(Path.GetTempPath(), "PhotoCopyTests", "source");
    }

    /// <summary>
    /// Simple test implementation of IFile for testing purposes.
    /// </summary>
    private class TestFile : IFile
    {
        private readonly FileInfo _fileInfo;

        public TestFile(string fullPath)
        {
            _fileInfo = new FileInfo(fullPath);
        }

        public FileInfo File => _fileInfo;
        public FileDateTime FileDateTime => new(DateTime.Now, DateTime.Now, DateTime.Now);
        public LocationData? Location => null;
        public string Checksum => string.Empty;
        public UnknownFileReason UnknownReason => UnknownFileReason.None;
        public string? Camera => null;
        public string? Album => null;
    }

    private IFile CreateMockFile(string relativePath)
    {
        var fullPath = Path.Combine(_testSourceRoot, relativePath);
        return new TestFile(fullPath);
    }

    #region Basic Pattern Matching Tests

    [Test]
    public async Task Validate_ReturnsSuccess_WhenNoPatterns()
    {
        // Arrange
        var matcher = new ExcludePatternMatcher([], _testSourceRoot);
        var file = CreateMockFile("photo.jpg");

        // Act
        var result = matcher.Validate(file);

        // Assert
        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    public async Task Validate_ReturnsSuccess_WhenFileDoesNotMatchPattern()
    {
        // Arrange
        var matcher = new ExcludePatternMatcher(["*.aae"], _testSourceRoot);
        var file = CreateMockFile("photo.jpg");

        // Act
        var result = matcher.Validate(file);

        // Assert
        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    public async Task Validate_ReturnsFail_WhenFileMatchesExtensionPattern()
    {
        // Arrange
        var matcher = new ExcludePatternMatcher(["*.aae"], _testSourceRoot);
        var file = CreateMockFile("IMG_1234.aae");

        // Act
        var result = matcher.Validate(file);

        // Assert
        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.ValidatorName).IsEqualTo(nameof(ExcludePatternMatcher));
        await Assert.That(result.Reason).Contains("*.aae");
    }

    [Test]
    public async Task Validate_ReturnsFail_WhenFileMatchesWildcardPattern()
    {
        // Arrange
        var matcher = new ExcludePatternMatcher(["*_thumb*"], _testSourceRoot);
        var file = CreateMockFile("photo_thumb.jpg");

        // Act
        var result = matcher.Validate(file);

        // Assert
        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Reason).Contains("*_thumb*");
    }

    [Test]
    public async Task Validate_ReturnsFail_WhenFileMatchesPrefixPattern()
    {
        // Arrange
        var matcher = new ExcludePatternMatcher([".trashed-*"], _testSourceRoot);
        var file = CreateMockFile(".trashed-12345");

        // Act
        var result = matcher.Validate(file);

        // Assert
        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Reason).Contains(".trashed-*");
    }

    #endregion

    #region Multiple Pattern Tests

    [Test]
    public async Task Validate_ReturnsFail_WhenFileMatchesAnyPattern()
    {
        // Arrange
        var patterns = new[] { "*.aae", "*_thumb*", ".trashed-*" };
        var matcher = new ExcludePatternMatcher(patterns, _testSourceRoot);
        var file = CreateMockFile("photo_thumb.jpg");

        // Act
        var result = matcher.Validate(file);

        // Assert
        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Reason).Contains("*_thumb*");
    }

    [Test]
    public async Task Validate_ReturnsSuccess_WhenFileMatchesNoPatterns()
    {
        // Arrange
        var patterns = new[] { "*.aae", "*_thumb*", ".trashed-*" };
        var matcher = new ExcludePatternMatcher(patterns, _testSourceRoot);
        var file = CreateMockFile("normal_photo.jpg");

        // Act
        var result = matcher.Validate(file);

        // Assert
        await Assert.That(result.IsValid).IsTrue();
    }

    #endregion

    #region Path-based Pattern Tests

    [Test]
    public async Task Validate_MatchesFilesInSubdirectories()
    {
        // Arrange
        var matcher = new ExcludePatternMatcher(["**/*.aae"], _testSourceRoot);
        var file = CreateMockFile(Path.Combine("2024", "January", "IMG_1234.aae"));

        // Act
        var result = matcher.Validate(file);

        // Assert
        await Assert.That(result.IsValid).IsFalse();
    }

    [Test]
    public async Task Validate_MatchesSpecificDirectory()
    {
        // Arrange
        var matcher = new ExcludePatternMatcher(["thumbnails/**"], _testSourceRoot);
        var file = CreateMockFile(Path.Combine("thumbnails", "photo.jpg"));

        // Act
        var result = matcher.Validate(file);

        // Assert
        await Assert.That(result.IsValid).IsFalse();
    }

    [Test]
    public async Task Validate_DoesNotMatchDifferentDirectory()
    {
        // Arrange
        var matcher = new ExcludePatternMatcher(["thumbnails/**"], _testSourceRoot);
        var file = CreateMockFile(Path.Combine("photos", "photo.jpg"));

        // Act
        var result = matcher.Validate(file);

        // Assert
        await Assert.That(result.IsValid).IsTrue();
    }

    #endregion

    #region Case Insensitivity Tests

    [Test]
    public async Task Validate_IsCaseInsensitive()
    {
        // Arrange
        var matcher = new ExcludePatternMatcher(["*.AAE"], _testSourceRoot);
        var file = CreateMockFile("photo.aae");

        // Act
        var result = matcher.Validate(file);

        // Assert
        await Assert.That(result.IsValid).IsFalse();
    }

    [Test]
    public async Task Validate_IsCaseInsensitiveForPatternLowerFilenameUpper()
    {
        // Arrange
        var matcher = new ExcludePatternMatcher(["*.aae"], _testSourceRoot);
        var file = CreateMockFile("PHOTO.AAE");

        // Act
        var result = matcher.Validate(file);

        // Assert
        await Assert.That(result.IsValid).IsFalse();
    }

    #endregion

    #region Real-world Pattern Tests

    [Test]
    public async Task Validate_MatchesiPhoneAaeFiles()
    {
        // Arrange
        var matcher = new ExcludePatternMatcher(["*.aae"], _testSourceRoot);
        var file = CreateMockFile("IMG_1234.AAE");

        // Act
        var result = matcher.Validate(file);

        // Assert
        await Assert.That(result.IsValid).IsFalse();
    }

    [Test]
    public async Task Validate_MatchesThumbnailFiles()
    {
        // Arrange
        var matcher = new ExcludePatternMatcher(["*_thumb*", "*_tn*"], _testSourceRoot);
        
        var thumbFile = CreateMockFile("photo_thumb.jpg");
        var tnFile = CreateMockFile("photo_tn.jpg");
        var normalFile = CreateMockFile("photo.jpg");

        // Act & Assert
        await Assert.That(matcher.Validate(thumbFile).IsValid).IsFalse();
        await Assert.That(matcher.Validate(tnFile).IsValid).IsFalse();
        await Assert.That(matcher.Validate(normalFile).IsValid).IsTrue();
    }

    [Test]
    public async Task Validate_MatchesTrashedFiles()
    {
        // Arrange
        var matcher = new ExcludePatternMatcher([".trashed-*", ".Trashes/**"], _testSourceRoot);
        var trashedFile = CreateMockFile(".trashed-12345-IMG_1234.jpg");

        // Act
        var result = matcher.Validate(trashedFile);

        // Assert
        await Assert.That(result.IsValid).IsFalse();
    }

    [Test]
    public async Task Validate_MatchesTempFiles()
    {
        // Arrange
        var matcher = new ExcludePatternMatcher(["*.tmp", "~*", ".*~"], _testSourceRoot);
        
        var tmpFile = CreateMockFile("photo.tmp");
        var tildePrefix = CreateMockFile("~tempfile.jpg");

        // Act & Assert
        await Assert.That(matcher.Validate(tmpFile).IsValid).IsFalse();
        await Assert.That(matcher.Validate(tildePrefix).IsValid).IsFalse();
    }

    [Test]
    public async Task Validate_MatchesDsStoreFiles()
    {
        // Arrange
        var matcher = new ExcludePatternMatcher([".DS_Store", "Thumbs.db"], _testSourceRoot);
        
        var dsStore = CreateMockFile(".DS_Store");
        var thumbsDb = CreateMockFile("Thumbs.db");

        // Act & Assert
        await Assert.That(matcher.Validate(dsStore).IsValid).IsFalse();
        await Assert.That(matcher.Validate(thumbsDb).IsValid).IsFalse();
    }

    #endregion

    #region Name Property Test

    [Test]
    public async Task Name_ReturnsCorrectValidatorName()
    {
        // Arrange
        var matcher = new ExcludePatternMatcher(["*.aae"], _testSourceRoot);

        // Act
        var name = matcher.Name;

        // Assert
        await Assert.That(name).IsEqualTo(nameof(ExcludePatternMatcher));
    }

    #endregion
}
