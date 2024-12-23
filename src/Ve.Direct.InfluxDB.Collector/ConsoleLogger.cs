using System;

namespace Ve.Direct.InfluxDB.Collector
{
    public static class ConsoleLogger
    {
        private static bool writeDebug;

        public static void Init(bool enableDebugOutput, string version)
        {
            writeDebug = enableDebugOutput;

            Info($"Current Version: {version}");
        }

        public static void Info(string message)
        {
            var oldForeground = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine($"{DateTime.Now:o} - {message}");
            Console.ForegroundColor = oldForeground;
        }

        public static void Debug(string message)
        {
            if (!writeDebug)
            {
                return;
            }

            var oldForeground = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"{DateTime.Now:o} DEBUG {message}");
            Console.ForegroundColor = oldForeground;
        }

        public static void Error(string message, string prefix = "ERROR")
        {
            var oldForeground = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"{DateTime.Now:o} {prefix} {message}");
            Console.ForegroundColor = oldForeground;
        }

        public static void Error(Exception exception)
        {
            Error(exception.Message, "EXCEPTION");

            if (writeDebug)
            {
                Error(exception.StackTrace, "StackTrace");
            }

            var innerException = exception.InnerException;
            while (innerException != null)
            {
                Error(innerException.Message, "INNEREXCEPTION");
                innerException = innerException.InnerException;
            }
        }
    }
}
