using AdminConsoleFor1C.Core.Services;
using FluentIcons.Common;

namespace AdminConsoleFor1C.App;

public sealed class AgentComponentPresentationBuilder
{
    public IReadOnlyList<AgentComponentItemViewModel> BuildComponents(
        IReadOnlyList<OneCServiceInfo> services,
        IReadOnlyList<OneCProcessInfo> processes)
    {
        var components = new List<AgentComponentItemViewModel>();
        var assignedProcessIds = new HashSet<uint>();

        foreach (var service in services)
        {
            var relatedProcesses = GetRelatedProcesses(service, processes).ToList();
            foreach (var process in relatedProcesses)
            {
                assignedProcessIds.Add(process.ProcessId);
            }

            components.Add(BuildComponent(service, relatedProcesses));
        }

        foreach (var process in processes.Where(static process => process.Kind == OneCProcessKind.ServerAgent))
        {
            if (assignedProcessIds.Contains(process.ProcessId))
            {
                continue;
            }

            var relatedProcesses = processes
                .Where(candidate => candidate.ProcessId == process.ProcessId || candidate.ParentProcessId == process.ProcessId)
                .OrderBy(static candidate => candidate.Kind)
                .ThenBy(static candidate => candidate.ProcessId)
                .ToList();

            foreach (var relatedProcess in relatedProcesses)
            {
                assignedProcessIds.Add(relatedProcess.ProcessId);
            }

            components.Add(BuildComponent(process, relatedProcesses));
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

    private static AgentComponentItemViewModel BuildComponent(
        OneCServiceInfo service,
        IReadOnlyList<OneCProcessInfo> processes)
    {
        var processItems = processes
            .Select(BuildProcessItem)
            .ToList();

        return new AgentComponentItemViewModel
        {
            Id = GetServiceComponentId(service),
            Title = service.KindDisplayName,
            SummaryDescription = service.DisplayNameText,
            StatusText = service.StateDisplayName,
            ProcessSummaryText = AgentComponentTextFormatter.FormatProcessCount(processes.Count),
            SortOrder = service.Kind switch
            {
                OneCServiceKind.ServerAgent => 0,
                OneCServiceKind.AdministrationServer => 10,
                OneCServiceKind.DebugServer => 20,
                _ => 100
            },
            Icon = service.Kind switch
            {
                OneCServiceKind.ServerAgent => Icon.PuzzlePiece,
                OneCServiceKind.AdministrationServer => Icon.ServerPlay,
                OneCServiceKind.DebugServer => Icon.DeveloperBoardLightning,
                _ => Icon.PuzzlePiece
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

    private static AgentComponentItemViewModel BuildComponent(
        OneCProcessInfo process,
        IReadOnlyList<OneCProcessInfo> relatedProcesses)
    {
        var processItems = relatedProcesses
            .Select(BuildProcessItem)
            .ToList();

        return new AgentComponentItemViewModel
        {
            Id = GetProcessComponentId(process),
            Title = process.KindDisplayName,
            SummaryDescription = $"Процесс без службы Windows: {process.Name}",
            StatusText = "Без службы",
            ProcessSummaryText = AgentComponentTextFormatter.FormatProcessCount(relatedProcesses.Count),
            SortOrder = process.Kind switch
            {
                OneCProcessKind.ServerAgent => 1,
                OneCProcessKind.AdministrationServer => 11,
                OneCProcessKind.DebugServer => 21,
                OneCProcessKind.ClusterManager => 30,
                OneCProcessKind.WorkerProcess => 40,
                _ => 100
            },
            Icon = process.Kind switch
            {
                OneCProcessKind.ServerAgent => Icon.ServerPlay,
                OneCProcessKind.AdministrationServer => Icon.ServerPlay,
                OneCProcessKind.DebugServer => Icon.DeveloperBoardLightning,
                OneCProcessKind.ClusterManager => Icon.ServerMultiple,
                OneCProcessKind.WorkerProcess => Icon.Box,
                _ => Icon.AppGeneric
            },
            Details = BuildProcessComponentDetails(process, relatedProcesses),
            Processes = processItems,
            ServiceName = null,
            ServiceDisplayName = "—",
            CanStartService = false,
            CanStopService = false,
            CanRestartService = false
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

    private static string GetProcessComponentId(OneCProcessInfo process)
    {
        return $"process:{process.ProcessId}";
    }

    private static IReadOnlyList<AgentComponentDetailItemViewModel> BuildServiceDetails(
        OneCServiceInfo service,
        IReadOnlyList<OneCProcessInfo> processes)
    {
        var details = new List<AgentComponentDetailItemViewModel>();

        AddComponentDetail(details, "Службы Windows", "Отображаемое имя службы", service.DisplayNameText, Icon.ServiceBell, includeEmpty: true);
        AddComponentDetail(details, "Диспетчер задач", "Системное имя службы", service.Name, Icon.TaskListSquare, includeEmpty: true);
        AddComponentDetail(details, "Запуск", "Тип запуска службы Windows", service.StartModeDisplayName, Icon.ArrowClockwise);
        AddComponentDetail(details, "Учетная запись", "Пользователь службы", service.AccountText, Icon.Person);
        AddComponentDetail(details, "PID службы", "Идентификатор процесса службы", service.ProcessIdText, Icon.NumberSymbol);
        AddComponentDetail(details, "Версия", "Версия платформы 1С", service.VersionText, Icon.AppGeneric);
        AddComponentDetail(details, "Порты", "Порты 1С по назначению", service.PortsText, Icon.SerialPort);

        var processNames = FormatProcessNames(processes);
        if (!string.IsNullOrWhiteSpace(processNames))
        {
            AddComponentDetail(
                details,
                "Процессы",
                AgentComponentTextFormatter.FormatProcessCount(processes.Count),
                processNames,
                Icon.AppsListDetail);
        }

        AddComponentDetail(details, "Каталог данных", "Рабочий каталог сервера", service.DataDirectoryText, Icon.Folder);
        AddComponentDetail(details, "Исполняемый файл", "Путь к файлу службы", service.ExecutablePathText, Icon.AppFolder);
        AddComponentDetail(details, "Аргументы", "Параметры запуска", service.ArgumentsText, Icon.Code);

        return details;
    }

    private static IReadOnlyList<AgentComponentDetailItemViewModel> BuildProcessComponentDetails(
        OneCProcessInfo process,
        IReadOnlyList<OneCProcessInfo> relatedProcesses)
    {
        var details = new List<AgentComponentDetailItemViewModel>();

        AddComponentDetail(details, "Процесс", "Имя исполняемого файла", process.Name, Icon.AppGeneric, includeEmpty: true);
        AddComponentDetail(details, "PID", "Идентификатор процесса", process.ProcessIdText, Icon.NumberSymbol, includeEmpty: true);
        AddComponentDetail(details, "Родитель", "PID родительского процесса", process.ParentProcessIdText, Icon.ArrowFlowUpRightRectangleMultiple);
        AddComponentDetail(details, "Версия", "Версия платформы 1С", process.VersionText, Icon.AppGeneric);
        AddComponentDetail(details, "Владелец", "Пользователь процесса", process.OwnerText, Icon.Person);
        AddComponentDetail(details, "Порты", "Порты 1С по назначению", process.PortsText, Icon.SerialPort);

        var processNames = FormatProcessNames(relatedProcesses);
        if (!string.IsNullOrWhiteSpace(processNames) && relatedProcesses.Count > 1)
        {
            AddComponentDetail(
                details,
                "Связанные процессы",
                AgentComponentTextFormatter.FormatProcessCount(relatedProcesses.Count),
                processNames,
                Icon.AppsListDetail);
        }

        AddComponentDetail(details, "Исполняемый файл", "Путь к файлу процесса", process.ExecutablePathText, Icon.AppFolder);
        AddComponentDetail(details, "Аргументы", "Параметры запуска", process.ArgumentsText, Icon.Code);

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
        AddProcessDetail(details, "Порты", "Порты 1С по назначению", process.PortsText, Icon.SerialPort);
        AddProcessDetail(details, "Каталог данных", "Рабочий каталог процесса", ToDisplayText(process.DataDirectory), Icon.Folder);
        AddProcessDetail(details, "Исполняемый файл", "Путь к файлу процесса", process.ExecutablePathText, Icon.AppFolder);
        AddProcessDetail(details, "Аргументы", "Параметры запуска", process.ArgumentsText, Icon.Code);

        return details;
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

    private static string FormatProcessNames(IReadOnlyList<OneCProcessInfo> processes)
    {
        return string.Join(
            ", ",
            processes
                .Select(static process => process.Name)
                .Where(static name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase));
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
