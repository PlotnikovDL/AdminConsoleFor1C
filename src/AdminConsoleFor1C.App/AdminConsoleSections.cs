using FluentIcons.Common;

namespace AdminConsoleFor1C.App;

public static class AdminConsoleSections
{
    public const string AgentsTag = "agents";
    public const string ProcessesTag = "processes";
    public const string InfobasesTag = "infobases";
    public const string ClustersTag = "clusters";
    public const string LicensesTag = "licenses";
    public const string SettingsTag = "settings";

    public static AdminConsoleSection FromTag(string tag)
    {
        return tag switch
        {
            AgentsTag => AdminConsoleSection.Agents,
            ProcessesTag => AdminConsoleSection.Processes,
            InfobasesTag => AdminConsoleSection.Infobases,
            ClustersTag => AdminConsoleSection.Clusters,
            LicensesTag => AdminConsoleSection.Licenses,
            SettingsTag => AdminConsoleSection.Settings,
            _ => AdminConsoleSection.Agents
        };
    }

    public static AdminConsoleSectionInfo GetInfo(AdminConsoleSection section)
    {
        return section switch
        {
            AdminConsoleSection.Agents => new AdminConsoleSectionInfo(
                section,
                "Службы",
                "Службы Windows и установки сервера 1С на этом компьютере",
                Icon.ServiceBell),
            AdminConsoleSection.Processes => new AdminConsoleSectionInfo(
                section,
                "Процессы",
                "Процессы 1С, PID, роли, порты и параметры запуска",
                Icon.AppsListDetail),
            AdminConsoleSection.Infobases => new AdminConsoleSectionInfo(
                section,
                "Информационные базы",
                "Список баз, параметры публикации и будущие операции переноса",
                Icon.Database),
            AdminConsoleSection.Clusters => new AdminConsoleSectionInfo(
                section,
                "Кластеры",
                "Рабочие серверы, кластеры 1С и состояние администрирования",
                Icon.ServerMultiple),
            AdminConsoleSection.Licenses => new AdminConsoleSectionInfo(
                section,
                "Лицензии",
                "Аппаратные, программные и занятые лицензии 1С",
                Icon.Key),
            AdminConsoleSection.Settings => new AdminConsoleSectionInfo(
                section,
                "Настройки",
                "Параметры приложения, пути и диагностика окружения",
                Icon.Settings),
            _ => throw new ArgumentOutOfRangeException(nameof(section), section, null)
        };
    }
}
