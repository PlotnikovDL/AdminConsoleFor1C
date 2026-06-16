using System.Collections.ObjectModel;
using AdminConsoleFor1C.Application.Services;
using AdminConsoleFor1C.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentIcons.Common;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AdminConsoleFor1C.App;

public sealed partial class AgentsPageViewModel : ObservableObject
{
    private readonly IOneCServiceInventory serviceInventory;
    private readonly IOneCProcessInventory processInventory;
    private readonly IOneCServiceController serviceController;

    public AgentsPageViewModel(
        IOneCServiceInventory serviceInventory,
        IOneCProcessInventory processInventory,
        IOneCServiceController serviceController)
    {
        this.serviceInventory = serviceInventory;
        this.processInventory = processInventory;
        this.serviceController = serviceController;
    }

    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(OverviewStatusText))]
    [NotifyPropertyChangedFor(nameof(HeaderSubtitle))]
    [NotifyPropertyChangedFor(nameof(RefreshProgressVisibility))]
    [NotifyPropertyChangedFor(nameof(EmptyStateVisibility))]
    [ObservableProperty]
    public partial bool IsRefreshing { get; set; }

    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(OverviewStatusText))]
    [NotifyPropertyChangedFor(nameof(HeaderSubtitle))]
    [NotifyPropertyChangedFor(nameof(EmptyStateVisibility))]
    [ObservableProperty]
    public partial bool HasLoaded { get; set; }

    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(OverviewStatusText))]
    [NotifyPropertyChangedFor(nameof(HeaderSubtitle))]
    [ObservableProperty]
    public partial int ServiceCount { get; set; }

    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(OverviewStatusText))]
    [NotifyPropertyChangedFor(nameof(HeaderSubtitle))]
    [ObservableProperty]
    public partial int ProcessCount { get; set; }

    [NotifyPropertyChangedFor(nameof(HasError))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(OverviewStatusText))]
    [NotifyPropertyChangedFor(nameof(HeaderSubtitle))]
    [NotifyPropertyChangedFor(nameof(EmptyStateVisibility))]
    [ObservableProperty]
    public partial string? ErrorText { get; set; }

    [NotifyPropertyChangedFor(nameof(HasServiceOperationMessage))]
    [ObservableProperty]
    public partial string? ServiceOperationMessage { get; set; }

    [ObservableProperty]
    public partial string ServiceOperationTitle { get; set; } = "Управление службой";

    [ObservableProperty]
    public partial InfoBarSeverity ServiceOperationSeverity { get; set; } = InfoBarSeverity.Informational;

    [NotifyPropertyChangedFor(nameof(IsServiceActionRunning))]
    [ObservableProperty]
    public partial string? ActiveServiceComponentId { get; set; }

    public ObservableCollection<AgentComponentItemViewModel> Components { get; } = [];

    [NotifyPropertyChangedFor(nameof(PageTitle))]
    [NotifyPropertyChangedFor(nameof(ComponentPageTitle))]
    [NotifyPropertyChangedFor(nameof(ComponentStatusText))]
    [NotifyPropertyChangedFor(nameof(HeaderBreadcrumbs))]
    [NotifyPropertyChangedFor(nameof(HeaderCurrentTitle))]
    [NotifyPropertyChangedFor(nameof(HeaderSubtitle))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(OverviewVisibility))]
    [NotifyPropertyChangedFor(nameof(ComponentDetailsVisibility))]
    [NotifyPropertyChangedFor(nameof(ProcessDetailsVisibility))]
    [NotifyPropertyChangedFor(nameof(BreadcrumbVisibility))]
    [ObservableProperty]
    public partial AgentComponentItemViewModel? SelectedComponent { get; set; }

    [NotifyPropertyChangedFor(nameof(PageTitle))]
    [NotifyPropertyChangedFor(nameof(ProcessPageTitle))]
    [NotifyPropertyChangedFor(nameof(ProcessStatusText))]
    [NotifyPropertyChangedFor(nameof(HeaderBreadcrumbs))]
    [NotifyPropertyChangedFor(nameof(HeaderCurrentTitle))]
    [NotifyPropertyChangedFor(nameof(HeaderSubtitle))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(OverviewVisibility))]
    [NotifyPropertyChangedFor(nameof(ComponentDetailsVisibility))]
    [NotifyPropertyChangedFor(nameof(ProcessDetailsVisibility))]
    [NotifyPropertyChangedFor(nameof(BreadcrumbVisibility))]
    [ObservableProperty]
    public partial AgentProcessGroupViewModel? SelectedProcessGroup { get; set; }

    private AgentsPageRoute currentRoute = AgentsPageRoute.Overview;
    private bool isChangingRoute;

    private void ChangeRoute(Action change)
    {
        isChangingRoute = true;

        try
        {
            change();
        }
        finally
        {
            isChangingRoute = false;
            NotifyHeaderStateChanged();
        }
    }

    public string PageTitle => SelectedProcessGroup is not null
        ? "Процессы"
        : SelectedComponent?.Title ?? "Компоненты сервера";

    public string OverviewPageTitle => "Компоненты сервера";

    public string OverviewStatusText => GetOverviewStatusText();

    public string ComponentPageTitle => SelectedComponent?.Title ?? "Компонент сервера";

    public string ComponentStatusText => SelectedComponent is null
        ? string.Empty
        : $"{SelectedComponent.StatusText}, {SelectedComponent.ProcessSummaryText}";

    public string ProcessPageTitle => "Процессы";

    public string ProcessStatusText => SelectedProcessGroup is null
        ? string.Empty
        : $"{SelectedProcessGroup.ComponentTitle}: {AgentComponentTextFormatter.FormatProcessCount(SelectedProcessGroup.Processes.Count)}";

    public IReadOnlyList<AgentsBreadcrumbItem> HeaderBreadcrumbs => currentRoute switch
    {
        AgentsPageRoute.Processes when SelectedComponent is not null =>
        [
            new(OverviewPageTitle, AgentsPageRoute.Overview),
            new(SelectedComponent.Title, AgentsPageRoute.ComponentDetails),
            new(ProcessPageTitle, AgentsPageRoute.Processes)
        ],
        AgentsPageRoute.ComponentDetails =>
        [
            new(OverviewPageTitle, AgentsPageRoute.Overview),
            new(ComponentPageTitle, AgentsPageRoute.ComponentDetails)
        ],
        _ => []
    };

    public string HeaderCurrentTitle => currentRoute switch
    {
        AgentsPageRoute.Processes => ProcessPageTitle,
        AgentsPageRoute.ComponentDetails => ComponentPageTitle,
        _ => OverviewPageTitle
    };

    public string HeaderSubtitle => currentRoute switch
    {
        AgentsPageRoute.Processes => ProcessStatusText,
        AgentsPageRoute.ComponentDetails => ComponentStatusText,
        _ => OverviewStatusText
    };

    public Visibility HeaderOverviewTitleVisibility => currentRoute == AgentsPageRoute.Overview
        ? Visibility.Visible
        : Visibility.Collapsed;

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

            if (SelectedProcessGroup is { } processGroup)
            {
                return $"{processGroup.ComponentTitle}: {AgentComponentTextFormatter.FormatProcessCount(processGroup.Processes.Count)}";
            }

            if (SelectedComponent is { } component)
            {
                return $"{component.StatusText}, {component.ProcessSummaryText}";
            }

            return Components.Count == 0
                ? "Нет данных о компонентах сервера"
                : $"{AgentComponentTextFormatter.FormatFoundServices(ServiceCount)}, {AgentComponentTextFormatter.FormatProcessCount(ProcessCount)}";
        }
    }

    public string ComponentsStatusText => IsRefreshing ? "Обновление" : "Нет данных";

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);

    public bool HasServiceOperationMessage => !string.IsNullOrWhiteSpace(ServiceOperationMessage);

    public bool IsServiceActionRunning => !string.IsNullOrWhiteSpace(ActiveServiceComponentId);

    public Visibility RefreshProgressVisibility => IsRefreshing
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility ComponentsListVisibility => Components.Count > 0
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility EmptyStateVisibility => HasLoaded && !IsRefreshing && !HasError && Components.Count == 0
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility OverviewVisibility => SelectedComponent is null && SelectedProcessGroup is null
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility ComponentDetailsVisibility => SelectedComponent is not null && SelectedProcessGroup is null
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility ProcessDetailsVisibility => SelectedProcessGroup is null
        ? Visibility.Collapsed
        : Visibility.Visible;

    public Visibility BreadcrumbVisibility => currentRoute == AgentsPageRoute.Overview
        ? Visibility.Collapsed
        : Visibility.Visible;

    public Visibility SelectedComponentServiceActionsVisibility => SelectedComponent?.HasService == true
        ? Visibility.Visible
        : Visibility.Collapsed;

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync()
    {
        IsRefreshing = true;
        var selectedComponentId = SelectedProcessGroup?.ComponentId ?? SelectedComponent?.Id;
        var restoreProcessDetails = currentRoute == AgentsPageRoute.Processes;

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
            RestoreSelectedComponent(selectedComponentId, restoreProcessDetails);
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
        NotifyServiceControlCommandsCanExecuteChanged();
    }

    partial void OnActiveServiceComponentIdChanged(string? value)
    {
        NotifyServiceControlCommandsCanExecuteChanged();
    }

    partial void OnSelectedComponentChanged(AgentComponentItemViewModel? value)
    {
        SelectedProcessGroup = null;
        NotifyServiceControlCommandsCanExecuteChanged();
        OnPropertyChanged(nameof(SelectedComponentServiceActionsVisibility));
        NotifyHeaderStateChanged();
    }

    partial void OnSelectedProcessGroupChanged(AgentProcessGroupViewModel? value)
    {
        NotifyHeaderStateChanged();
    }

    [RelayCommand]
    private void OpenComponentDetails(AgentComponentItemViewModel component)
    {
        ChangeRoute(() =>
        {
            currentRoute = AgentsPageRoute.ComponentDetails;
            SelectedComponent = component;
        });
    }

    [RelayCommand]
    private void OpenProcessDetails(AgentComponentDetailItemViewModel detail)
    {
        if (detail.NavigationTarget != AgentComponentDetailNavigationTarget.Processes)
        {
            return;
        }

        ShowProcessDetails(detail.ComponentId);
    }

    [RelayCommand]
    private void CloseProcessDetails()
    {
        SelectedProcessGroup = null;
    }

    [RelayCommand(CanExecute = nameof(CanStartSelectedService))]
    private Task StartSelectedServiceAsync()
    {
        return ExecuteSelectedServiceActionAsync(OneCServiceControlAction.Start);
    }

    [RelayCommand(CanExecute = nameof(CanStopSelectedService))]
    private Task StopSelectedServiceAsync()
    {
        return ExecuteSelectedServiceActionAsync(OneCServiceControlAction.Stop);
    }

    [RelayCommand(CanExecute = nameof(CanRestartSelectedService))]
    private Task RestartSelectedServiceAsync()
    {
        return ExecuteSelectedServiceActionAsync(OneCServiceControlAction.Restart);
    }

    private bool CanStartSelectedService()
    {
        return CanRunServiceAction() && SelectedComponent?.CanStartService == true;
    }

    private bool CanStopSelectedService()
    {
        return CanRunServiceAction() && SelectedComponent?.CanStopService == true;
    }

    private bool CanRestartSelectedService()
    {
        return CanRunServiceAction() && SelectedComponent?.CanRestartService == true;
    }

    private Task ExecuteSelectedServiceActionAsync(OneCServiceControlAction action)
    {
        return SelectedComponent is null
            ? Task.CompletedTask
            : ExecuteServiceActionAsync(SelectedComponent, action);
    }

    private bool CanRunServiceAction()
    {
        return !IsRefreshing && !IsServiceActionRunning;
    }

    private async Task ExecuteServiceActionAsync(
        AgentComponentItemViewModel component,
        OneCServiceControlAction action)
    {
        if (string.IsNullOrWhiteSpace(component.ServiceName))
        {
            return;
        }

        ActiveServiceComponentId = component.Id;
        ServiceOperationTitle = GetServiceActionTitle(action);
        ServiceOperationSeverity = InfoBarSeverity.Informational;
        ServiceOperationMessage = $"{GetServiceActionProgressText(action)}: {component.ServiceDisplayName} ({component.ServiceName})";

        try
        {
            await ExecuteServiceControllerActionAsync(action, component.ServiceName);

            ServiceOperationSeverity = InfoBarSeverity.Success;
            ServiceOperationMessage = $"{GetServiceActionSuccessText(action)}: {component.ServiceDisplayName} ({component.ServiceName})";

            await RefreshAsync();
        }
        catch (OperationCanceledException exception)
        {
            ServiceOperationSeverity = InfoBarSeverity.Warning;
            ServiceOperationMessage = exception.Message;
        }
        catch (Exception exception)
        {
            ServiceOperationSeverity = InfoBarSeverity.Error;
            ServiceOperationMessage = exception.Message;
        }
        finally
        {
            ActiveServiceComponentId = null;
        }
    }

    private Task ExecuteServiceControllerActionAsync(
        OneCServiceControlAction action,
        string serviceName)
    {
        return action switch
        {
            OneCServiceControlAction.Start => serviceController.StartAsync(serviceName),
            OneCServiceControlAction.Stop => serviceController.StopAsync(serviceName),
            OneCServiceControlAction.Restart => serviceController.RestartAsync(serviceName),
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
        };
    }

    public bool TryNavigateToBreadcrumb(AgentsBreadcrumbItem item, out AgentsPageRoute route)
    {
        route = item.Route;

        if (route == currentRoute)
        {
            return false;
        }

        if (route == AgentsPageRoute.Overview)
        {
            ChangeRoute(() =>
            {
                currentRoute = AgentsPageRoute.Overview;
                SelectedProcessGroup = null;
                SelectedComponent = null;
            });

            return true;
        }

        if (route == AgentsPageRoute.ComponentDetails && SelectedComponent is not null)
        {
            ChangeRoute(() =>
            {
                currentRoute = AgentsPageRoute.ComponentDetails;
                SelectedProcessGroup = null;
            });

            return true;
        }

        return false;
    }

    private void RestoreSelectedComponent(string? componentId, bool restoreProcessDetails)
    {
        if (componentId is null)
        {
            SelectedComponent = null;
            SelectedProcessGroup = null;
            return;
        }

        var component = FindComponent(componentId);
        SelectedComponent = component;

        if (component is null)
        {
            SelectedProcessGroup = null;
            return;
        }

        if (restoreProcessDetails)
        {
            ShowProcessDetails(component.Id);
        }
    }

    private void ShowProcessDetails(string? componentId)
    {
        if (string.IsNullOrWhiteSpace(componentId))
        {
            return;
        }

        var component = FindComponent(componentId);

        if (component is null || component.Processes.Count == 0)
        {
            SelectedProcessGroup = null;
            return;
        }

        ChangeRoute(() =>
        {
            currentRoute = AgentsPageRoute.Processes;
            SelectedComponent = component;
            SelectedProcessGroup = AgentProcessGroupViewModel.FromComponent(component);
        });
    }

    private AgentComponentItemViewModel? FindComponent(string componentId)
    {
        return Components.FirstOrDefault(candidate => string.Equals(
            candidate.Id,
            componentId,
            StringComparison.Ordinal));
    }

    private string GetOverviewStatusText()
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

    private void NotifyServiceControlCommandsCanExecuteChanged()
    {
        StartSelectedServiceCommand.NotifyCanExecuteChanged();
        StopSelectedServiceCommand.NotifyCanExecuteChanged();
        RestartSelectedServiceCommand.NotifyCanExecuteChanged();
    }

    private static string GetServiceActionTitle(OneCServiceControlAction action)
    {
        return action switch
        {
            OneCServiceControlAction.Start => "Запуск службы",
            OneCServiceControlAction.Stop => "Остановка службы",
            OneCServiceControlAction.Restart => "Перезапуск службы",
            _ => "Управление службой"
        };
    }

    private static string GetServiceActionProgressText(OneCServiceControlAction action)
    {
        return action switch
        {
            OneCServiceControlAction.Start => "Запускается служба Windows",
            OneCServiceControlAction.Stop => "Останавливается служба Windows",
            OneCServiceControlAction.Restart => "Перезапускается служба Windows",
            _ => "Выполняется действие со службой Windows"
        };
    }

    private static string GetServiceActionSuccessText(OneCServiceControlAction action)
    {
        return action switch
        {
            OneCServiceControlAction.Start => "Служба Windows запущена",
            OneCServiceControlAction.Stop => "Служба Windows остановлена",
            OneCServiceControlAction.Restart => "Служба Windows перезапущена",
            _ => "Действие со службой Windows выполнено"
        };
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
        OnPropertyChanged(nameof(OverviewStatusText));
        OnPropertyChanged(nameof(ComponentsListVisibility));
        OnPropertyChanged(nameof(EmptyStateVisibility));
        OnPropertyChanged(nameof(ComponentsStatusText));
        NotifyHeaderStateChanged();
    }

    private void NotifyHeaderStateChanged()
    {
        if (isChangingRoute)
        {
            return;
        }

        OnPropertyChanged(nameof(HeaderBreadcrumbs));
        OnPropertyChanged(nameof(HeaderCurrentTitle));
        OnPropertyChanged(nameof(HeaderSubtitle));
        OnPropertyChanged(nameof(HeaderOverviewTitleVisibility));
        OnPropertyChanged(nameof(BreadcrumbVisibility));
    }

}

