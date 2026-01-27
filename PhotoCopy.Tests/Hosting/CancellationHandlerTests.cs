using PhotoCopy.Hosting;

namespace PhotoCopy.Tests.Hosting;

/// <summary>
/// Tests for CancellationHandler class.
/// </summary>
public class CancellationHandlerTests
{
    [Test]
    public async Task Token_WhenCreated_IsNotCancelled()
    {
        using var handler = new CancellationHandler();

        await Assert.That(handler.Token.IsCancellationRequested).IsFalse();
    }

    [Test]
    public async Task Token_IsValidCancellationToken()
    {
        using var handler = new CancellationHandler();

        await Assert.That(handler.Token.CanBeCanceled).IsTrue();
    }

    [Test]
    public async Task Dispose_CanBeCalledMultipleTimes()
    {
        var handler = new CancellationHandler();
        
        // Should not throw when disposed multiple times
        handler.Dispose();
        handler.Dispose();

        // If we reach here without exception, the test passes
        await Assert.That(handler).IsNotNull();
    }

    [Test]
    public async Task Constructor_WithNullLogger_DoesNotThrow()
    {
        // Should not throw
        using var handler = new CancellationHandler(logger: null);

        await Assert.That(handler.Token.CanBeCanceled).IsTrue();
    }
}
