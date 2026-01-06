using System.Threading;
using System.Threading.Tasks;

namespace PhotoCopy.Commands;

/// <summary>
/// Represents an executable command in the application.
/// </summary>
public interface ICommand
{
    /// <summary>
    /// Executes the command asynchronously.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>Exit code (0 for success, non-zero for failure).</returns>
    Task<int> ExecuteAsync(CancellationToken cancellationToken = default);
}
