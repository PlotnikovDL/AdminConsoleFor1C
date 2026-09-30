using System.Reflection;

namespace AdminConsoleFor1C.App;

internal static class ApplicationVersion
{
    public static string WindowTitle { get; } = CreateWindowTitle();

    private static string CreateWindowTitle()
    {
        var value = typeof(ApplicationVersion).Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version;
        var version = Version.TryParse(value, out var parsed)
            ? parsed.ToString(parsed.Revision > 0 ? 4 : 3) : "версия не определена";
#if DEBUG
        return $"Центр администрирования 1С · {version} · Разработка";
#else
        return $"Центр администрирования 1С · {version}";
#endif
    }
}
