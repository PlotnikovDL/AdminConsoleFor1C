using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using AdminConsoleFor1C.Application.Services;
using AdminConsoleFor1C.Core.Services;
using AdminConsoleFor1C.Infrastructure.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace AdminConsoleFor1C.App;

/// <summary>
/// The main content page displayed inside the application window.
/// </summary>
public sealed partial class MainPage : Page
{
    private readonly IOneCServiceInventory _serviceInventory = new WindowsOneCServiceInventory();
    private readonly IOneCProcessInventory _processInventory = new WindowsOneCProcessInventory();
    private readonly IOneCAdministrationToolInventory _administrationToolInventory = new WindowsOneCAdministrationToolInventory();
    private readonly IOneCServiceController _serviceController = new WindowsOneCServiceController();
    private readonly ObservableCollection<OneCServiceProcessNode> _nodes = [];
    private OneCServiceProcessNode? _selectedNode;

    public MainPage()
    {
        InitializeComponent();
        ServiceTreeRepeater.ItemsSource = _nodes;
        AdministrationToolsCard.DataContext = new OneCAdministrationToolDiagnosticsViewModel([], [], []);
        Loaded += MainPage_Loaded;
    }

    private async void MainPage_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= MainPage_Loaded;
        await RefreshServicesAsync();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshServicesAsync();
    }

    private void ToggleNodeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: OneCServiceProcessNode node })
        {
            node.ToggleExpanded();
        }
    }

    private void ServiceNode_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: OneCServiceProcessNode node })
        {
            SelectNode(node);
        }
    }

    private void OpenWindowsServicesButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: OneCServiceProcessNode node })
        {
            OpenWindowsServices(node.WindowsServicesDisplayNameText);
        }
    }

    private void CopyValueButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string value, CommandParameter: string actionText })
        {
            CopyToClipboard(value, actionText);
        }
    }

    private async void StartServiceButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: OneCServiceProcessNode node })
        {
            await ExecuteServiceCommandAsync(node, OneCServiceControlAction.Start);
        }
    }

    private async void StopServiceButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: OneCServiceProcessNode node })
        {
            await ExecuteServiceCommandAsync(node, OneCServiceControlAction.Stop);
        }
    }

    private async void RestartServiceButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: OneCServiceProcessNode node })
        {
            await ExecuteServiceCommandAsync(node, OneCServiceControlAction.Restart);
        }
    }

    private async Task<bool> RefreshServicesAsync()
    {
        RefreshButton.IsEnabled = false;
        RefreshProgress.IsActive = true;
        RefreshProgress.Visibility = Visibility.Visible;
        ErrorInfoBar.IsOpen = false;
        StatusText.Text = "Обновление...";

        try
        {
            var servicesTask = _serviceInventory.GetServicesAsync();
            var processesTask = _processInventory.GetProcessesAsync();

            await Task.WhenAll(servicesTask, processesTask);

            var services = servicesTask.Result;
            var processes = EnrichProcesses(processesTask.Result, services);
            var tools = await _administrationToolInventory.GetToolsAsync(GetKnownExecutablePaths(services, processes));
            var nodes = BuildServiceProcessTree(services, processes);
            var previousServiceName = _selectedNode?.Service?.Name;

            AdministrationToolsCard.DataContext = new OneCAdministrationToolDiagnosticsViewModel(
                tools,
                services,
                processes);

            _nodes.Clear();
            foreach (var node in nodes)
            {
                _nodes.Add(node);
            }

            SelectNode(GetNodeToSelect(previousServiceName));

            UpdateEmptyState();
            StatusText.Text = $"Найдено служб: {services.Count}, процессов: {processes.Count}";
            return true;
        }
        catch (Exception exception)
        {
            ErrorInfoBar.Message = exception.Message;
            ErrorInfoBar.IsOpen = true;
            StatusText.Text = "Ошибка обновления";
            return false;
        }
        finally
        {
            RefreshProgress.IsActive = false;
            RefreshProgress.Visibility = Visibility.Collapsed;
            RefreshButton.IsEnabled = true;
        }
    }

    private void UpdateEmptyState()
    {
        var hasItems = _nodes.Count > 0;

        ServiceTreeRepeater.Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;
        ComponentDetailsCard.Visibility = hasItems && _selectedNode is not null ? Visibility.Visible : Visibility.Collapsed;
        ServicesTable.Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;
        EmptyState.Visibility = hasItems ? Visibility.Collapsed : Visibility.Visible;
    }

    private OneCServiceProcessNode? GetNodeToSelect(string? previousServiceName)
    {
        if (!string.IsNullOrWhiteSpace(previousServiceName))
        {
            var previousNode = _nodes.FirstOrDefault(node => node.Service?.Name == previousServiceName);
            if (previousNode is not null)
            {
                return previousNode;
            }
        }

        return _nodes.FirstOrDefault();
    }

    private void SelectNode(OneCServiceProcessNode? node)
    {
        if (_selectedNode is not null)
        {
            _selectedNode.IsSelected = false;
        }

        _selectedNode = node;

        if (_selectedNode is null)
        {
            ComponentDetailsCard.DataContext = null;
            ComponentDetailsCard.Visibility = Visibility.Collapsed;
            return;
        }

        _selectedNode.IsSelected = true;
        ComponentDetailsCard.DataContext = _selectedNode;
        ComponentDetailsCard.Visibility = Visibility.Visible;
    }

    private static IReadOnlyList<OneCServiceProcessNode> BuildServiceProcessTree(
        IReadOnlyList<OneCServiceInfo> services,
        IReadOnlyList<OneCProcessInfo> processes)
    {
        var nodes = new List<OneCServiceProcessNode>();
        var assignedProcessIds = new HashSet<uint>();

        foreach (var service in services)
        {
            var serviceProcessId = service.ProcessId;
            var serviceProcesses = serviceProcessId is null or 0
                ? []
                : processes
                    .Where(process => process.ProcessId == serviceProcessId.Value
                        || process.ParentProcessId == serviceProcessId.Value)
                    .ToList();

            foreach (var process in serviceProcesses)
            {
                assignedProcessIds.Add(process.ProcessId);
            }

            nodes.Add(new OneCServiceProcessNode
            {
                Service = service,
                Processes = serviceProcesses
            });
        }

        var orphanProcesses = processes
            .Where(process => !assignedProcessIds.Contains(process.ProcessId))
            .ToList();

        if (orphanProcesses.Count > 0)
        {
            nodes.Add(new OneCServiceProcessNode
            {
                Service = null,
                Processes = orphanProcesses
            });
        }

        return nodes;
    }

    private static IReadOnlyCollection<string> GetKnownExecutablePaths(
        IReadOnlyList<OneCServiceInfo> services,
        IReadOnlyList<OneCProcessInfo> processes)
    {
        return services
            .Select(static service => service.ExecutablePath)
            .Concat(processes.Select(static process => process.ExecutablePath))
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(static path => path!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<OneCProcessInfo> EnrichProcesses(
        IReadOnlyList<OneCProcessInfo> processes,
        IReadOnlyList<OneCServiceInfo> services)
    {
        var servicesByProcessId = services
            .Where(static service => service.ProcessId is not null and not 0)
            .GroupBy(static service => service.ProcessId!.Value)
            .ToDictionary(static group => group.Key, static group => group.First());

        var serverAgentServicesByProcessId = servicesByProcessId
            .Where(static pair => pair.Value.Kind == OneCServiceKind.ServerAgent)
            .ToDictionary(static pair => pair.Key, static pair => pair.Value);

        return processes
            .Select(process =>
            {
                if (servicesByProcessId.TryGetValue(process.ProcessId, out var service))
                {
                    return EnrichProcess(process, service, isDirectServiceProcess: true);
                }

                if (process.ParentProcessId is not null
                    && serverAgentServicesByProcessId.TryGetValue(process.ParentProcessId.Value, out var parentAgentService))
                {
                    return EnrichProcess(process, parentAgentService, isDirectServiceProcess: false);
                }

                return process;
            })
            .ToList();
    }

    private static OneCProcessInfo EnrichProcess(
        OneCProcessInfo process,
        OneCServiceInfo service,
        bool isDirectServiceProcess)
    {
        var enriched = process with
        {
            RelatedServiceDisplayName = isDirectServiceProcess
                ? service.KindDisplayName
                : $"{service.KindDisplayName}, родительский процесс",
            Version = string.IsNullOrWhiteSpace(process.Version) ? service.Version : process.Version,
            Owner = string.IsNullOrWhiteSpace(process.Owner) ? service.Account : process.Owner,
            ExecutablePath = isDirectServiceProcess && string.IsNullOrWhiteSpace(process.ExecutablePath)
                ? service.ExecutablePath
                : process.ExecutablePath,
            Arguments = isDirectServiceProcess && string.IsNullOrWhiteSpace(process.Arguments)
                ? service.Arguments
                : process.Arguments
        };

        return process.Kind switch
        {
            OneCProcessKind.ServerAgent => enriched with
            {
                AgentPort = Prefer(process.AgentPort, service.AgentPort),
                ClusterPort = Prefer(process.ClusterPort, service.RegPort),
                DebugServerPort = Prefer(process.DebugServerPort, service.DebugServerPort),
                PortRange = Prefer(process.PortRange, service.PortRange)
            },
            OneCProcessKind.ClusterManager => enriched with
            {
                ClusterPort = Prefer(process.ClusterPort, service.RegPort)
            },
            OneCProcessKind.WorkerProcess => enriched with
            {
                PortRange = Prefer(process.PortRange, service.PortRange)
            },
            OneCProcessKind.AdministrationServer => enriched with
            {
                AdministrationServerPort = Prefer(process.AdministrationServerPort, service.AdministrationServerPort)
            },
            OneCProcessKind.DebugServer => enriched with
            {
                DebugServerPort = Prefer(process.DebugServerPort, service.DebugServerPort)
            },
            _ => enriched
        };
    }

    private static int? Prefer(int? current, int? fallback)
    {
        return current ?? fallback;
    }

    private static string? Prefer(string? current, string? fallback)
    {
        return string.IsNullOrWhiteSpace(current) ? fallback : current;
    }

    private void CopyToClipboard(string value, string actionText)
    {
        if (!IsCopyValueAvailable(value))
        {
            StatusText.Text = $"{actionText}: нет значения";
            return;
        }

        SetClipboardText(value);
        StatusText.Text = $"{actionText}: скопировано";
    }

    private void OpenWindowsServices(string displayName)
    {
        try
        {
            if (IsCopyValueAvailable(displayName))
            {
                SetClipboardText(displayName);
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = "services.msc",
                UseShellExecute = true
            });

            StatusText.Text = IsCopyValueAvailable(displayName)
                ? "Открыто окно служб Windows, имя строки скопировано"
                : "Открыто окно служб Windows";
        }
        catch (Exception exception)
        {
            ErrorInfoBar.Message = exception.Message;
            ErrorInfoBar.IsOpen = true;
            StatusText.Text = "Не удалось открыть службы Windows";
        }
    }

    private static bool IsCopyValueAvailable(string value)
    {
        return !string.IsNullOrWhiteSpace(value) && value != "—";
    }

    private static void SetClipboardText(string value)
    {
        var package = new DataPackage();
        package.SetText(value);
        Clipboard.SetContent(package);
    }

    private async Task ExecuteServiceCommandAsync(OneCServiceProcessNode node, OneCServiceControlAction action)
    {
        var serviceName = node.Service?.Name;
        if (string.IsNullOrWhiteSpace(serviceName))
        {
            return;
        }

        if (!await ConfirmServiceCommandAsync(node, action))
        {
            return;
        }

        SetServiceCommandRunning(true);
        ErrorInfoBar.IsOpen = false;
        StatusText.Text = GetActionProgressText(action);

        try
        {
            switch (action)
            {
                case OneCServiceControlAction.Start:
                    await _serviceController.StartAsync(serviceName);
                    break;
                case OneCServiceControlAction.Stop:
                    await _serviceController.StopAsync(serviceName);
                    break;
                case OneCServiceControlAction.Restart:
                    await _serviceController.RestartAsync(serviceName);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(action), action, null);
            }

            if (await RefreshServicesAsync())
            {
                StatusText.Text = GetActionCompletedText(action);
            }
        }
        catch (Exception exception) when (IsAccessDenied(exception))
        {
            ErrorInfoBar.Title = "Не хватает прав администратора";
            ErrorInfoBar.Message = "Запустите приложение от имени администратора или используйте будущий режим повышенных действий.";
            ErrorInfoBar.IsOpen = true;
            StatusText.Text = "Действие не выполнено";
        }
        catch (Exception exception)
        {
            ErrorInfoBar.Title = "Не удалось выполнить действие со службой";
            ErrorInfoBar.Message = exception.Message;
            ErrorInfoBar.IsOpen = true;
            StatusText.Text = "Действие не выполнено";
        }
        finally
        {
            SetServiceCommandRunning(false);
        }
    }

    private async Task<bool> ConfirmServiceCommandAsync(OneCServiceProcessNode node, OneCServiceControlAction action)
    {
        if (action == OneCServiceControlAction.Start)
        {
            return true;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = action == OneCServiceControlAction.Stop
                ? "Остановить службу 1С?"
                : "Перезапустить службу 1С?",
            Content = $"{node.Service?.DisplayNameText}{Environment.NewLine}{Environment.NewLine}Активные подключения к этому компоненту могут быть прерваны.",
            PrimaryButtonText = action == OneCServiceControlAction.Stop ? "Остановить" : "Перезапустить",
            CloseButtonText = "Отмена",
            DefaultButton = ContentDialogButton.Close
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private void SetServiceCommandRunning(bool isRunning)
    {
        ServiceActionsPanel.IsHitTestVisible = !isRunning;
        ServiceActionsPanel.Opacity = isRunning ? 0.55 : 1;
        ServiceCommandProgress.IsActive = isRunning;
        ServiceCommandProgress.Visibility = isRunning ? Visibility.Visible : Visibility.Collapsed;
        RefreshButton.IsEnabled = !isRunning;
    }

    private static bool IsAccessDenied(Exception exception)
    {
        return exception is UnauthorizedAccessException
            || exception is Win32Exception { NativeErrorCode: 5 }
            || exception.InnerException is Win32Exception { NativeErrorCode: 5 };
    }

    private static string GetActionProgressText(OneCServiceControlAction action)
    {
        return action switch
        {
            OneCServiceControlAction.Start => "Запуск службы...",
            OneCServiceControlAction.Stop => "Остановка службы...",
            OneCServiceControlAction.Restart => "Перезапуск службы...",
            _ => "Выполнение действия..."
        };
    }

    private static string GetActionCompletedText(OneCServiceControlAction action)
    {
        return action switch
        {
            OneCServiceControlAction.Start => "Служба была запущена",
            OneCServiceControlAction.Stop => "Служба была остановлена",
            OneCServiceControlAction.Restart => "Служба была перезапущена",
            _ => "Действие было выполнено"
        };
    }
}
