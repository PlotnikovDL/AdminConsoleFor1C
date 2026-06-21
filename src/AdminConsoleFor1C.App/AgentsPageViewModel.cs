using System.Collections.ObjectModel;
using AdminConsoleFor1C.Application.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AdminConsoleFor1C.App;

public sealed partial class AgentsPageViewModel : ObservableObject
{
    private static readonly TimeSpan RelatedProcessExitTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan RelatedProcessExitPollInterval = TimeSpan.FromMilliseconds(500);

    private readonly IOneCServiceInventory serviceInventory;
    private readonly IOneCProcessInventory processInventory;
    private readonly IOneCServiceController serviceController;
    private readonly AgentComponentPresentationBuilder componentBuilder;

    public AgentsPageViewModel(
        IOneCServiceInventory serviceInventory,
        IOneCProcessInventory processInventory,
        IOneCServiceController serviceController,
        AgentComponentPresentationBuilder componentBuilder)
    {
        this.serviceInventory = serviceInventory;
        this.processInventory = processInventory;
        this.serviceController = serviceController;
        this.componentBuilder = componentBuilder;
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
    [NotifyPropertyChangedFor(nameof(ErrorInfoBarVisibility))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(OverviewStatusText))]
    [NotifyPropertyChangedFor(nameof(HeaderSubtitle))]
    [NotifyPropertyChangedFor(nameof(EmptyStateVisibility))]
    [ObservableProperty]
    public partial string? ErrorText { get; set; }

    [NotifyPropertyChangedFor(nameof(HasServiceOperationMessage))]
    [NotifyPropertyChangedFor(nameof(ServiceOperationInfoBarVisibility))]
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
    [NotifyPropertyChangedFor(nameof(BreadcrumbVisibility))]
    [ObservableProperty]
    public partial AgentComponentItemViewModel? SelectedComponent { get; set; }

    private AgentsPageRoute currentRoute = AgentsPageRoute.Overview;
    private bool isChangingRoute;

    public string PageTitle => SelectedComponent?.Title ?? "Агенты сервера 1С";

    public string OverviewPageTitle => "Агенты сервера 1С";

    public string OverviewStatusText => GetOverviewStatusText();

    public string ComponentPageTitle => SelectedComponent?.Title ?? "Агент сервера";

    public string ComponentStatusText => SelectedComponent is null
        ? string.Empty
        : $"{SelectedComponent.StatusText}, {SelectedComponent.ProcessSummaryText}";

    public IReadOnlyList<AgentsBreadcrumbItem> HeaderBreadcrumbs => currentRoute switch
    {
        AgentsPageRoute.ComponentDetails =>
        [
            new(OverviewPageTitle, AgentsPageRoute.Overview),
            new(ComponentPageTitle, AgentsPageRoute.ComponentDetails)
        ],
        _ => []
    };

    public string HeaderCurrentTitle => currentRoute switch
    {
        AgentsPageRoute.ComponentDetails => ComponentPageTitle,
        _ => OverviewPageTitle
    };

    public string HeaderSubtitle => currentRoute switch
    {
        AgentsPageRoute.ComponentDetails => ComponentStatusText,
        _ => OverviewStatusText
    };

    public Visibility HeaderOverviewTitleVisibility => currentRoute == AgentsPageRoute.Overview
        ? Visibility.Visible
        : Visibility.Collapsed;

    public string PageSubtitle => "Службы Windows агентов сервера 1С на этом компьютере";

    public string StatusText
    {
        get
        {
            if (IsRefreshing)
            {
                return "Обновление списка агентов";
            }

            if (HasError)
            {
                return "Не удалось обновить сведения об агентах сервера";
            }

            if (!HasLoaded)
            {
                return "Сведения об агентах еще не загружены";
            }

            if (SelectedComponent is { } component)
            {
                return $"{component.StatusText}, {component.ProcessSummaryText}";
            }

            return Components.Count == 0
                ? "Нет данных об агентах сервера"
                : $"{AgentComponentTextFormatter.FormatFoundServices(ServiceCount)}, {AgentComponentTextFormatter.FormatProcessCount(ProcessCount)}";
        }
    }

