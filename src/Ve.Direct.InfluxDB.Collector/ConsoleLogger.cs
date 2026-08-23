namespace Ve.Direct.InfluxDB.Collector;

internal static class ConsoleLogger
{
    private static readonly object SyncRoot = new();
    private static bool debugOutputEnabled;

    internal static void Init(bool enableDebugOutput, string version)
    {
        debugOutputEnabled = enableDebugOutput;

        Info($"Current Version: {version}");
    }

    internal static void Info(string message)
    {
        Write(message, ConsoleColor.White);
    }

    internal static void Warning(string message)
    {
        Write($"WARNING {message}", ConsoleColor.Yellow);
    }

    internal static void Debug(string message)
    {
        if (!debugOutputEnabled)
        {
            return;
        }

        Write($"DEBUG {message}", ConsoleColor.Yellow);
    }

    internal static void Error(string message, string prefix = "ERROR")
    {
        Write($"{prefix} {message}", ConsoleColor.Red);
    }

    internal static void Error(Exception exception)
    {
        Error(exception.Message, "EXCEPTION");

        if (debugOutputEnabled)
        {
            Error(exception.StackTrace ?? "No stack trace available.", "StackTrace");
        }

        var innerException = exception.InnerException;
        while (innerException != null)
        {
            Error(innerException.Message, "INNEREXCEPTION");
            innerException = innerException.InnerException;
        }
    }

    private static void Write(string message, ConsoleColor color)
    {
        lock (SyncRoot)
        {
            var oldForeground = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.WriteLine($"{DateTime.Now:o} - {message}");
            Console.ForegroundColor = oldForeground;
        }
    }
}
