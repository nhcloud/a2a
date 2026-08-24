namespace A2A.Demo.Client.Infrastructure;

/// <summary>Console formatting. Presentation-friendly: big headings, calm colours.</summary>
public static class Ux
{
    public static void Banner(string title, string subtitle)
    {
        Console.WriteLine();
        WriteLine(new string('=', 74), ConsoleColor.DarkCyan);
        WriteLine("  " + title, ConsoleColor.Cyan);
        WriteLine("  " + subtitle, ConsoleColor.DarkGray);
        WriteLine(new string('=', 74), ConsoleColor.DarkCyan);
        Console.WriteLine();
    }

    public static void Heading(string text)
    {
        Console.WriteLine();
        WriteLine("── " + text + " " + new string('─', Math.Max(0, 70 - text.Length)), ConsoleColor.Cyan);
    }

    public static void Step(string text) => WriteLine("  ▸ " + text, ConsoleColor.DarkGray);

    public static void Wire(string text) => WriteLine("  « " + text, ConsoleColor.DarkYellow);

    public static void Info(string text) => WriteLine("  " + text, ConsoleColor.Gray);

    public static void Success(string text) => WriteLine("  ✓ " + text, ConsoleColor.Green);

    public static void Warn(string text) => WriteLine("  ! " + text, ConsoleColor.Yellow);

    public static void Error(string text) => WriteLine("  ✗ " + text, ConsoleColor.Red);

    public static void Agent(string text)
    {
        Console.WriteLine();
        WriteLine(Indent(text, "  "), ConsoleColor.White);
        Console.WriteLine();
    }

    public static void Prompt(string text)
    {
        Console.WriteLine();
        WriteLine("  you → " + text, ConsoleColor.Magenta);
    }

    public static void WriteLine(string text, ConsoleColor color)
    {
        ConsoleColor previous = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.WriteLine(text);
        Console.ForegroundColor = previous;
    }

    public static void Write(string text, ConsoleColor color)
    {
        ConsoleColor previous = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.Write(text);
        Console.ForegroundColor = previous;
    }

    /// <summary>Waits for Enter so the presenter controls the pace between beats.</summary>
    public static void Pause(bool enabled, string message = "press Enter to continue")
    {
        if (!enabled)
        {
            return;
        }

        WriteLine($"  [{message}]", ConsoleColor.DarkGray);
        Console.ReadLine();
    }

    private static string Indent(string text, string prefix) =>
        string.Join(Environment.NewLine, text
            .Replace("\r\n", "\n")
            .Split('\n')
            .Select(line => prefix + line));
}
