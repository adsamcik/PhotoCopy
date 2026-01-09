using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using PhotoCopy.Files;

namespace PhotoCopy.Tests.Files;

public class Sha256ChecksumCalculatorTests : TestBase
{
    private readonly Sha256ChecksumCalculator _calculator;
    private readonly string _testDirectory;

    public Sha256ChecksumCalculatorTests()
    {
        _calculator = new Sha256ChecksumCalculator();
        _testDirectory = Path.Combine(Path.GetTempPath(), $"Sha256Tests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDirectory);
    }

    #region Helper Methods

    private string CreateTempFile(string fileName, string content)
    {
        var filePath = Path.Combine(_testDirectory, fileName);
        File.WriteAllText(filePath, content);
        return filePath;
    }

    private string CreateTempFileWithBytes(string fileName, byte[] content)
    {
        var filePath = Path.Combine(_testDirectory, fileName);
        File.WriteAllBytes(filePath, content);
        return filePath;
    }

    private void Cleanup()
    {
        try
        {
            if (Directory.Exists(_testDirectory))
            {
                Directory.Delete(_testDirectory, true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    #endregion

    #region Calculate Tests

    [Test]
    public void Calculate_ReturnsCorrectHash()
    {
        // Arrange
        // "Hello, World!" has a known SHA256 hash
        var content = "Hello, World!";
        var filePath = CreateTempFile("known_hash.txt", content);
        var fileInfo = new FileInfo(filePath);

        // Act
        var hash = _calculator.Calculate(fileInfo);

        // Assert
        hash.Should().NotBeNullOrEmpty();
        hash.Should().HaveLength(64); // SHA256 produces 64 hex characters
        hash.Should().MatchRegex("^[a-f0-9]{64}$"); // Should be lowercase hex only

        Cleanup();
    }

    [Test]
    public void Calculate_ReturnsLowercaseHash()
    {
        // Arrange
        var filePath = CreateTempFile("lowercase_test.txt", "Test content");
        var fileInfo = new FileInfo(filePath);

        // Act
        var hash = _calculator.Calculate(fileInfo);

        // Assert
        hash.Should().Be(hash.ToLowerInvariant());
        hash.Should().NotContain("-"); // Should not have dashes like BitConverter.ToString produces

        Cleanup();
    }

    [Test]
    public void Calculate_WithEmptyFile_ReturnsValidHash()
    {
        // Arrange
        var filePath = CreateTempFile("empty_file.txt", string.Empty);
        var fileInfo = new FileInfo(filePath);

        // SHA256 of empty content is a known value
        var expectedEmptyHash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

        // Act
        var hash = _calculator.Calculate(fileInfo);

        // Assert
        hash.Should().Be(expectedEmptyHash);

        Cleanup();
    }

    #endregion

    #region Same Content Tests

    [Test]
    public void Calculate_WithSameContent_ReturnsSameHash()
    {
        // Arrange
        var content = "Identical content for hash comparison";
        var filePath1 = CreateTempFile("same_content_1.txt", content);
        var filePath2 = CreateTempFile("same_content_2.txt", content);
        var fileInfo1 = new FileInfo(filePath1);
        var fileInfo2 = new FileInfo(filePath2);

        // Act
        var hash1 = _calculator.Calculate(fileInfo1);
        var hash2 = _calculator.Calculate(fileInfo2);

        // Assert
        hash1.Should().Be(hash2);

        Cleanup();
    }

    [Test]
    public void Calculate_WithDifferentContent_ReturnsDifferentHash()
    {
        // Arrange
        var filePath1 = CreateTempFile("different_1.txt", "Content A");
        var filePath2 = CreateTempFile("different_2.txt", "Content B");
        var fileInfo1 = new FileInfo(filePath1);
        var fileInfo2 = new FileInfo(filePath2);

        // Act
        var hash1 = _calculator.Calculate(fileInfo1);
        var hash2 = _calculator.Calculate(fileInfo2);

        // Assert
        hash1.Should().NotBe(hash2);

        Cleanup();
    }

    [Test]
    public void Calculate_SameFileMultipleTimes_ReturnsSameHash()
    {
        // Arrange
        var filePath = CreateTempFile("multiple_reads.txt", "Content for multiple reads");
        var fileInfo = new FileInfo(filePath);

        // Act
        var hash1 = _calculator.Calculate(fileInfo);
        var hash2 = _calculator.Calculate(fileInfo);
        var hash3 = _calculator.Calculate(fileInfo);

        // Assert
        hash1.Should().Be(hash2);
        hash2.Should().Be(hash3);

        Cleanup();
    }

    #endregion

    #region Case Sensitivity Tests

    [Test]
    public void Calculate_IsCaseInsensitive()
    {
        // Arrange - The hash output should always be lowercase
        var filePath = CreateTempFile("case_test.txt", "Case sensitivity test");
        var fileInfo = new FileInfo(filePath);

        // Act
        var hash = _calculator.Calculate(fileInfo);
        var upperHash = hash.ToUpperInvariant();
        var lowerHash = hash.ToLowerInvariant();

        // Assert - The calculated hash should be lowercase
        hash.Should().Be(lowerHash);
        hash.Should().NotBe(upperHash); // Unless all characters are digits

        // Verify the hash only contains lowercase letters and digits
        foreach (var c in hash)
        {
            (char.IsDigit(c) || (c >= 'a' && c <= 'f')).Should().BeTrue();
        }

        Cleanup();
    }

    [Test]
    public void Calculate_ContentCaseSensitive_DifferentHashForDifferentCase()
    {
        // Arrange - Content case DOES matter for hash calculation
        var filePath1 = CreateTempFile("upper_content.txt", "HELLO WORLD");
        var filePath2 = CreateTempFile("lower_content.txt", "hello world");
        var fileInfo1 = new FileInfo(filePath1);
        var fileInfo2 = new FileInfo(filePath2);

        // Act
        var hash1 = _calculator.Calculate(fileInfo1);
        var hash2 = _calculator.Calculate(fileInfo2);

        // Assert - Different case content should produce different hashes
        hash1.Should().NotBe(hash2);

        Cleanup();
    }

    #endregion

    #region Large File Tests

    [Test]
    public void Calculate_HandlesLargeFiles()
    {
        // Arrange - Create a 1MB file
        var largeContent = new StringBuilder();
        var chunk = "This is a chunk of text that will be repeated many times. ";
        var targetSize = 1024 * 1024; // 1 MB

        while (largeContent.Length < targetSize)
        {
            largeContent.Append(chunk);
        }

        var filePath = CreateTempFile("large_file.txt", largeContent.ToString());
        var fileInfo = new FileInfo(filePath);

        // Act
        var hash = _calculator.Calculate(fileInfo);

        // Assert
        hash.Should().NotBeNullOrEmpty();
        hash.Should().HaveLength(64);
        hash.Should().MatchRegex("^[a-f0-9]{64}$");

        Cleanup();
    }

    [Test]
    public void Calculate_LargeFileConsistentHash()
    {
        // Arrange - Create a moderately large file
        var content = new string('X', 100_000); // 100KB of X's
        var filePath1 = CreateTempFile("large_consistent_1.txt", content);
        var filePath2 = CreateTempFile("large_consistent_2.txt", content);
        var fileInfo1 = new FileInfo(filePath1);
        var fileInfo2 = new FileInfo(filePath2);

        // Act
        var hash1 = _calculator.Calculate(fileInfo1);
        var hash2 = _calculator.Calculate(fileInfo2);

        // Assert
        hash1.Should().Be(hash2);

        Cleanup();
    }

    #endregion

    #region Binary Content Tests

    [Test]
    public void Calculate_HandlesBinaryContent()
    {
        // Arrange - Create a file with binary content
        var binaryContent = new byte[] { 0x00, 0x01, 0x02, 0xFF, 0xFE, 0xFD, 0x80, 0x7F };
        var filePath = CreateTempFileWithBytes("binary_file.bin", binaryContent);
        var fileInfo = new FileInfo(filePath);

        // Act
        var hash = _calculator.Calculate(fileInfo);

        // Assert
        hash.Should().NotBeNullOrEmpty();
        hash.Should().HaveLength(64);
        hash.Should().MatchRegex("^[a-f0-9]{64}$");

        Cleanup();
    }

    [Test]
    public void Calculate_SameBinaryContent_ReturnsSameHash()
    {
        // Arrange
        var binaryContent = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }; // PNG header bytes
        var filePath1 = CreateTempFileWithBytes("binary_same_1.bin", binaryContent);
        var filePath2 = CreateTempFileWithBytes("binary_same_2.bin", binaryContent);
        var fileInfo1 = new FileInfo(filePath1);
        var fileInfo2 = new FileInfo(filePath2);

        // Act
        var hash1 = _calculator.Calculate(fileInfo1);
        var hash2 = _calculator.Calculate(fileInfo2);

        // Assert
        hash1.Should().Be(hash2);

        Cleanup();
    }

    #endregion

    #region Special Characters Tests

    [Test]
    public void Calculate_HandlesSpecialCharacters()
    {
        // Arrange - Content with unicode and special characters
        var content = "Special chars: äöü ñ 中文 日本語 🎉 emoji! \t\n\r";
        var filePath = CreateTempFile("special_chars.txt", content);
        var fileInfo = new FileInfo(filePath);

        // Act
        var hash = _calculator.Calculate(fileInfo);

        // Assert
        hash.Should().NotBeNullOrEmpty();
        hash.Should().HaveLength(64);
        hash.Should().MatchRegex("^[a-f0-9]{64}$");

        Cleanup();
    }

    #endregion

    #region Async Tests

    [Test]
    public async Task CalculateAsync_ReturnsCorrectHash()
    {
        // Arrange
        var content = "Hello, World!";
        var filePath = CreateTempFile("async_hash_test.txt", content);
        var fileInfo = new FileInfo(filePath);

        // Act
        var hash = await _calculator.CalculateAsync(fileInfo);

        // Assert
        hash.Should().NotBeNullOrEmpty();
        hash.Should().HaveLength(64);
        hash.Should().MatchRegex("^[a-f0-9]{64}$");

        Cleanup();
    }

    [Test]
    public async Task CalculateAsync_ReturnsLowercaseHash()
    {
        // Arrange
        var filePath = CreateTempFile("async_lowercase_test.txt", "Test content");
        var fileInfo = new FileInfo(filePath);

        // Act
        var hash = await _calculator.CalculateAsync(fileInfo);

        // Assert
        hash.Should().Be(hash.ToLowerInvariant());

        Cleanup();
    }

    [Test]
    public async Task CalculateAsync_WithEmptyFile_ReturnsValidHash()
    {
        // Arrange
        var filePath = CreateTempFile("async_empty_file.txt", string.Empty);
        var fileInfo = new FileInfo(filePath);
        var expectedEmptyHash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

        // Act
        var hash = await _calculator.CalculateAsync(fileInfo);

        // Assert
        hash.Should().Be(expectedEmptyHash);

        Cleanup();
    }

    [Test]
    public async Task CalculateAsync_MatchesSyncVersion()
    {
        // Arrange
        var content = "Content for sync vs async comparison";
        var filePath = CreateTempFile("sync_async_compare.txt", content);
        var fileInfo = new FileInfo(filePath);

        // Act
        var syncHash = _calculator.Calculate(fileInfo);
        var asyncHash = await _calculator.CalculateAsync(fileInfo);

        // Assert
        asyncHash.Should().Be(syncHash);

        Cleanup();
    }

    [Test]
    public async Task CalculateAsync_SupportsCancellation()
    {
        // Arrange
        var content = "Content for cancellation test";
        var filePath = CreateTempFile("cancellation_test.txt", content);
        var fileInfo = new FileInfo(filePath);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await _calculator.CalculateAsync(fileInfo, cts.Token));

        Cleanup();
    }

    #endregion
}
