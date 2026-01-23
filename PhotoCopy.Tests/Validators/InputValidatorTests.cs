using Microsoft.Extensions.Logging;
using NSubstitute;
using PhotoCopy.Configuration;
using PhotoCopy.Validators;
using TUnit.Assertions.Extensions;

namespace PhotoCopy.Tests.Validators;

/// <summary>
/// Tests for InputValidator class.
/// </summary>
public class InputValidatorTests
{
    private ILogger<InputValidator> _logger = null!;
    private IConsoleInteraction _console = null!;
    private InputValidator _validator = null!;

    [Before(Test)]
    public void Setup()
    {
        _logger = Substitute.For<ILogger<InputValidator>>();
        _console = Substitute.For<IConsoleInteraction>();
        _validator = new InputValidator(_logger, _console);
    }

    [Test]
    public async Task ValidateCopyConfiguration_MissingSource_ReturnsFalse()
    {
        var config = new PhotoCopyConfig { Source = null, Destination = "/dest" };

        var result = _validator.ValidateCopyConfiguration(config);

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task ValidateCopyConfiguration_MissingDestination_ReturnsFalse()
    {
        var config = new PhotoCopyConfig { Source = "/source", Destination = null };

        var result = _validator.ValidateCopyConfiguration(config);

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task ValidateCopyConfiguration_SourceDoesNotExist_ReturnsFalse()
    {
        var config = new PhotoCopyConfig 
        { 
            Source = @"C:\NonExistent\Directory\ThatDoesNotExist_12345",
            Destination = @"C:\Dest\{name}",
            DuplicatesFormat = "_{number}"
        };

        var result = _validator.ValidateCopyConfiguration(config);

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task ValidateCopyConfiguration_DuplicatesFormatMissingNumber_ReturnsFalse()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        try
        {
            var config = new PhotoCopyConfig 
            { 
                Source = tempDir,
                Destination = @"{name}",
                DuplicatesFormat = "_copy" // Missing {number}
            };

            var result = _validator.ValidateCopyConfiguration(config);

            await Assert.That(result).IsFalse();
        }
        finally
        {
            Directory.Delete(tempDir);
        }
    }

    [Test]
    public async Task ValidateCopyConfiguration_DestinationMissingName_NonInteractive_ReturnsFalse()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        try
        {
            var config = new PhotoCopyConfig 
            { 
                Source = tempDir,
                Destination = @"C:\Photos\{year}\{month}", // Missing {name}
                DuplicatesFormat = "_{number}"
            };

            _console.IsInputRedirected.Returns(true); // Non-interactive mode

            var result = _validator.ValidateCopyConfiguration(config);

            await Assert.That(result).IsFalse();
            _console.Received().WriteLine(Arg.Is<string>(s => s.Contains("destination path does not contain name")));
        }
        finally
        {
            Directory.Delete(tempDir);
        }
    }

    [Test]
    public async Task ValidateCopyConfiguration_DestinationMissingName_UserConfirmsYes_ReturnsTrue()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        try
        {
            var config = new PhotoCopyConfig 
            { 
                Source = tempDir,
                Destination = @"C:\Photos\{year}\{month}", // Missing {name}
                DuplicatesFormat = "_{number}"
            };

            _console.IsInputRedirected.Returns(false);
            _console.ReadLine().Returns("yes");

            var result = _validator.ValidateCopyConfiguration(config);

            await Assert.That(result).IsTrue();
        }
        finally
        {
            Directory.Delete(tempDir);
        }
    }

    [Test]
    public async Task ValidateCopyConfiguration_DestinationMissingName_UserRejects_ReturnsFalse()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        try
        {
            var config = new PhotoCopyConfig 
            { 
                Source = tempDir,
                Destination = @"C:\Photos\{year}\{month}", // Missing {name}
                DuplicatesFormat = "_{number}"
            };

            _console.IsInputRedirected.Returns(false);
            _console.ReadLine().Returns("no");

            var result = _validator.ValidateCopyConfiguration(config);

            await Assert.That(result).IsFalse();
        }
        finally
        {
            Directory.Delete(tempDir);
        }
    }

    [Test]
    public async Task ValidateCopyConfiguration_ValidConfig_ReturnsTrue()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        try
        {
            var config = new PhotoCopyConfig 
            { 
                Source = tempDir,
                Destination = @"C:\Photos\{year}\{month}\{name}",
                DuplicatesFormat = "_{number}"
            };

            var result = _validator.ValidateCopyConfiguration(config);

            await Assert.That(result).IsTrue();
        }
        finally
        {
            Directory.Delete(tempDir);
        }
    }

    [Test]
    public async Task ValidateCopyConfiguration_DestinationWithNameNoExtension_ReturnsTrue()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        try
        {
            var config = new PhotoCopyConfig 
            { 
                Source = tempDir,
                Destination = @"C:\Photos\{year}\{namenoext}.jpg",
                DuplicatesFormat = "_{number}"
            };

            var result = _validator.ValidateCopyConfiguration(config);

            await Assert.That(result).IsTrue();
        }
        finally
        {
            Directory.Delete(tempDir);
        }
    }

    [Test]
    public async Task ValidateSourceRequired_MissingSource_ReturnsFalse()
    {
        var config = new PhotoCopyConfig { Source = null };

        var result = _validator.ValidateSourceRequired(config);

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task ValidateSourceRequired_EmptySource_ReturnsFalse()
    {
        var config = new PhotoCopyConfig { Source = "" };

        var result = _validator.ValidateSourceRequired(config);

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task ValidateSourceRequired_ValidSource_ReturnsTrue()
    {
        var config = new PhotoCopyConfig { Source = "/some/path" };

        var result = _validator.ValidateSourceRequired(config);

        await Assert.That(result).IsTrue();
    }
}
