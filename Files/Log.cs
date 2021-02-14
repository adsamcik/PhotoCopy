using PhotoCopySort;
using System;

namespace PhotoCopy.Files
{
    public static class Log
    {

        public static void Print(string message, Options.LogLevel logLevel)
        {
            if ((int)logLevel >= (int)ApplicationState.Options.Log)
            {
                Console.WriteLine(message);
            }
        }
    }
}