public enum AgentsPageRoute
{
    Overview,
    ComponentDetails,
    Processes
}

public sealed record AgentsBreadcrumbItem(string Title, AgentsPageRoute Route)
{
    public override string ToString() => Title;
}

public sealed record AgentComponentItemViewModel
{
    public required string Id { get; init; }

    public required string Title { get; init; }

    public required string SummaryDescription { get; init; }

    public required string StatusText { get; init; }

    public required string ProcessSummaryText { get; init; }

    public required int SortOrder { get; init; }

    public required Icon Icon { get; init; }

    public required IReadOnlyList<AgentComponentDetailItemViewModel> Details { get; init; }

    public required IReadOnlyList<AgentProcessItemViewModel> Processes { get; init; }

    public required string? ServiceName { get; init; }

    public required string ServiceDisplayName { get; init; }

    public required bool CanStartService { get; init; }

    public required bool CanStopService { get; init; }

    public required bool CanRestartService { get; init; }

    public bool HasService => !string.IsNullOrWhiteSpace(ServiceName);

    public static AgentComponentItemViewModel FromService(
        OneCServiceInfo service,
        IReadOnlyList<OneCProcessInfo> processes)
    {
        var componentId = GetServiceComponentId(service);
        var processItems = processes
            .Select(AgentProcessItemViewModel.FromProcess)
            .ToList();

        return new AgentComponentItemViewModel
        {
            Id = componentId,
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
            Details = BuildServiceDetails(componentId, service, processes),
            Processes = processItems,
            ServiceName = service.Name,
            ServiceDisplayName = service.DisplayNameText,
            CanStartService = CanStart(service),
            CanStopService = CanStop(service),
            CanRestartService = CanRestart(service)
        };
    }

