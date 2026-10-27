namespace LocalSonar.Infrastructure;

/// <summary>Thread-safe colored console output.</summary>
public static class ConsoleWriter
{
    private static readonly Lock Sync = new();

    public static void Header(string text)
    {
        Write(Environment.NewLine + "== " + text + " ==", ConsoleColor.Cyan);
    }

    public static void Info(string text) => Write(text, null);
    public static void Success(string text) => Write(text, ConsoleColor.Green);
    public static void Warn(string text) => Write("WARN: " + text, ConsoleColor.Yellow);
    public static void Error(string text) => Write("ERROR: " + text, ConsoleColor.Red);
    public static void Muted(string text) => Write(text, ConsoleColor.DarkGray);

    public static void Write(string text, ConsoleColor? color)
    {
        lock (Sync)
        {
            if (color is { } c)
            {
                var previous = Console.ForegroundColor;
                Console.ForegroundColor = c;
                Console.WriteLine(text);
                Console.ForegroundColor = previous;
            }
            else
            {
                Console.WriteLine(text);
            }
        }
    }
}
