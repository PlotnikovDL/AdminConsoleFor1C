namespace AdminConsoleFor1C.App;

public enum AdminConsoleSection
{
    Agents,
    Infobases,
    Sessions,
    Licenses,
    Settings
}

internal static class AdminConsoleSections
{
    public const string AgentsTag = "agents";
    public const string InfobasesTag = "infobases";
    public const string SessionsTag = "sessions";
    public const string LicensesTag = "licenses";
    public const string SettingsTag = "settings";

    public static AdminConsoleSection FromTag(string? tag) =>
        tag switch
        {
            InfobasesTag => AdminConsoleSection.Infobases,
            SessionsTag => AdminConsoleSection.Sessions,
            LicensesTag => AdminConsoleSection.Licenses,
            SettingsTag => AdminConsoleSection.Settings,
            _ => AdminConsoleSection.Agents
        };

    public static string GetTitle(AdminConsoleSection section) =>
        section switch
        {
            AdminConsoleSection.Infobases => "Информационные базы",
            AdminConsoleSection.Sessions => "Сеансы",
            AdminConsoleSection.Licenses => "Лицензии",
            AdminConsoleSection.Settings => "Настройки",
            _ => "Агенты сервера 1С"
        };

    public static string GetDescription(AdminConsoleSection section, string agentsStatusText) =>
        section switch
        {
            AdminConsoleSection.Infobases => "Информационные базы найденных кластеров 1С",
            AdminConsoleSection.Sessions => "Активные пользователи и сеансы информационных баз",
            AdminConsoleSection.Licenses => "Аппаратные, программные и занятые лицензии",
            AdminConsoleSection.Settings => "Параметры приложения",
            _ => agentsStatusText
        };
}
