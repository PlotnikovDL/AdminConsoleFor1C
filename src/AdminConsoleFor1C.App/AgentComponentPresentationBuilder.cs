using System.Text.RegularExpressions;
using AdminConsoleFor1C.Core.Services;
using FluentIcons.Common;

namespace AdminConsoleFor1C.App;

public sealed partial class AgentComponentPresentationBuilder
{
    public IReadOnlyList<AgentComponentItemViewModel> BuildComponents(
        IReadOnlyList<OneCServiceInfo> services,
        IReadOnlyList<OneCProcessInfo> processes)
    {
        var components = new List<AgentComponentItemViewModel>();

        foreach (var service in services)
        {
            var relatedProcesses = GetRelatedProcesses(service, processes).ToList();

            components.Add(BuildComponent(service, relatedProcesses));
        }

        return components
            .OrderBy(static component => component.SortOrder)
            .ThenBy(static component => component.Title, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(static component => component.SummaryDescription, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<OneCProcessItemViewModel> BuildProcessItems(
        IReadOnlyList<OneCProcessInfo> processes)
    {
        return processes
            .OrderBy(static process => process.Kind)
            .ThenBy(static process => process.ProcessId)
            .Select(BuildProcessItem)
            .ToList();
    }

    public IReadOnlyList<AgentServiceCandidateItemViewModel> BuildServiceCandidateItems(
        IReadOnlyList<OneCServerAgentServiceCandidate> candidates)
    {
        return candidates
            .Select(BuildServiceCandidateItem)
            .ToList();
    }

    private static AgentServiceCandidateItemViewModel BuildServiceCandidateItem(
        OneCServerAgentServiceCandidate candidate)
    {
        var versionText = candidate.VersionText == "—"
            ? "Версия: —"
            : $"Версия: {candidate.VersionText}";

        return new AgentServiceCandidateItemViewModel
        {
            Title = "ragent.exe",
            SummaryDescription = candidate.ExecutablePathText,
            ExecutablePathText = candidate.ExecutablePathText,
            StatusSummaryText = "Не зарегистрирован",
            StatusKind = AgentStatusKind.Information,
            TechnicalSummaryText = versionText,
            Icon = Icon.AppFolder
        };
    }

    private static AgentComponentItemViewModel BuildComponent(
        OneCServiceInfo service,
        IReadOnlyList<OneCProcessInfo> processes)
    {
        var processItems = processes
            .Select(BuildProcessItem)
            .ToList();

        var primaryPortSummaryText = GetPrimaryServicePortSummaryText(service);
        var versionSummaryText = GetVersionSummaryText(service.VersionText);
        var startModeSummaryText = $"Запуск: {service.StartModeDisplayName}";

        return new AgentComponentItemViewModel
        {
            Id = GetServiceComponentId(service),
            Title = GetServiceTitle(service),
            SummaryDescription = GetServiceDisplayNameSummaryText(service),
            StatusText = service.StateDisplayName,
            ProcessSummaryText = AgentComponentTextFormatter.FormatLinkedProcessCount(processes.Count),
            StatusSummaryText = service.StateDisplayName,
            StatusKind = GetServiceStatusKind(service),
            PrimaryPortSummaryText = primaryPortSummaryText,
            VersionSummaryText = versionSummaryText,
            StartModeSummaryText = startModeSummaryText,
            TechnicalSummaryText = $"{primaryPortSummaryText} {versionSummaryText}",
            SortOrder = service.Kind switch
            {
                OneCServiceKind.ServerAgent => 0,
                OneCServiceKind.AdministrationServer => 10,
                OneCServiceKind.DebugServer => 20,
                _ => 100
            },
            Icon = service.Kind switch
            {
                OneCServiceKind.ServerAgent => Icon.ServiceBell,
                OneCServiceKind.AdministrationServer => Icon.ServerPlay,
                OneCServiceKind.DebugServer => Icon.DeveloperBoardLightning,
                _ => Icon.ServiceBell
            },
            Details = BuildServiceDetails(service, processes),
            Processes = processItems,
            ServiceName = service.Name,
            ServiceDisplayName = service.DisplayNameText,
            CanStartService = CanStart(service),
            CanStopService = CanStop(service),
            CanRestartService = CanRestart(service)
        };
    }

    private static AgentStatusKind GetServiceStatusKind(OneCServiceInfo service)
    {
        return service.State switch
        {
            "Running" => AgentStatusKind.Success,
            "Start Pending" or "Continue Pending" or "Pause Pending" or "Paused" or "Stop Pending" => AgentStatusKind.Attention,
            _ => AgentStatusKind.Neutral
        };
    }

    private static OneCProcessItemViewModel BuildProcessItem(OneCProcessInfo process)
    {
        return new OneCProcessItemViewModel
        {
            ProcessId = process.ProcessId,
            Title = process.KindDisplayName,
            Description = process.Name,
            ProcessIdText = $"PID {process.ProcessIdText}",
            RoleText = process.RoleText,
            Icon = GetProcessIcon(process.Kind),
            Details = BuildProcessDetails(process)
        };
    }

    private static Icon GetProcessIcon(OneCProcessKind kind)
    {
        return kind switch
        {
            OneCProcessKind.ServerAgent => Icon.ServerPlay,
            OneCProcessKind.AdministrationServer => Icon.ServerPlay,
            OneCProcessKind.DebugServer => Icon.DeveloperBoardLightning,
            OneCProcessKind.ClusterManager => Icon.ServerMultiple,
            OneCProcessKind.WorkerProcess => Icon.Box,
            _ => Icon.AppGeneric
        };
    }

    private static string GetServiceTitle(OneCServiceInfo service)
    {
        return service.Kind switch
        {
            OneCServiceKind.ServerAgent => "Агент сервера 1С",
            _ => service.KindDisplayName
        };
    }

    private static string GetPrimaryServicePortSummaryText(OneCServiceInfo service)
    {
        return service.Kind switch
        {
            OneCServiceKind.ServerAgent => service.AgentPort is null ? "Агент: —" : $"Агент: {service.AgentPort}",
            OneCServiceKind.AdministrationServer => service.AdministrationServerPort is null ? "RAS: —" : $"RAS: {service.AdministrationServerPort}",
            OneCServiceKind.DebugServer => service.DebugServerPort is null ? "Отладка HTTP: —" : $"Отладка HTTP: {service.DebugServerPort}",
            _ => service.PortsText == "—" ? "Порты: —" : service.PortsText.Replace(Environment.NewLine, ", ")
        };
    }

    private static string GetVersionSummaryText(string versionText)
    {
        return versionText == "—" ? "Версия: —" : $"Версия: {versionText}";
    }

    private static string GetServiceDisplayNameSummaryText(OneCServiceInfo service)
    {
        var displayName = service.DisplayNameText;
        if (displayName == "—")
        {
            return displayName;
        }

        if (service.AgentPort is { } agentPort && !string.IsNullOrWhiteSpace(service.Version))
        {
            var suffix = $" {agentPort} {service.Version}";
            if (displayName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return displayName[..^suffix.Length].TrimEnd();
            }
        }

        return ServiceDisplayNameVersionSuffixRegex().Replace(displayName, string.Empty).TrimEnd();
    }

    [GeneratedRegex(@"\s+\d{2,5}\s+\d+(?:\.\d+){2,}$", RegexOptions.CultureInvariant)]
    private static partial Regex ServiceDisplayNameVersionSuffixRegex();

    private static bool CanStart(OneCServiceInfo service)
    {
        return !string.IsNullOrWhiteSpace(service.Name)
            && string.Equals(service.State, "Stopped", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(service.StartMode, "Disabled", StringComparison.OrdinalIgnoreCase);
    }

    private static bool CanStop(OneCServiceInfo service)
    {
        return !string.IsNullOrWhiteSpace(service.Name)
            && string.Equals(service.State, "Running", StringComparison.OrdinalIgnoreCase);
    }

    private static bool CanRestart(OneCServiceInfo service)
    {
        return CanStop(service);
    }

    private static string GetServiceComponentId(OneCServiceInfo service)
    {
        if (!string.IsNullOrWhiteSpace(service.Name))
        {
            return $"service:{service.Name}";
        }

        return $"service:{service.Kind}:{service.DisplayNameText}";
    }

    private static IReadOnlyList<AgentComponentDetailItemViewModel> BuildServiceDetails(
        OneCServiceInfo service,
        IReadOnlyList<OneCProcessInfo> processes)
    {
        var details = new List<AgentComponentDetailItemViewModel>();

        AddComponentDetail(details, "Службы Windows", "Отображаемое имя службы", service.DisplayNameText, Icon.ServiceBell, includeEmpty: true);
        AddComponentDetail(details, "Диспетчер задач", "Системное имя службы", service.Name, Icon.TaskListSquare, includeEmpty: true);
        AddComponentDetail(details, "Тип запуска", "Тип запуска службы Windows", service.StartModeDisplayName, Icon.ArrowClockwise);
        AddComponentDetail(details, "Запуск от имени", "Пользователь службы", service.AccountText, Icon.Person);
        AddComponentDetail(details, "PID службы", "Идентификатор процесса службы", service.ProcessIdText, Icon.NumberSymbol);
        AddComponentDetail(details, "Версия", "Версия платформы 1С", service.VersionText, Icon.AppGeneric);
        AddServicePortDetails(details, service);

        if (processes.Count > 0)
        {
            AddComponentDetail(
                details,
                "Связанные процессы",
                "Подробности доступны в разделе Процессы",
                AgentComponentTextFormatter.FormatLinkedProcessCount(processes.Count),
                Icon.AppsListDetail);
        }

        AddComponentDetail(details, "Каталог данных", "Рабочий каталог сервера", service.DataDirectoryText, Icon.Folder);
        AddComponentDetail(details, "Исполняемый файл", "Путь к файлу службы", service.ExecutablePathText, Icon.AppFolder);
        AddComponentDetail(details, "Аргументы", "Параметры запуска", service.ArgumentsText, Icon.Code);

        return details;
    }

    private static IReadOnlyList<OneCProcessDetailItemViewModel> BuildProcessDetails(
        OneCProcessInfo process)
    {
        var details = new List<OneCProcessDetailItemViewModel>();

        AddProcessDetail(details, "Процесс", "Имя исполняемого файла", process.Name, Icon.AppGeneric, includeEmpty: true);
        AddProcessDetail(details, "PID", "Идентификатор процесса", process.ProcessIdText, Icon.NumberSymbol, includeEmpty: true);
        AddProcessDetail(details, "Родитель", "PID родительского процесса", process.ParentProcessIdText, Icon.ArrowFlowUpRightRectangleMultiple);
        AddProcessDetail(details, "Роль", "Назначение процесса 1С", process.RoleText, Icon.TaskListSquare);
        AddProcessDetail(details, "Служба Windows", "Сопоставление со службой", process.RelatedServiceText, Icon.ServiceBell);
        AddProcessDetail(details, "Владелец", "Пользователь процесса", process.OwnerText, Icon.Person);
        AddProcessDetail(details, "Версия", "Версия платформы 1С", process.VersionText, Icon.AppGeneric);
        AddProcessPortDetails(details, process);
        AddProcessDetail(details, "Каталог данных", "Рабочий каталог процесса", ToDisplayText(process.DataDirectory), Icon.Folder);
        AddProcessDetail(details, "Исполняемый файл", "Путь к файлу процесса", process.ExecutablePathText, Icon.AppFolder);
        AddProcessDetail(details, "Аргументы", "Параметры запуска", process.ArgumentsText, Icon.Code);

        return details;
    }

    private static void AddServicePortDetails(
        List<AgentComponentDetailItemViewModel> details,
        OneCServiceInfo service)
    {
        AddComponentDetail(details, "Агент", "Порт агента кластера", service.AgentPortText, Icon.SerialPort);
        AddComponentDetail(details, "Кластер", "Порт главного менеджера кластера", service.RegPortText, Icon.ServerMultiple);
        AddComponentDetail(details, "RAS", "Порт сервера администрирования", service.AdministrationServerPortText, Icon.ServerPlay);
        AddComponentDetail(details, "Отладка HTTP", "Порт сервера отладки", service.DebugServerPortText, Icon.DeveloperBoardLightning);
        AddComponentDetail(details, "Рабочие процессы", "Диапазон портов рабочих процессов", service.PortRangeText, Icon.Box);
    }

    private static void AddProcessPortDetails(
        List<OneCProcessDetailItemViewModel> details,
        OneCProcessInfo process)
    {
        AddProcessDetail(details, "Агент", "Порт агента кластера", process.AgentPort?.ToString() ?? "—", Icon.SerialPort);
        AddProcessDetail(details, "Кластер", "Порт главного менеджера кластера", process.ClusterPort?.ToString() ?? "—", Icon.ServerMultiple);
        AddProcessDetail(details, "Рабочий", "Порт рабочего процесса", process.WorkerPort?.ToString() ?? "—", Icon.Box);
        AddProcessDetail(details, "RAS", "Порт сервера администрирования", process.AdministrationServerPort?.ToString() ?? "—", Icon.ServerPlay);
        AddProcessDetail(details, "Отладка HTTP", "Порт сервера отладки", process.DebugServerPort?.ToString() ?? "—", Icon.DeveloperBoardLightning);
        AddProcessDetail(details, "Рабочие процессы", "Диапазон портов рабочих процессов", ToDisplayText(process.PortRange), Icon.Box);
    }

    private static void AddComponentDetail(
        List<AgentComponentDetailItemViewModel> details,
        string title,
        string description,
        string value,
        Icon icon,
        bool includeEmpty = false)
    {
        if (!includeEmpty && (string.IsNullOrWhiteSpace(value) || value == "—"))
        {
            return;
        }

        details.Add(new AgentComponentDetailItemViewModel
        {
            Title = title,
            Description = description,
            Value = string.IsNullOrWhiteSpace(value) ? "—" : value,
            Icon = icon
        });
    }

    private static void AddProcessDetail(
        List<OneCProcessDetailItemViewModel> details,
        string title,
        string description,
        string value,
        Icon icon,
        bool includeEmpty = false)
    {
        if (!includeEmpty && (string.IsNullOrWhiteSpace(value) || value == "—"))
        {
            return;
        }

        details.Add(new OneCProcessDetailItemViewModel
        {
            Title = title,
            Description = description,
            Value = string.IsNullOrWhiteSpace(value) ? "—" : value,
            Icon = icon
        });
    }

    private static string ToDisplayText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "—" : value;
    }

    private static IEnumerable<OneCProcessInfo> GetRelatedProcesses(
        OneCServiceInfo service,
        IReadOnlyList<OneCProcessInfo> processes)
    {
        return processes
            .Where(process => IsRelatedProcess(service, process))
            .OrderBy(static process => process.Kind)
            .ThenBy(static process => process.ProcessId);
    }

    private static bool IsRelatedProcess(OneCServiceInfo service, OneCProcessInfo process)
    {
        if (service.ProcessId is { } serviceProcessId
            && serviceProcessId != 0
            && (process.ProcessId == serviceProcessId || process.ParentProcessId == serviceProcessId))
        {
            return true;
        }

        return service.Kind switch
        {
            OneCServiceKind.ServerAgent => process.Kind switch
            {
                OneCProcessKind.ServerAgent => service.AgentPort is not null && process.AgentPort == service.AgentPort,
                OneCProcessKind.ClusterManager => service.RegPort is not null && process.ClusterPort == service.RegPort,
                OneCProcessKind.WorkerProcess => !string.IsNullOrWhiteSpace(service.PortRange)
                    && string.Equals(service.PortRange, process.PortRange, StringComparison.OrdinalIgnoreCase),
                OneCProcessKind.DebugServer => service.DebugServerPort is not null
                    && process.DebugServerPort == service.DebugServerPort,
                _ => false
            },
            OneCServiceKind.AdministrationServer => service.AdministrationServerPort is not null
                && process.AdministrationServerPort == service.AdministrationServerPort,
            OneCServiceKind.DebugServer => service.DebugServerPort is not null
                && process.DebugServerPort == service.DebugServerPort,
            _ => false
        };
    }
}
