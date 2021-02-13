using PhotoCopySort;
using System;

namespace PhotoCopy.Files
{
    public static class Log
    {

        public static void Print(string message, LogLevel logLevel)
        {
            if ((int)logLevel >= (int)ApplicationState.Options.LogLevel)
            {
                Console.WriteLine(message);
            }
        }
    }
}
