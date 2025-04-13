using System;

namespace PhotoCopy.Files;

public static class Log
{
    public static void Print(string message, Options.LogLevel logLevel)
    {
        // Default to showing all logs if Options is not initialized
        if (ApplicationState.Options == null || (int)logLevel >= (int)ApplicationState.Options.Log)
        {
            Console.WriteLine(message);
        }
    }
}