    public static AgentComponentItemViewModel FromProcess(
        OneCProcessInfo process,
        IReadOnlyList<OneCProcessInfo> relatedProcesses)
    {
        var componentId = GetProcessComponentId(process);
        var processItems = relatedProcesses
            .Select(AgentProcessItemViewModel.FromProcess)
            .ToList();

        return new AgentComponentItemViewModel
        {
            Id = componentId,
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
            Details = BuildProcessDetails(componentId, process, relatedProcesses),
            Processes = processItems,
            ServiceName = null,
            ServiceDisplayName = "—",
            CanStartService = false,
            CanStopService = false,
            CanRestartService = false
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
        string componentId,
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
            AddDetail(
                details,
                "Процессы",
                AgentComponentTextFormatter.FormatProcessCount(processes.Count),
                processNames,
                Icon.AppsListDetail,
                componentId: componentId,
                navigationTarget: AgentComponentDetailNavigationTarget.Processes,
                actionToolTip: "Открыть процессы");
        }

        AddDetail(details, "Каталог данных", "Рабочий каталог сервера", service.DataDirectoryText, Icon.Folder);
        AddDetail(details, "Исполняемый файл", "Путь к файлу службы", service.ExecutablePathText, Icon.AppFolder);
        AddDetail(details, "Аргументы", "Параметры запуска", service.ArgumentsText, Icon.Code);

        return details;
    }

