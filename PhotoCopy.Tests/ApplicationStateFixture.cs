using System;
using PhotoCopy;

namespace PhotoCopy.Tests
{
    public class ApplicationStateFixture : IDisposable
    {
        private readonly Options originalOptions;

        public ApplicationStateFixture()
        {
            // Store the original options
            originalOptions = ApplicationState.Options;
            // Initialize with fresh options for tests
            ApplicationState.Options = new Options();
        }

        public void Dispose()
        {
            // Restore original options after tests
            ApplicationState.Options = originalOptions;
        }
    }
}