    public string ComponentsStatusText => IsRefreshing ? "Обновление" : "Нет данных об агентах сервера";

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);

    public bool HasServiceOperationMessage => !string.IsNullOrWhiteSpace(ServiceOperationMessage);

    public bool IsServiceActionRunning => !string.IsNullOrWhiteSpace(ActiveServiceComponentId);

    public Visibility ErrorInfoBarVisibility => HasError
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility ServiceOperationInfoBarVisibility => HasServiceOperationMessage
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility RefreshProgressVisibility => IsRefreshing
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility ComponentsListVisibility => Components.Count > 0
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility EmptyStateVisibility => HasLoaded && !IsRefreshing && !HasError && Components.Count == 0
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility OverviewVisibility => SelectedComponent is null
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility ComponentDetailsVisibility => SelectedComponent is not null
        ? Visibility.Visible
        : Visibility.Collapsed;

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
        var selectedComponentId = SelectedComponent?.Id;

        try
        {
            ErrorText = null;

            var servicesTask = serviceInventory.GetServicesAsync();
            var processesTask = processInventory.GetProcessesAsync();

            await Task.WhenAll(servicesTask, processesTask);

            var services = await servicesTask;
            var processes = await processesTask;
            var components = componentBuilder.BuildComponents(services, processes);

            Components.Clear();
            foreach (var component in components)
            {
                Components.Add(component);
            }

            ServiceCount = services.Count;
            ProcessCount = processes.Count;
            HasLoaded = true;
            RestoreSelectedComponent(selectedComponentId);
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
        NotifyServiceControlCommandsCanExecuteChanged();
        OnPropertyChanged(nameof(SelectedComponentServiceActionsVisibility));
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
                SelectedComponent = null;
            });

            return true;
        }

        if (route == AgentsPageRoute.ComponentDetails && SelectedComponent is not null)
        {
            ChangeRoute(() =>
            {
                currentRoute = AgentsPageRoute.ComponentDetails;
            });

            return true;
        }

        return false;
    }

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
            var relatedProcessIds = component.Processes
                .Select(static process => process.ProcessId)
                .Where(static processId => processId != 0)
                .ToHashSet();

            await ExecuteServiceControllerActionAsync(action, component, relatedProcessIds);

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

    private async Task ExecuteServiceControllerActionAsync(
        OneCServiceControlAction action,
        AgentComponentItemViewModel component,
        IReadOnlySet<uint> relatedProcessIds)
    {
        var serviceName = component.ServiceName;
        if (string.IsNullOrWhiteSpace(serviceName))
        {
            return;
        }

        switch (action)
        {
            case OneCServiceControlAction.Start:
                await serviceController.StartAsync(serviceName);
                break;

            case OneCServiceControlAction.Stop:
                await serviceController.StopAsync(serviceName);
                await WaitForRelatedProcessesExitAsync(component, relatedProcessIds);
                break;

            case OneCServiceControlAction.Restart:
                ServiceOperationMessage = $"Останавливается служба Windows: {component.ServiceDisplayName} ({serviceName})";
                await serviceController.StopAsync(serviceName);
                await WaitForRelatedProcessesExitAsync(component, relatedProcessIds);
                ServiceOperationMessage = $"Запускается служба Windows: {component.ServiceDisplayName} ({serviceName})";
                await serviceController.StartAsync(serviceName);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(action), action, null);
        }
    }

    private async Task WaitForRelatedProcessesExitAsync(
        AgentComponentItemViewModel component,
        IReadOnlySet<uint> relatedProcessIds)
    {
        if (relatedProcessIds.Count == 0)
        {
            return;
        }

        var deadline = DateTimeOffset.UtcNow.Add(RelatedProcessExitTimeout);
        ServiceOperationMessage =
            $"Ожидается завершение процессов службы Windows: {FormatProcessIds(relatedProcessIds)}";

        while (true)
        {
            var processes = await processInventory.GetProcessesAsync();
            var remainingProcessIds = processes
                .Select(static process => process.ProcessId)
                .Where(relatedProcessIds.Contains)
                .Order()
                .ToArray();

            if (remainingProcessIds.Length == 0)
            {
                return;
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                throw new TimeoutException(
                    $"Не завершились процессы службы {component.ServiceDisplayName}: {FormatProcessIds(remainingProcessIds)}");
            }

            ServiceOperationMessage =
                $"Ожидается завершение процессов службы Windows: {FormatProcessIds(remainingProcessIds)}";

            await Task.Delay(RelatedProcessExitPollInterval);
        }
    }

    private void RestoreSelectedComponent(string? componentId)
    {
        if (componentId is null)
        {
            SelectedComponent = null;
            return;
        }

        var component = FindComponent(componentId);
        SelectedComponent = component;
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
            return "Обновление списка агентов";
        }

        if (HasError)
        {
            return "Не удалось обновить сведения об агентах сервера";
        }

        if (!HasLoaded)
        {
            return "Сведения об агентах еще не загружены";
        }

        return Components.Count == 0
            ? "Нет данных об агентах сервера"
            : $"{AgentComponentTextFormatter.FormatFoundServices(ServiceCount)}, {AgentComponentTextFormatter.FormatProcessCount(ProcessCount)}";
    }

    private void NotifyServiceControlCommandsCanExecuteChanged()
    {
        StartSelectedServiceCommand.NotifyCanExecuteChanged();
        StopSelectedServiceCommand.NotifyCanExecuteChanged();
        RestartSelectedServiceCommand.NotifyCanExecuteChanged();
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

    private static string FormatProcessIds(IEnumerable<uint> processIds)
    {
        return "PID " + string.Join(", ", processIds.Order());
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
}

public enum AgentsPageRoute
{
    Overview,
    ComponentDetails
}

public sealed record AgentsBreadcrumbItem(string Title, AgentsPageRoute Route)
{
    public override string ToString() => Title;
}