    private static IReadOnlyList<AgentComponentDetailItemViewModel> BuildProcessDetails(
        string componentId,
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
            AddDetail(
                details,
                "Связанные процессы",
                AgentComponentTextFormatter.FormatProcessCount(relatedProcesses.Count),
                processNames,
                Icon.AppsListDetail,
                componentId: componentId,
                navigationTarget: AgentComponentDetailNavigationTarget.Processes,
                actionToolTip: "Открыть процессы");
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
        bool includeEmpty = false,
        string? componentId = null,
        AgentComponentDetailNavigationTarget navigationTarget = AgentComponentDetailNavigationTarget.None,
        string actionToolTip = "")
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
            Icon = icon,
            ComponentId = componentId,
            NavigationTarget = navigationTarget,
            ActionToolTip = actionToolTip
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

    public required string? ComponentId { get; init; }

    public required AgentComponentDetailNavigationTarget NavigationTarget { get; init; }

    public required string ActionToolTip { get; init; }

    public bool IsClickEnabled => NavigationTarget != AgentComponentDetailNavigationTarget.None;
}

public enum AgentComponentDetailNavigationTarget
{
    None,
    Processes
}

public sealed record AgentProcessGroupViewModel
{
    public required string ComponentId { get; init; }

    public required string ComponentTitle { get; init; }

