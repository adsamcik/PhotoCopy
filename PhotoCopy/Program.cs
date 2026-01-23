using PhotoCopy.Hosting;
using System.Threading.Tasks;

namespace PhotoCopy;

/// <summary>
/// Application entry point.
/// </summary>
public class Program
{
    /// <summary>
    /// Main entry point that delegates to CommandRouter for argument handling.
    /// </summary>
    /// <param name="args">Command-line arguments.</param>
    /// <returns>Exit code from the executed command.</returns>
    static async Task<int> Main(string[] args)
    {
        var router = new CommandRouter();
        return await router.RouteAsync(args);
    }
}
