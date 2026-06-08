using System.Collections.ObjectModel;
using AdminConsoleFor1C.Application.Services;
using AdminConsoleFor1C.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentIcons.Common;
using Microsoft.UI.Xaml;

namespace AdminConsoleFor1C.App;

public sealed partial class AgentsPageViewModel : ObservableObject
{
    private readonly IOneCServiceInventory serviceInventory;
    private readonly IOneCProcessInventory processInventory;

    public AgentsPageViewModel(
        IOneCServiceInventory serviceInventory,
        IOneCProcessInventory processInventory)
    {
        this.serviceInventory = serviceInventory;
        this.processInventory = processInventory;
    }

    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(RefreshProgressVisibility))]
    [NotifyPropertyChangedFor(nameof(EmptyStateVisibility))]
    [ObservableProperty]
    public partial bool IsRefreshing { get; set; }

    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(EmptyStateVisibility))]
    [ObservableProperty]
    public partial bool HasLoaded { get; set; }

    [NotifyPropertyChangedFor(nameof(StatusText))]
    [ObservableProperty]
    public partial int ServiceCount { get; set; }

    [NotifyPropertyChangedFor(nameof(StatusText))]
    [ObservableProperty]
    public partial int ProcessCount { get; set; }

    [NotifyPropertyChangedFor(nameof(HasError))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(EmptyStateVisibility))]
    [ObservableProperty]
    public partial string? ErrorText { get; set; }

    public ObservableCollection<AgentComponentItemViewModel> Components { get; } = [];

    public string PageTitle => "Компоненты сервера";

    public string PageSubtitle => "Службы Windows и процессы 1С на этом компьютере";

    public string StatusText
    {
        get
        {
            if (IsRefreshing)
            {
                return "Обновление списка компонентов";
            }

            if (HasError)
            {
                return "Не удалось обновить сведения о компонентах сервера";
            }

            if (!HasLoaded)
            {
                return "Сведения о компонентах еще не загружены";
            }

            return Components.Count == 0
                ? "Нет данных о компонентах сервера"
                : $"{AgentComponentTextFormatter.FormatFoundServices(ServiceCount)}, {AgentComponentTextFormatter.FormatProcessCount(ProcessCount)}";
        }
    }

    public string ComponentsStatusText => IsRefreshing ? "Обновление" : "Нет данных";

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);

    public Visibility RefreshProgressVisibility => IsRefreshing
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility ComponentsListVisibility => Components.Count > 0
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility EmptyStateVisibility => HasLoaded && !IsRefreshing && !HasError && Components.Count == 0
        ? Visibility.Visible
        : Visibility.Collapsed;

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync()
    {
        IsRefreshing = true;

        try
        {
            ErrorText = null;

            var servicesTask = serviceInventory.GetServicesAsync();
            var processesTask = processInventory.GetProcessesAsync();

            await Task.WhenAll(servicesTask, processesTask);

            var services = await servicesTask;
            var processes = await processesTask;
            var components = BuildComponents(services, processes);

            Components.Clear();
            foreach (var component in components)
            {
                Components.Add(component);
            }

            ServiceCount = services.Count;
            ProcessCount = processes.Count;
            HasLoaded = true;
            NotifyComponentStateChanged();
        }
        catch (Exception exception)
        {
            ErrorText = exception.Message;
            HasLoaded = true;
            NotifyComponentStateChanged();
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    private bool CanRefresh() => !IsRefreshing;

    partial void OnIsRefreshingChanged(bool value)
    {
        RefreshCommand.NotifyCanExecuteChanged();
    }

    private static IReadOnlyList<AgentComponentItemViewModel> BuildComponents(
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

            components.Add(AgentComponentItemViewModel.FromService(service, relatedProcesses));
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

            components.Add(AgentComponentItemViewModel.FromProcess(process, relatedProcesses));
        }

        foreach (var process in processes.Where(process => !assignedProcessIds.Contains(process.ProcessId)))
        {
            assignedProcessIds.Add(process.ProcessId);
            components.Add(AgentComponentItemViewModel.FromProcess(process, [process]));
        }

        var orderedComponents = components
            .OrderBy(static component => component.SortOrder)
            .ThenBy(static component => component.Title, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(static component => component.SummaryDescription, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if (orderedComponents.Count == 1)
        {
            orderedComponents[0].IsExpanded = true;
        }

        return orderedComponents;
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

    private void NotifyComponentStateChanged()
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(ComponentsListVisibility));
        OnPropertyChanged(nameof(EmptyStateVisibility));
        OnPropertyChanged(nameof(ComponentsStatusText));
    }
}

public sealed partial class AgentComponentItemViewModel : ObservableObject
{
    public required string Title { get; init; }

    public required string SummaryDescription { get; init; }

    public required string StatusText { get; init; }

    public required string ProcessSummaryText { get; init; }

    public required int SortOrder { get; init; }

    public required Icon Icon { get; init; }

    public required IReadOnlyList<AgentComponentDetailItemViewModel> Details { get; init; }

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    public static AgentComponentItemViewModel FromService(
        OneCServiceInfo service,
        IReadOnlyList<OneCProcessInfo> processes)
    {
        return new AgentComponentItemViewModel
        {
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
            Details = BuildServiceDetails(service, processes)
        };
    }

    public static AgentComponentItemViewModel FromProcess(
        OneCProcessInfo process,
        IReadOnlyList<OneCProcessInfo> relatedProcesses)
    {
        return new AgentComponentItemViewModel
        {
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
            Details = BuildProcessDetails(process, relatedProcesses)
        };
    }

    private static IReadOnlyList<AgentComponentDetailItemViewModel> BuildServiceDetails(
        OneCServiceInfo service,
        IReadOnlyList<OneCProcessInfo> processes)
    {
        var details = new List<AgentComponentDetailItemViewModel>();

        AddDetail(details, "Службы Windows", "Отображаемое имя службы", service.DisplayNameText, Icon.ServiceBell, includeEmpty: true);
        AddDetail(details, "Диспетчер задач", "Системное имя службы", service.Name, Icon.TaskListSquare, includeEmpty: true);
        AddDetail(details, "Запуск", "Тип запуска службы Windows", service.StartModeDisplayName, Icon.ArrowClockwise);
        AddDetail(details, "Учетная запись", "Пользователь службы", service.AccountText, Icon.Person);
        AddDetail(details, "PID службы", "Идентификатор процесса службы", service.ProcessIdText, Icon.NumberSymbol);
        AddDetail(details, "Версия", "Версия платформы 1С", service.VersionText, Icon.AppGeneric);
        AddDetail(details, "Порты", "Порты 1С по назначению", service.PortsText, Icon.SerialPort);

        var processNames = FormatProcessNames(processes);
        if (!string.IsNullOrWhiteSpace(processNames))
        {
            AddDetail(details, "Процессы", AgentComponentTextFormatter.FormatProcessCount(processes.Count), processNames, Icon.AppsListDetail);
        }

        AddDetail(details, "Каталог данных", "Рабочий каталог сервера", service.DataDirectoryText, Icon.Folder);
        AddDetail(details, "Исполняемый файл", "Путь к файлу службы", service.ExecutablePathText, Icon.AppFolder);
        AddDetail(details, "Аргументы", "Параметры запуска", service.ArgumentsText, Icon.Code);

        return details;
    }

    private static IReadOnlyList<AgentComponentDetailItemViewModel> BuildProcessDetails(
        OneCProcessInfo process,
        IReadOnlyList<OneCProcessInfo> relatedProcesses)
    {
        var details = new List<AgentComponentDetailItemViewModel>();

        AddDetail(details, "Процесс", "Имя исполняемого файла", process.Name, Icon.AppGeneric, includeEmpty: true);
        AddDetail(details, "PID", "Идентификатор процесса", process.ProcessIdText, Icon.NumberSymbol, includeEmpty: true);
        AddDetail(details, "Родитель", "PID родительского процесса", process.ParentProcessIdText, Icon.ArrowFlowUpRightRectangleMultiple);
        AddDetail(details, "Версия", "Версия платформы 1С", process.VersionText, Icon.AppGeneric);
        AddDetail(details, "Владелец", "Пользователь процесса", process.OwnerText, Icon.Person);
        AddDetail(details, "Порты", "Порты 1С по назначению", process.PortsText, Icon.SerialPort);

        var processNames = FormatProcessNames(relatedProcesses);
        if (!string.IsNullOrWhiteSpace(processNames) && relatedProcesses.Count > 1)
        {
            AddDetail(details, "Связанные процессы", AgentComponentTextFormatter.FormatProcessCount(relatedProcesses.Count), processNames, Icon.AppsListDetail);
        }

        AddDetail(details, "Исполняемый файл", "Путь к файлу процесса", process.ExecutablePathText, Icon.AppFolder);
        AddDetail(details, "Аргументы", "Параметры запуска", process.ArgumentsText, Icon.Code);

        return details;
    }

    private static void AddDetail(
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

    private static string FormatProcessNames(IReadOnlyList<OneCProcessInfo> processes)
    {
        return string.Join(
            ", ",
            processes
                .Select(static process => process.Name)
                .Where(static name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase));
    }

}

public sealed record AgentComponentDetailItemViewModel
{
    public required string Title { get; init; }

    public required string Description { get; init; }

    public required string Value { get; init; }

    public required Icon Icon { get; init; }
}

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