    public required IReadOnlyList<AgentProcessItemViewModel> Processes { get; init; }

    public static AgentProcessGroupViewModel FromComponent(AgentComponentItemViewModel component)
    {
        return new AgentProcessGroupViewModel
        {
            ComponentId = component.Id,
            ComponentTitle = component.Title,
            Processes = component.Processes
        };
    }
}

public sealed record AgentProcessItemViewModel
{
    public required string Title { get; init; }

    public required string Description { get; init; }

    public required string ProcessIdText { get; init; }

    public required string RoleText { get; init; }

    public required Icon Icon { get; init; }

    public required IReadOnlyList<AgentProcessDetailItemViewModel> Details { get; init; }

    public static AgentProcessItemViewModel FromProcess(OneCProcessInfo process)
    {
        return new AgentProcessItemViewModel
        {
            Title = process.KindDisplayName,
            Description = process.Name,
            ProcessIdText = $"PID {process.ProcessIdText}",
            RoleText = process.RoleText,
            Icon = GetProcessIcon(process.Kind),
            Details = BuildDetails(process)
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

    private static IReadOnlyList<AgentProcessDetailItemViewModel> BuildDetails(OneCProcessInfo process)
    {
        var details = new List<AgentProcessDetailItemViewModel>();

        AddDetail(details, "Процесс", "Имя исполняемого файла", process.Name, Icon.AppGeneric, includeEmpty: true);
        AddDetail(details, "PID", "Идентификатор процесса", process.ProcessIdText, Icon.NumberSymbol, includeEmpty: true);
        AddDetail(details, "Родитель", "PID родительского процесса", process.ParentProcessIdText, Icon.ArrowFlowUpRightRectangleMultiple);
        AddDetail(details, "Роль", "Назначение процесса 1С", process.RoleText, Icon.TaskListSquare);
        AddDetail(details, "Служба Windows", "Сопоставление со службой", process.RelatedServiceText, Icon.ServiceBell);
        AddDetail(details, "Владелец", "Пользователь процесса", process.OwnerText, Icon.Person);
        AddDetail(details, "Версия", "Версия платформы 1С", process.VersionText, Icon.AppGeneric);
        AddDetail(details, "Порты", "Порты 1С по назначению", process.PortsText, Icon.SerialPort);
        AddDetail(details, "Каталог данных", "Рабочий каталог процесса", ToDisplayText(process.DataDirectory), Icon.Folder);
        AddDetail(details, "Исполняемый файл", "Путь к файлу процесса", process.ExecutablePathText, Icon.AppFolder);
        AddDetail(details, "Аргументы", "Параметры запуска", process.ArgumentsText, Icon.Code);

        return details;
    }

    private static string ToDisplayText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "—" : value;
    }

    private static void AddDetail(
        List<AgentProcessDetailItemViewModel> details,
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

        details.Add(new AgentProcessDetailItemViewModel
        {
            Title = title,
            Description = description,
            Value = string.IsNullOrWhiteSpace(value) ? "—" : value,
            Icon = icon
        });
    }
}

public sealed record AgentProcessDetailItemViewModel
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
