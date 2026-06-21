namespace AdminConsoleFor1C.App;

internal static class AgentComponentTextFormatter
{
    public static string FormatProcessCount(int count)
    {
        return FormatCount(count, "процесс", "процесса", "процессов");
    }

    public static string FormatFoundServices(int count)
    {
        return count == 1
            ? "Найдена 1 служба"
            : $"Найдено {FormatServiceCount(count)}";
    }

    private static string FormatServiceCount(int count)
    {
        return FormatCount(count, "служба", "службы", "служб");
    }

    private static string FormatCount(int count, string one, string few, string many)
    {
        var modulo100 = count % 100;
        if (modulo100 is >= 11 and <= 14)
        {
            return $"{count} {many}";
        }

        return (count % 10) switch
        {
            1 => $"{count} {one}",
            >= 2 and <= 4 => $"{count} {few}",
            _ => $"{count} {many}"
        };
    }
}
