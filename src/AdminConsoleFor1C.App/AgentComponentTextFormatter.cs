namespace AdminConsoleFor1C.App;

internal static class AgentComponentTextFormatter
{
    public static string FormatProcessCount(int count)
    {
        return FormatCount(count, "процесс", "процесса", "процессов");
    }

    public static string FormatLinkedProcessCount(int count)
    {
        return $"{count} {FormatCountWord(count, "связанный процесс", "связанных процесса", "связанных процессов")}";
    }

    public static string FormatFoundServices(int count)
    {
        return count == 1
            ? "Найдена 1 служба"
            : $"Найдено {FormatServiceCount(count)}";
    }

    public static string FormatRunningServiceSummary(int serviceCount, int runningServiceCount)
    {
        if (serviceCount == 0)
        {
            return "Нет данных о службах агента";
        }

        if (serviceCount == 1)
        {
            return runningServiceCount == 1
                ? "1 служба работает"
                : "1 служба не работает";
        }

        return $"{FormatServiceCount(serviceCount)}, работает {runningServiceCount}";
    }

    private static string FormatServiceCount(int count)
    {
        return FormatCount(count, "служба", "службы", "служб");
    }

    private static string FormatCount(int count, string one, string few, string many)
    {
        return $"{count} {FormatCountWord(count, one, few, many)}";
    }

    private static string FormatCountWord(int count, string one, string few, string many)
    {
        var modulo100 = count % 100;
        if (modulo100 is >= 11 and <= 14)
        {
            return many;
        }

        return (count % 10) switch
        {
            1 => one,
            >= 2 and <= 4 => few,
            _ => many
        };
    }
}
