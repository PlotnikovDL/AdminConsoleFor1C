using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using AdminConsoleFor1C.Core.Administration;
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
    private const string ServiceAccountLocalSystem = "LocalSystem";
    private const string ServiceAccountLocalService = @"NT AUTHORITY\LocalService";
    private const string ServiceAccountNetworkService = @"NT AUTHORITY\NetworkService";
    private const string ServiceAccountCustom = "Другой пользователь...";
    private const int Logon32LogonNetwork = 3;
    private const int Logon32ProviderDefault = 0;

    private readonly IOneCServiceInventory _serviceInventory = new WindowsOneCServiceInventory();
    private readonly IOneCProcessInventory _processInventory = new WindowsOneCProcessInventory();
    private readonly IOneCAdministrationToolInventory _administrationToolInventory = new WindowsOneCAdministrationToolInventory();
    private readonly IOneCClusterInventory _clusterInventory = new RacOneCClusterInventory();
    private readonly IOneCAdministrationServerLauncher _administrationServerLauncher = new RasOneCAdministrationServerLauncher();
    private readonly IOneCServiceController _serviceController = new ElevatedWorkerOneCServiceController();
    private readonly ObservableCollection<OneCServiceProcessNode> _nodes = [];
    private OneCAdministrationToolDiagnosticsViewModel? _administrationToolDiagnostics;
    private OneCServiceProcessNode? _selectedNode;
    private int? _temporaryRasProcessId;

    public MainPage()
    {
        InitializeComponent();
        ServiceTreeRepeater.ItemsSource = _nodes;
        AdministrationToolsCard.DataContext = new OneCAdministrationToolDiagnosticsViewModel([], [], []);
        ServerAgentSetupCard.DataContext = new OneCServerAgentSetupViewModel(
            new OneCAdministrationToolDiagnosticsViewModel([], [], []),
            [],
            []);
        var initialClusterDiagnostics = new OneCClusterDiagnosticsViewModel(
            CreateUnavailableClusterResult("localhost:1545", "Данные еще не обновлены"),
            "localhost:1540",
            canStartTemporaryRas: false,
            canStopTemporaryRas: false);
        ClustersCard.DataContext = initialClusterDiagnostics;
        LicensesCard.DataContext = initialClusterDiagnostics;
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

    private async void DeleteServiceButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: OneCServiceProcessNode node })
        {
            await ExecuteServiceCommandAsync(node, OneCServiceControlAction.Delete);
        }
    }

    private async void StartTemporaryRasButton_Click(object sender, RoutedEventArgs e)
    {
        await StartTemporaryRasAsync();
    }

    private async void StopTemporaryRasButton_Click(object sender, RoutedEventArgs e)
    {
        await StopTemporaryRasAsync();
    }

    private async void OpenCreateInfobaseDialogButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: OneCClusterViewModel cluster })
        {
            await ShowCreateInfobaseDraftDialogAsync(cluster);
        }
    }

    private async void ConfigureServerAgentServiceButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: OneCServerAgentSetupCandidateViewModel candidate })
        {
            await ShowServerAgentServiceDialogAsync(candidate);
        }
    }

    private void InfobaseSessionsToggleSwitch_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch { Tag: OneCInfobaseSummaryInfo infobase } toggleSwitch)
        {
            toggleSwitch.Toggled -= InfobaseSessionsToggleSwitch_Toggled;
            toggleSwitch.IsOn = infobase.AreSessionsAllowed;
            toggleSwitch.Toggled += InfobaseSessionsToggleSwitch_Toggled;
        }
    }

    private void InfobaseSessionsToggleSwitch_Unloaded(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch toggleSwitch)
        {
            toggleSwitch.Toggled -= InfobaseSessionsToggleSwitch_Toggled;
        }
    }

    private void InfobaseScheduledJobsToggleSwitch_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch { Tag: OneCInfobaseSummaryInfo infobase } toggleSwitch)
        {
            toggleSwitch.Toggled -= InfobaseScheduledJobsToggleSwitch_Toggled;
            toggleSwitch.IsOn = infobase.AreScheduledJobsAllowed;
            toggleSwitch.Toggled += InfobaseScheduledJobsToggleSwitch_Toggled;
        }
    }

    private void InfobaseScheduledJobsToggleSwitch_Unloaded(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch toggleSwitch)
        {
            toggleSwitch.Toggled -= InfobaseScheduledJobsToggleSwitch_Toggled;
        }
    }

    private async void InfobaseSessionsToggleSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch { Tag: OneCInfobaseSummaryInfo infobase } toggleSwitch)
        {
            var desiredAllowed = toggleSwitch.IsOn;
            if (desiredAllowed == infobase.AreSessionsAllowed)
            {
                return;
            }

            var targetValue = desiredAllowed ? "off" : "on";
            var title = desiredAllowed
                ? "Разрешить новые сеансы?"
                : "Запретить новые сеансы?";
            var description = desiredAllowed
                ? "Пользователи снова смогут открывать новые сеансы этой информационной базы."
                : "Пользователи не смогут открывать новые сеансы. Уже открытые сеансы не будут завершены автоматически.";

            var isUpdated = await ExecuteInfobaseRestrictionsUpdateAsync(
                infobase,
                sessionsDeny: targetValue,
                scheduledJobsDeny: null,
                title,
                description,
                completedText: desiredAllowed
                    ? "Новые сеансы были разрешены"
                    : "Новые сеансы были запрещены");
            if (!isUpdated)
            {
                toggleSwitch.Toggled -= InfobaseSessionsToggleSwitch_Toggled;
                toggleSwitch.IsOn = infobase.AreSessionsAllowed;
                toggleSwitch.Toggled += InfobaseSessionsToggleSwitch_Toggled;
            }
        }
    }

    private async void InfobaseScheduledJobsToggleSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch { Tag: OneCInfobaseSummaryInfo infobase } toggleSwitch)
        {
            var desiredAllowed = toggleSwitch.IsOn;
            if (desiredAllowed == infobase.AreScheduledJobsAllowed)
            {
                return;
            }

            var targetValue = desiredAllowed ? "off" : "on";
            var title = desiredAllowed
                ? "Разрешить регламентные задания?"
                : "Блокировать регламентные задания?";
            var description = desiredAllowed
                ? "Фоновые и регламентные задания снова смогут выполняться для этой информационной базы."
                : "Регламентные задания будут заблокированы для этой информационной базы.";

            var isUpdated = await ExecuteInfobaseRestrictionsUpdateAsync(
                infobase,
                sessionsDeny: null,
                scheduledJobsDeny: targetValue,
                title,
                description,
                completedText: desiredAllowed
                    ? "Регламентные задания были разрешены"
                    : "Регламентные задания были заблокированы");
            if (!isUpdated)
            {
                toggleSwitch.Toggled -= InfobaseScheduledJobsToggleSwitch_Toggled;
                toggleSwitch.IsOn = infobase.AreScheduledJobsAllowed;
                toggleSwitch.Toggled += InfobaseScheduledJobsToggleSwitch_Toggled;
            }
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
            var administrationToolDiagnostics = new OneCAdministrationToolDiagnosticsViewModel(
                tools,
                services,
                processes);
            var serverAgentSetup = new OneCServerAgentSetupViewModel(
                administrationToolDiagnostics,
                services,
                processes);
            var clusterDiagnostics = await GetClusterDiagnosticsAsync(administrationToolDiagnostics);
            var nodes = BuildServiceProcessTree(services, processes);
            var previousServiceName = _selectedNode?.Service?.Name;

            _administrationToolDiagnostics = administrationToolDiagnostics;
            AdministrationToolsCard.DataContext = administrationToolDiagnostics;
            ServerAgentSetupCard.DataContext = serverAgentSetup;
            ClustersCard.DataContext = clusterDiagnostics;
            LicensesCard.DataContext = clusterDiagnostics;

            _nodes.Clear();
            foreach (var node in nodes)
            {
                _nodes.Add(node);
            }

            SelectNode(GetNodeToSelect(previousServiceName));

            UpdateEmptyState();
            StatusText.Text = serverAgentSetup.IsVisible && services.All(static service => service.Kind != OneCServiceKind.ServerAgent)
                ? "Найдены компоненты сервера 1С без службы Windows"
                : $"Найдено служб: {services.Count}, процессов: {processes.Count}";
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
        var hasServerAgentSetup = ServerAgentSetupCard.DataContext is OneCServerAgentSetupViewModel { IsVisible: true };
        var hasLeftContent = hasItems || hasServerAgentSetup;

        ServiceTreeRepeater.Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;
        ComponentDetailsCard.Visibility = hasItems && _selectedNode is not null ? Visibility.Visible : Visibility.Collapsed;
        ServicesTable.Visibility = hasLeftContent ? Visibility.Visible : Visibility.Collapsed;
        EmptyState.Visibility = hasLeftContent ? Visibility.Collapsed : Visibility.Visible;
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

    private async Task<OneCClusterDiagnosticsViewModel> GetClusterDiagnosticsAsync(
        OneCAdministrationToolDiagnosticsViewModel administrationTools)
    {
        var address = administrationTools.AdministrationServerAddress;
        var canStopTemporaryRas = IsTemporaryRasRunning();
        var canStartTemporaryRas = administrationTools.RasTool is not null
            && administrationTools.IsServerAgentRunning
            && !IsAdministrationServerRunning(administrationTools)
            && !canStopTemporaryRas;

        if (administrationTools.RacTool is null)
        {
            return new OneCClusterDiagnosticsViewModel(
                CreateUnavailableClusterResult(address, "rac.exe не найден"),
                administrationTools.AgentAddress,
                canStartTemporaryRas,
                canStopTemporaryRas);
        }

        if (!administrationTools.IsServerAgentRunning)
        {
            return new OneCClusterDiagnosticsViewModel(
                CreateUnavailableClusterResult(address, "Агент сервера не запущен"),
                administrationTools.AgentAddress,
                canStartTemporaryRas,
                canStopTemporaryRas);
        }

        if (!IsAdministrationServerRunning(administrationTools))
        {
            return new OneCClusterDiagnosticsViewModel(
                CreateUnavailableClusterResult(address, "RAS не запущен"),
                administrationTools.AgentAddress,
                canStartTemporaryRas,
                canStopTemporaryRas);
        }

        var result = await _clusterInventory.GetClustersAsync(administrationTools.RacTool.FilePath, address);
        return new OneCClusterDiagnosticsViewModel(
            result,
            administrationTools.AgentAddress,
            canStartTemporaryRas,
            canStopTemporaryRas);
    }

    private static OneCClusterInventoryResult CreateUnavailableClusterResult(
        string administrationServerAddress,
        string message)
    {
        return OneCClusterInventoryResult.Unavailable(
            administrationServerAddress,
            $"rac.exe {administrationServerAddress} cluster list",
            message);
    }

    private static bool IsAdministrationServerRunning(OneCAdministrationToolDiagnosticsViewModel administrationTools)
    {
        return administrationTools.RasProcess is not null
            || administrationTools.RasService?.State == "Running";
    }

    private bool IsTemporaryRasRunning()
    {
        if (_temporaryRasProcessId is null)
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById(_temporaryRasProcessId.Value);
            if (!process.HasExited)
            {
                return true;
            }
        }
        catch (ArgumentException)
        {
        }
        catch (InvalidOperationException)
        {
        }

        _temporaryRasProcessId = null;
        return false;
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

    private async Task ShowServerAgentServiceDialogAsync(OneCServerAgentSetupCandidateViewModel candidate)
    {
        var serviceNameTextBox = CreateReadOnlyDialogTextBox(candidate.ExpectedServiceNameText);
        var ragentPathTextBox = CreateReadOnlyDialogTextBox(candidate.RagentPathText);
        var dataDirectoryTextBox = new TextBox
        {
            Width = 420,
            Height = 34,
            Text = candidate.SuggestedServiceDataDirectory
        };
        var agentPortTextBox = CreatePortTextBox(candidate.AgentPortText);
        var clusterPortTextBox = CreatePortTextBox(candidate.ClusterPortText);
        var processRangeTextBox = new TextBox
        {
            Width = 420,
            Height = 34,
            Text = candidate.ProcessRangeText
        };
        var debugModeComboBox = CreateServiceDialogComboBox(
            "Выключен",
            "TCP",
            "HTTP");
        var debugServerPortTextBox = CreatePortTextBox(candidate.DebugServerPortText);
        var serviceUserComboBox = CreateServiceDialogComboBox(GetServiceAccountOptions());
        serviceUserComboBox.Width = 320;
        serviceUserComboBox.HorizontalAlignment = HorizontalAlignment.Left;
        var serviceUserPanel = new Grid
        {
            Width = 420,
            Children =
            {
                serviceUserComboBox
            }
        };
        var serviceCustomUserTextBox = CreateOptionalDialogTextBox(@"DOMAIN\user или .\user");
        serviceCustomUserTextBox.Width = 420;
        serviceCustomUserTextBox.HorizontalAlignment = HorizontalAlignment.Left;
        var servicePasswordBox = new PasswordBox
        {
            Width = 260,
            Height = 34,
            PlaceholderText = "не задавать"
        };
        var checkServicePasswordButton = new Button
        {
            Content = "Проверить",
            Height = 34,
            Width = 132
        };
        var servicePasswordStatusTextBlock = new TextBlock
        {
            LineHeight = 18,
            TextWrapping = TextWrapping.WrapWholeWords,
            Visibility = Visibility.Collapsed
        };
        var servicePasswordPanel = new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        servicePasswordBox,
                        checkServicePasswordButton
                    }
                },
                servicePasswordStatusTextBlock
            }
        };
        var commandPreviewTextBox = new TextBox
        {
            AcceptsReturn = true,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Mono"),
            FontSize = 12,
            Height = 108,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap
        };
        var validationTextBlock = new TextBlock
        {
            Foreground = GetThemeBrush("SystemFillColorCriticalBrush", 255, 196, 43, 28),
            LineHeight = 20,
            TextWrapping = TextWrapping.WrapWholeWords,
            Visibility = Visibility.Collapsed
        };
        var hintTextBlock = new TextBlock
        {
            Foreground = GetThemeBrush("TextFillColorSecondaryBrush", 255, 96, 96, 96),
            LineHeight = 20,
            Text = "Сейчас ragent.exe уже запущен без службы. Перед запуском созданной службы этот процесс нужно остановить.",
            TextWrapping = TextWrapping.WrapWholeWords,
            Visibility = candidate.IsRunningWithoutService ? Visibility.Visible : Visibility.Collapsed
        };

        var formGrid = new Grid
        {
            ColumnSpacing = 12,
            RowSpacing = 6,
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(210) },
                new ColumnDefinition { Width = new GridLength(420) }
            }
        };

        var row = 0;
        AddDialogFormRow(formGrid, row++, "Имя службы Windows:", serviceNameTextBox);
        AddDialogFormRow(formGrid, row++, "ragent.exe:", ragentPathTextBox);
        AddDialogFormRow(formGrid, row++, "Каталог данных:", dataDirectoryTextBox);
        AddDialogFormRow(formGrid, row++, "Порт агента:", agentPortTextBox);
        AddDialogFormRow(formGrid, row++, "Порт кластера:", clusterPortTextBox);
        AddDialogFormRow(formGrid, row++, "Диапазон процессов:", processRangeTextBox);
        AddDialogFormRow(formGrid, row++, "Режим отладки:", debugModeComboBox);
        var debugServerPortLabel = AddDialogFormRow(formGrid, row, "Порт сервера отладки:", debugServerPortTextBox);

        var serviceAccountGrid = new Grid
        {
            ColumnSpacing = 12,
            RowSpacing = 6,
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(210) },
                new ColumnDefinition { Width = new GridLength(420) }
            }
        };
        var serviceAccountRow = 0;
        AddDialogFormRow(serviceAccountGrid, serviceAccountRow++, "Пользователь службы:", serviceUserPanel);
        var serviceCustomUserLabel = AddDialogFormRow(serviceAccountGrid, serviceAccountRow++, "Имя пользователя:", serviceCustomUserTextBox);
        var servicePasswordLabel = AddDialogFormRow(serviceAccountGrid, serviceAccountRow, "Пароль пользователя:", servicePasswordPanel);

        var formCard = new Border
        {
            Padding = new Thickness(12),
            Background = GetThemeBrush("CardBackgroundFillColorDefaultBrush", 255, 255, 255, 255),
            BorderBrush = GetThemeBrush("CardStrokeColorDefaultBrush", 64, 0, 0, 0),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    CreateDialogSectionTitle("Параметры службы Windows"),
                    hintTextBlock,
                    formGrid,
                    CreateDialogSectionTitle("Учетная запись службы Windows"),
                    serviceAccountGrid,
                    validationTextBlock
                }
            }
        };
        var commandExpander = new Expander
        {
            Header = "Команда регистрации",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsExpanded = false,
            Content = new Border
            {
                Padding = new Thickness(12),
                Child = commandPreviewTextBox
            }
        };
        var contentPanel = new StackPanel
        {
            Width = 680,
            Spacing = 12,
            Children =
            {
                formCard,
                commandExpander
            }
        };
        var content = new ScrollViewer
        {
            Content = contentPanel,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            MaxHeight = 620,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        ContentDialog? dialog = null;
        ServerAgentServiceRegistrationDraft? draftToRegister = null;

        bool IsCustomServiceAccountSelected()
        {
            return string.Equals(GetSelectedText(serviceUserComboBox), ServiceAccountCustom, StringComparison.Ordinal);
        }

        bool IsNamedServiceAccountSelected()
        {
            return !IsBuiltInServiceAccount(GetSelectedText(serviceUserComboBox));
        }

        void SetServicePasswordStatus(string message, bool isSuccess)
        {
            servicePasswordStatusTextBlock.Foreground = isSuccess
                ? GetThemeBrush("SystemFillColorSuccessBrush", 255, 16, 124, 65)
                : GetThemeBrush("SystemFillColorCriticalBrush", 255, 196, 43, 28);
            servicePasswordStatusTextBlock.Text = message;
            servicePasswordStatusTextBlock.Visibility = Visibility.Visible;
        }

        void ClearServicePasswordStatus()
        {
            servicePasswordStatusTextBlock.Text = string.Empty;
            servicePasswordStatusTextBlock.Visibility = Visibility.Collapsed;
        }

        bool TryGetServiceCredentials(
            bool validatePassword,
            out string serviceUser,
            out string servicePassword,
            out string validationMessage)
        {
            validationMessage = string.Empty;
            servicePassword = servicePasswordBox.Password;
            if (!IsCustomServiceAccountSelected())
            {
                serviceUser = GetSelectedText(serviceUserComboBox);
                if (validatePassword
                    && IsNamedServiceAccountSelected()
                    && string.IsNullOrWhiteSpace(servicePassword))
                {
                    validationMessage = "Укажите пароль пользователя службы.";
                    return false;
                }

                return true;
            }

            serviceUser = NormalizeFormValue(serviceCustomUserTextBox.Text) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(serviceUser))
            {
                validationMessage = "Укажите имя пользователя службы.";
                return false;
            }

            if (validatePassword && string.IsNullOrWhiteSpace(servicePassword))
            {
                validationMessage = "Укажите пароль пользователя службы.";
                return false;
            }

            return true;
        }

        void UpdatePreview()
        {
            if (TryGetServiceCredentials(false, out var serviceUser, out var servicePassword, out _)
                && TryCreateServerAgentServiceRegistrationDraft(
                    candidate,
                    dataDirectoryTextBox.Text,
                    agentPortTextBox.Text,
                    clusterPortTextBox.Text,
                    processRangeTextBox.Text,
                    GetSelectedText(debugModeComboBox),
                    debugServerPortTextBox.Text,
                    serviceUser,
                    servicePassword,
                    out var draft,
                    out _)
                && draft is not null)
            {
                serviceNameTextBox.Text = draft.ExpectedServiceName;
                commandPreviewTextBox.Text = draft.CommandText;
            }
            else
            {
                commandPreviewTextBox.Text = string.Empty;
            }
        }

        void UpdateServiceAccountControls()
        {
            var isCustom = IsCustomServiceAccountSelected();
            var requiresPassword = IsNamedServiceAccountSelected();
            var customUserVisibility = isCustom ? Visibility.Visible : Visibility.Collapsed;
            var passwordVisibility = requiresPassword ? Visibility.Visible : Visibility.Collapsed;
            serviceCustomUserLabel.Visibility = customUserVisibility;
            serviceCustomUserTextBox.Visibility = customUserVisibility;
            servicePasswordLabel.Visibility = passwordVisibility;
            servicePasswordPanel.Visibility = passwordVisibility;
            serviceCustomUserTextBox.IsEnabled = isCustom;
            servicePasswordBox.IsEnabled = requiresPassword;
            servicePasswordBox.PlaceholderText = requiresPassword ? "пароль Windows" : "не задавать";
            checkServicePasswordButton.IsEnabled = requiresPassword;

            if (!requiresPassword)
            {
                servicePasswordBox.Password = string.Empty;
                ClearServicePasswordStatus();
            }

            if (!isCustom)
            {
                serviceCustomUserTextBox.Text = string.Empty;
            }
        }

        void ShowValidation(string message)
        {
            validationTextBlock.Text = message;
            validationTextBlock.Visibility = Visibility.Visible;
        }

        void HideValidation()
        {
            validationTextBlock.Visibility = Visibility.Collapsed;
        }

        dataDirectoryTextBox.TextChanged += (_, _) => UpdatePreview();
        agentPortTextBox.TextChanged += (_, _) => UpdatePreview();
        clusterPortTextBox.TextChanged += (_, _) => UpdatePreview();
        processRangeTextBox.TextChanged += (_, _) => UpdatePreview();
        debugServerPortTextBox.TextChanged += (_, _) => UpdatePreview();
        serviceUserComboBox.SelectionChanged += (_, _) =>
        {
            UpdateServiceAccountControls();
            UpdatePreview();
        };
        serviceCustomUserTextBox.TextChanged += (_, _) =>
        {
            ClearServicePasswordStatus();
            UpdatePreview();
        };
        servicePasswordBox.PasswordChanged += (_, _) =>
        {
            ClearServicePasswordStatus();
            UpdatePreview();
        };
        checkServicePasswordButton.Click += async (_, _) =>
        {
            if (!TryGetServiceCredentials(true, out var serviceUser, out var servicePassword, out var validationMessage))
            {
                SetServicePasswordStatus(validationMessage, isSuccess: false);
                return;
            }

            checkServicePasswordButton.IsEnabled = false;
            SetServicePasswordStatus("Проверка...", isSuccess: true);
            var result = await Task.Run(() => TryValidateWindowsCredentials(
                serviceUser,
                servicePassword,
                out var message)
                    ? (IsSuccess: true, Message: message)
                    : (IsSuccess: false, Message: message));
            SetServicePasswordStatus(result.Message, result.IsSuccess);
            checkServicePasswordButton.IsEnabled = IsNamedServiceAccountSelected();
        };
        debugModeComboBox.SelectionChanged += (_, _) =>
        {
            UpdateDebugControls();
            UpdatePreview();
        };

        void UpdateDebugControls()
        {
            var isDebugEnabled = GetSelectedText(debugModeComboBox) != "Выключен";
            var visibility = isDebugEnabled ? Visibility.Visible : Visibility.Collapsed;
            debugServerPortLabel.Visibility = visibility;
            debugServerPortTextBox.IsEnabled = isDebugEnabled;
            debugServerPortTextBox.Visibility = visibility;
        }

        UpdateDebugControls();
        UpdateServiceAccountControls();
        UpdatePreview();

        dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Создание службы агента 1С",
            Content = content,
            CloseButtonText = "Закрыть",
            DefaultButton = ContentDialogButton.Primary,
            PrimaryButtonText = "Зарегистрировать",
            SecondaryButtonText = "Копировать команду"
        };
        dialog.Resources["ContentDialogMaxWidth"] = 820d;
        dialog.Resources["ContentDialogMinWidth"] = 740d;
        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (!TryGetServiceCredentials(true, out var serviceUser, out var servicePassword, out var validationMessage)
                || !TryCreateServerAgentServiceRegistrationDraft(
                    candidate,
                    dataDirectoryTextBox.Text,
                    agentPortTextBox.Text,
                    clusterPortTextBox.Text,
                    processRangeTextBox.Text,
                    GetSelectedText(debugModeComboBox),
                    debugServerPortTextBox.Text,
                    serviceUser,
                    servicePassword,
                    out var draft,
                    out validationMessage))
            {
                ShowValidation(validationMessage);
                args.Cancel = true;
                return;
            }

            HideValidation();
            draftToRegister = draft;
        };
        dialog.SecondaryButtonClick += (_, args) =>
        {
            args.Cancel = true;
            if (!TryGetServiceCredentials(true, out var serviceUser, out var servicePassword, out var validationMessage)
                || !TryCreateServerAgentServiceRegistrationDraft(
                    candidate,
                    dataDirectoryTextBox.Text,
                    agentPortTextBox.Text,
                    clusterPortTextBox.Text,
                    processRangeTextBox.Text,
                    GetSelectedText(debugModeComboBox),
                    debugServerPortTextBox.Text,
                    serviceUser,
                    servicePassword,
                    out var draft,
                    out validationMessage))
            {
                ShowValidation(validationMessage);
                return;
            }

            if (draft is not null)
            {
                HideValidation();
                CopyToClipboard(draft.CommandText, "Команда регистрации службы");
            }
        };

        await dialog.ShowAsync();
        if (draftToRegister is not null)
        {
            await ExecuteRegisterServerAgentServiceAsync(draftToRegister);
        }
    }

    private async Task ExecuteRegisterServerAgentServiceAsync(ServerAgentServiceRegistrationDraft draft)
    {
        ErrorInfoBar.IsOpen = false;
        StatusText.Text = "Ожидание подтверждения администратора...";

        try
        {
            var scriptPath = WriteRegistrationScript(draft.ScriptText);
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c {QuoteCommandValue(scriptPath)}",
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = Path.GetDirectoryName(scriptPath) ?? string.Empty
            });

            if (process is null)
            {
                StatusText.Text = "Регистрация службы не была запущена";
                return;
            }

            StatusText.Text = "Регистрация службы выполняется...";
            await Task.Run(process.WaitForExit);

            if (process.ExitCode != 0)
            {
                ErrorInfoBar.Title = "Не удалось зарегистрировать службу";
                ErrorInfoBar.Message = $"Команда регистрации завершилась с кодом {process.ExitCode}. Команда была скопирована в буфер обмена.";
                ErrorInfoBar.IsOpen = true;
                CopyToClipboard(draft.CommandText, "Команда регистрации службы");
                return;
            }

            if (await RefreshServicesAsync())
            {
                StatusText.Text = $"Служба была зарегистрирована: {draft.ExpectedServiceName}";
            }
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            StatusText.Text = "Регистрация службы была отменена";
        }
        catch (Exception exception)
        {
            ErrorInfoBar.Title = "Не удалось зарегистрировать службу";
            ErrorInfoBar.Message = exception.Message;
            ErrorInfoBar.IsOpen = true;
            StatusText.Text = "Регистрация службы не была выполнена";
        }
    }

    private static string WriteRegistrationScript(string scriptText)
    {
        var tempDirectory = GetRegistrationTempDirectory();
        Directory.CreateDirectory(tempDirectory);

        var scriptPath = Path.Combine(
            tempDirectory,
            $"register-server-agent-{DateTime.Now:yyyyMMdd-HHmmss}.cmd");
        File.WriteAllText(scriptPath, scriptText, Encoding.UTF8);
        return scriptPath;
    }

    private static string GetRegistrationTempDirectory()
    {
        try
        {
            var localCachePath = Windows.Storage.ApplicationData.Current.LocalCacheFolder.Path;
            if (!string.IsNullOrWhiteSpace(localCachePath))
            {
                return Path.Combine(localCachePath, "AdminConsoleFor1C", "Temp");
            }
        }
        catch (InvalidOperationException)
        {
            // Unpackaged fallback.
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AdminConsoleFor1C",
            "Temp");
    }

    private static bool TryCreateServerAgentServiceRegistrationDraft(
        OneCServerAgentSetupCandidateViewModel candidate,
        string dataDirectoryText,
        string agentPortText,
        string clusterPortText,
        string processRangeText,
        string debugModeText,
        string debugServerPortText,
        string serviceUserText,
        string servicePassword,
        out ServerAgentServiceRegistrationDraft? draft,
        out string validationMessage)
    {
        draft = null;
        validationMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(candidate.RagentPathText)
            || !File.Exists(candidate.RagentPathText))
        {
            validationMessage = "Файл ragent.exe не найден.";
            return false;
        }

        var dataDirectory = NormalizeFormValue(dataDirectoryText);
        if (string.IsNullOrWhiteSpace(dataDirectory))
        {
            validationMessage = "Укажите каталог данных кластера.";
            return false;
        }

        if (!TryReadPort(agentPortText, "Порт агента", out var agentPort, out validationMessage)
            || !TryReadPort(clusterPortText, "Порт кластера", out var clusterPort, out validationMessage)
            || !TryReadProcessRange(processRangeText, out var processRangeStart, out var processRangeEnd, out validationMessage))
        {
            return false;
        }

        var debugMode = GetServerAgentDebugModeValue(debugModeText);
        var debugServerPort = 0;
        if (debugMode is not null
            && !TryReadPort(debugServerPortText, "Порт сервера отладки", out debugServerPort, out validationMessage))
        {
            return false;
        }

        if (!ValidateServerAgentPorts(
                agentPort,
                clusterPort,
                processRangeStart,
                processRangeEnd,
                debugMode is not null ? debugServerPort : null,
                out validationMessage))
        {
            return false;
        }

        var serviceUser = NormalizeFormValue(serviceUserText);
        var normalizedServicePassword = NormalizeFormValue(servicePassword);
        if (string.IsNullOrWhiteSpace(serviceUser) && !string.IsNullOrWhiteSpace(normalizedServicePassword))
        {
            validationMessage = "Пароль пользователя службы можно указать только вместе с пользователем службы.";
            return false;
        }

        var processRange = string.Create(
            CultureInfo.InvariantCulture,
            $"{processRangeStart}:{processRangeEnd}");
        var expectedServiceName = GetServerAgentServiceName(candidate.VersionText, agentPort);
        var expectedServiceDisplayName = expectedServiceName;
        var serviceDescription = GetServerAgentServiceDescription(
            candidate.VersionText,
            agentPort,
            clusterPort,
            processRange);
        var arguments = BuildServerAgentServiceArguments(
            clusterPort,
            agentPort,
            processRange,
            dataDirectory,
            debugMode,
            debugServerPort);
        var scriptText = BuildServerAgentServiceRegistrationScript(
            candidate.RagentPathText,
            arguments,
            dataDirectory,
            expectedServiceName,
            expectedServiceDisplayName,
            serviceDescription,
            serviceUser,
            normalizedServicePassword);

        draft = new ServerAgentServiceRegistrationDraft(
            candidate.RagentPathText,
            scriptText,
            scriptText,
            expectedServiceName,
            serviceDescription);
        return true;
    }

    private static TextBox CreateReadOnlyDialogTextBox(string text)
    {
        return new TextBox
        {
            Width = 420,
            MinHeight = 34,
            IsReadOnly = true,
            Text = text,
            TextWrapping = TextWrapping.Wrap
        };
    }

    private static TextBox CreatePortTextBox(string text)
    {
        return new TextBox
        {
            Width = 420,
            Height = 34,
            Text = text
        };
    }

    private static TextBox CreateOptionalDialogTextBox(string placeholderText)
    {
        return new TextBox
        {
            Width = 420,
            Height = 34,
            PlaceholderText = placeholderText
        };
    }

    private static ComboBox CreateServiceDialogComboBox(params string[] items)
    {
        var comboBox = CreateComboBox(null, items);
        comboBox.Width = 420;
        return comboBox;
    }

    private static string[] GetServiceAccountOptions()
    {
        var accounts = new List<string>
        {
            ServiceAccountLocalSystem,
            ServiceAccountLocalService,
            ServiceAccountNetworkService
        };

        var currentUser = GetCurrentUserServiceAccountName();
        if (!string.IsNullOrWhiteSpace(currentUser))
        {
            accounts.Add(currentUser);
        }

        accounts.Add(ServiceAccountCustom);
        return accounts
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? GetCurrentUserServiceAccountName()
    {
        var userName = Environment.UserName;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var domainName = Environment.UserDomainName;
        if (string.IsNullOrWhiteSpace(domainName)
            || string.Equals(domainName, Environment.MachineName, StringComparison.OrdinalIgnoreCase))
        {
            return $@".\{userName}";
        }

        return $@"{domainName}\{userName}";
    }

    private static bool IsBuiltInServiceAccount(string accountName)
    {
        return string.Equals(accountName, ServiceAccountLocalSystem, StringComparison.OrdinalIgnoreCase)
            || string.Equals(accountName, @"NT AUTHORITY\SYSTEM", StringComparison.OrdinalIgnoreCase)
            || string.Equals(accountName, ServiceAccountLocalService, StringComparison.OrdinalIgnoreCase)
            || string.Equals(accountName, ServiceAccountNetworkService, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryReadPort(string text, string fieldName, out int port, out string validationMessage)
    {
        validationMessage = string.Empty;
        if (!int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out port)
            || port is < 1 or > 65535)
        {
            validationMessage = $"{fieldName}: укажите порт от 1 до 65535.";
            return false;
        }

        return true;
    }

    private static bool TryReadProcessRange(
        string text,
        out int start,
        out int end,
        out string validationMessage)
    {
        start = 0;
        end = 0;
        validationMessage = string.Empty;

        var parts = text.Split([':', '-'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2
            || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out start)
            || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out end)
            || start is < 1 or > 65535
            || end is < 1 or > 65535
            || start > end)
        {
            validationMessage = "Диапазон процессов: укажите диапазон портов, например 2560:2591.";
            return false;
        }

        return true;
    }

    private static bool ValidateServerAgentPorts(
        int agentPort,
        int clusterPort,
        int processRangeStart,
        int processRangeEnd,
        int? debugServerPort,
        out string validationMessage)
    {
        validationMessage = string.Empty;
        var namedPorts = new Dictionary<int, string>
        {
            [agentPort] = "агента",
        };

        if (!namedPorts.TryAdd(clusterPort, "кластера"))
        {
            validationMessage = "Порт агента и порт кластера должны быть разными.";
            return false;
        }

        if (debugServerPort is not null
            && !namedPorts.TryAdd(debugServerPort.Value, "HTTP-отладки"))
        {
            validationMessage = "Порт HTTP-отладки должен отличаться от портов агента и кластера.";
            return false;
        }

        foreach (var (port, name) in namedPorts)
        {
            if (port >= processRangeStart && port <= processRangeEnd)
            {
                validationMessage = $"Порт {name} не должен попадать в диапазон рабочих процессов.";
                return false;
            }
        }

        return true;
    }

    private static string BuildServerAgentServiceArguments(
        int clusterPort,
        int agentPort,
        string processRange,
        string dataDirectory,
        string? debugMode,
        int debugServerPort)
    {
        var builder = new StringBuilder()
            .Append("/srvc /agent")
            .Append(" /regport ")
            .Append(clusterPort.ToString(CultureInfo.InvariantCulture))
            .Append(" /port ")
            .Append(agentPort.ToString(CultureInfo.InvariantCulture))
            .Append(" /range ")
            .Append(processRange)
            .Append(" /d ")
            .Append(QuoteBinPathArgument(dataDirectory));

        if (debugMode is not null)
        {
            builder
                .Append(" /debug ")
                .Append(debugMode)
                .Append(" /debugServerPort ")
                .Append(debugServerPort.ToString(CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    private static string GetServerAgentServiceName(string version, int agentPort)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"1C:Enterprise {GetServerAgentVersionFamily(version)} Server Agent {agentPort} {version}");
    }

    private static string GetServerAgentServiceDescription(
        string version,
        int agentPort,
        int clusterPort,
        string processRange)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"1C:Enterprise {GetServerAgentVersionFamily(version)} Server Agent. Parameters: {version}, ragent port: {agentPort}, rmngr port: {clusterPort}, range: {processRange}.");
    }

    private static string BuildServerAgentServiceRegistrationScript(
        string ragentPath,
        string serviceArguments,
        string dataDirectory,
        string serviceName,
        string displayName,
        string description,
        string? serviceUser,
        string? servicePassword)
    {
        var builder = new StringBuilder()
            .AppendLine("@echo off")
            .AppendLine("setlocal EnableExtensions")
            .Append("if not exist ")
            .Append(QuoteCommandValue(dataDirectory))
            .Append(" mkdir ")
            .Append(QuoteCommandValue(dataDirectory))
            .AppendLine()
            .Append("set BinPath=\"")
            .Append(QuoteBinPathArgument(ragentPath))
            .Append(' ')
            .Append(serviceArguments)
            .AppendLine("\"")
            .Append("set DisplayName=")
            .Append(QuoteCommandValue(displayName))
            .AppendLine()
            .Append("set Description=")
            .Append(QuoteCommandValue(description))
            .AppendLine()
            .Append("sc create ")
            .Append(QuoteCommandValue(serviceName))
            .Append(" binPath= %BinPath% start= auto");

        if (!string.IsNullOrWhiteSpace(serviceUser))
        {
            builder
                .Append(" obj= ")
                .Append(QuoteCommandValue(serviceUser));

            if (!string.IsNullOrWhiteSpace(servicePassword))
            {
                builder
                    .Append(" password= ")
                    .Append(QuoteCommandValue(servicePassword));
            }
        }

        builder
            .Append(" displayname= %DisplayName% depend= Tcpip/Dnscache/lanmanworkstation/lanmanserver")
            .AppendLine()
            .AppendLine("set CreateExitCode=%ERRORLEVEL%")
            .AppendLine("if not \"%CreateExitCode%\"==\"0\" exit /b %CreateExitCode%")
            .Append("sc description ")
            .Append(QuoteCommandValue(serviceName))
            .Append(" %Description%")
            .AppendLine()
            .AppendLine("set DescriptionExitCode=%ERRORLEVEL%")
            .AppendLine("exit /b %DescriptionExitCode%");

        return builder.ToString();
    }

    private static string? GetServerAgentDebugModeValue(string debugModeText)
    {
        return debugModeText switch
        {
            "TCP" => "-tcp",
            "HTTP" => "-http",
            _ => null
        };
    }

    private static string GetServerAgentVersionFamily(string version)
    {
        var parts = version.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length >= 2
            ? string.Create(CultureInfo.InvariantCulture, $"{parts[0]}.{parts[1]}")
            : "8";
    }

    private static string QuoteCommandValue(string value)
    {
        return $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    }

    private static string QuoteBinPathArgument(string value)
    {
        return $"\\\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\\\"";
    }

    private sealed record ServerAgentServiceRegistrationDraft(
        string RagentPath,
        string ScriptText,
        string CommandText,
        string ExpectedServiceName,
        string ServiceDescription);

    private async Task ShowCreateInfobaseDraftDialogAsync(OneCClusterViewModel cluster)
    {
        var infobaseNameTextBox = new TextBox
        {
            Width = 300,
            Height = 34
        };
        var descriptionTextBox = new TextBox
        {
            Width = 300,
            Height = 34
        };
        var securityLevelComboBox = CreateComboBox(
            null,
            "выключено",
            "только соединение",
            "постоянно");
        var dbServerTextBox = new TextBox
        {
            Width = 300,
            Height = 34
        };
        var dbmsComboBox = CreateComboBox(
            null,
            "MS SQL Server",
            "PostgreSQL",
            "IBM DB2",
            "Oracle Database");
        var dbNameTextBox = new TextBox
        {
            Width = 300,
            Height = 34
        };
        var dbUserTextBox = new TextBox
        {
            Width = 300,
            Height = 34
        };
        var dbPasswordBox = new PasswordBox
        {
            Width = 300,
            Height = 34
        };
        var licenseDistributionComboBox = CreateComboBox(
            null,
            "Да",
            "Нет");
        var localeComboBox = CreateComboBox(
            null,
            "русский (Россия)");
        var dateOffsetComboBox = CreateComboBox(
            null,
            "0",
            "2000");
        var createDatabaseCheckBox = new CheckBox
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            MinWidth = 0,
            Padding = new Thickness(0)
        };
        var scheduledJobsDenyCheckBox = new CheckBox
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            MinWidth = 0,
            Padding = new Thickness(0)
        };
        var formGrid = new Grid
        {
            ColumnSpacing = 12,
            RowSpacing = 5,
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(240) },
                new ColumnDefinition { Width = new GridLength(300) }
            }
        };

        var row = 0;
        AddDialogFormRow(formGrid, row++, "Имя:", infobaseNameTextBox);
        AddDialogFormRow(formGrid, row++, "Описание:", descriptionTextBox);
        AddDialogFormRow(formGrid, row++, "Защищенное соединение:", securityLevelComboBox);
        AddDialogFormRow(formGrid, row++, "Сервер баз данных:", dbServerTextBox);
        AddDialogFormRow(formGrid, row++, "Тип СУБД:", dbmsComboBox);
        AddDialogFormRow(formGrid, row++, "База данных:", dbNameTextBox);
        AddDialogFormRow(formGrid, row++, "Пользователь сервера БД:", dbUserTextBox);
        AddDialogFormRow(formGrid, row++, "Пароль пользователя БД:", dbPasswordBox);
        AddDialogFormRow(
            formGrid,
            row++,
            "Разрешить выдачу лицензий\nсервером 1С:Предприятия:",
            licenseDistributionComboBox);
        AddDialogFormRow(formGrid, row++, "Язык (Страна):", localeComboBox);
        AddDialogFormRow(formGrid, row++, "Смещение дат:", dateOffsetComboBox);
        AddDialogCheckBoxRow(
            formGrid,
            row++,
            "Создать базу данных в\nслучае ее отсутствия",
            createDatabaseCheckBox);
        AddDialogCheckBoxRow(
            formGrid,
            row,
            "Установить блокировку\nрегламентных заданий",
            scheduledJobsDenyCheckBox);

        var validationTextBlock = new TextBlock
        {
            Foreground = GetThemeBrush("SystemFillColorCriticalBrush", 255, 196, 43, 28),
            LineHeight = 20,
            TextWrapping = TextWrapping.WrapWholeWords,
            Visibility = Visibility.Collapsed
        };
        var okButton = new Button
        {
            Content = "OK",
            Width = 100,
            Background = GetThemeBrush("AccentFillColorDefaultBrush", 255, 0, 120, 212),
            BorderBrush = GetThemeBrush("AccentFillColorDefaultBrush", 255, 0, 120, 212),
            Foreground = GetThemeBrush("TextOnAccentFillColorPrimaryBrush", 255, 255, 255, 255)
        };
        var cancelButton = new Button
        {
            Content = "Отмена",
            Width = 100
        };
        var footer = new StackPanel
        {
            Margin = new Thickness(0, 12, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                okButton,
                cancelButton
            }
        };

        var formCard = new Border
        {
            Padding = new Thickness(12),
            Background = GetThemeBrush("CardBackgroundFillColorDefaultBrush", 255, 255, 255, 255),
            BorderBrush = GetThemeBrush("CardStrokeColorDefaultBrush", 64, 0, 0, 0),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    CreateDialogSectionTitle("Параметры информационной базы"),
                    formGrid,
                    validationTextBlock
                }
            }
        };
        var confirmationContent = new StackPanel
        {
            Spacing = 8
        };
        var confirmationCard = new Border
        {
            Padding = new Thickness(12),
            Background = GetThemeBrush("CardBackgroundFillColorDefaultBrush", 255, 255, 255, 255),
            BorderBrush = GetThemeBrush("CardStrokeColorDefaultBrush", 64, 0, 0, 0),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Visibility = Visibility.Collapsed,
            Child = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    CreateDialogSectionTitle("Проверьте параметры"),
                    confirmationContent
                }
            }
        };
        var content = new StackPanel
        {
            Width = 590,
            Spacing = 0,
            Children =
            {
                formCard,
                confirmationCard,
                footer
            }
        };

        ContentDialog? dialog = null;
        var isConfirmationStep = false;
        OneCInfobaseCreateRequest? pendingRequest = null;
        OneCInfobaseCreateRequest? requestToCreate = null;
        okButton.Click += (_, _) =>
        {
            if (isConfirmationStep)
            {
                if (pendingRequest is null)
                {
                    return;
                }

                requestToCreate = pendingRequest;
                dialog?.Hide();
                return;
            }

            var request = CreateInfobaseCreateRequest(
                cluster,
                infobaseNameTextBox.Text,
                descriptionTextBox.Text,
                GetSecurityLevelOptionValue(securityLevelComboBox),
                dbServerTextBox.Text,
                GetDbmsOptionValue(dbmsComboBox),
                dbNameTextBox.Text,
                dbUserTextBox.Text,
                dbPasswordBox.Password,
                GetLicenseDistributionOptionValue(licenseDistributionComboBox),
                GetLocaleOptionValue(localeComboBox),
                GetSelectedText(dateOffsetComboBox),
                createDatabaseCheckBox.IsChecked == true,
                scheduledJobsDenyCheckBox.IsChecked == true,
                out var validationMessage);

            if (request is null)
            {
                validationTextBlock.Text = validationMessage;
                validationTextBlock.Visibility = Visibility.Visible;
                return;
            }

            validationTextBlock.Visibility = Visibility.Collapsed;
            pendingRequest = request;
            FillCreateInfobaseConfirmationContent(confirmationContent, request);

            isConfirmationStep = true;
            formCard.Visibility = Visibility.Collapsed;
            confirmationCard.Visibility = Visibility.Visible;
            okButton.Content = "Создать";
            if (dialog is not null)
            {
                dialog.Title = "Создать информационную базу?";
            }
        };
        cancelButton.Click += (_, _) =>
        {
            if (isConfirmationStep)
            {
                pendingRequest = null;
                isConfirmationStep = false;
                confirmationContent.Children.Clear();
                confirmationCard.Visibility = Visibility.Collapsed;
                formCard.Visibility = Visibility.Visible;
                okButton.Content = "OK";
                if (dialog is not null)
                {
                    dialog.Title = "Новая информационная база";
                }

                return;
            }

            dialog?.Hide();
        };

        dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Новая информационная база",
            Content = content,
        };
        dialog.Resources["ContentDialogMaxWidth"] = 700d;
        dialog.Resources["ContentDialogMinWidth"] = 650d;

        await dialog.ShowAsync();
        if (requestToCreate is not null)
        {
            await ExecuteCreateInfobaseAsync(requestToCreate);
        }
    }

    private OneCInfobaseCreateRequest? CreateInfobaseCreateRequest(
        OneCClusterViewModel cluster,
        string name,
        string description,
        string securityLevel,
        string dbServer,
        string dbms,
        string dbName,
        string dbUser,
        string dbPassword,
        string licenseDistribution,
        string locale,
        string dateOffset,
        bool createDatabase,
        bool scheduledJobsDeny,
        out string validationMessage)
    {
        var requiredErrors = new List<string>();
        var normalizedName = NormalizeFormValue(name);
        var normalizedDbServer = NormalizeFormValue(dbServer);
        var normalizedDbName = NormalizeFormValue(dbName);
        var clusterUuid = NormalizeFormValue(cluster.UuidText);

        if (string.IsNullOrWhiteSpace(clusterUuid) || clusterUuid == "—")
        {
            requiredErrors.Add("кластер");
        }

        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            requiredErrors.Add("имя");
        }

        if (string.IsNullOrWhiteSpace(normalizedDbServer))
        {
            requiredErrors.Add("сервер баз данных");
        }

        if (string.IsNullOrWhiteSpace(normalizedDbName))
        {
            requiredErrors.Add("база данных");
        }

        if (requiredErrors.Count > 0)
        {
            validationMessage = $"Заполните: {string.Join(", ", requiredErrors)}.";
            return null;
        }

        validationMessage = string.Empty;
        return new OneCInfobaseCreateRequest
        {
            AdministrationServerAddress = _administrationToolDiagnostics?.AdministrationServerAddress ?? "localhost:1545",
            ClusterUuid = clusterUuid!,
            Name = normalizedName!,
            Description = NormalizeFormValue(description),
            SecurityLevel = NormalizeFormValue(securityLevel),
            DbServer = normalizedDbServer!,
            Dbms = dbms,
            DbName = normalizedDbName!,
            DbUser = NormalizeFormValue(dbUser),
            DbPassword = NormalizeFormValue(dbPassword),
            LicenseDistribution = NormalizeFormValue(licenseDistribution),
            Locale = locale,
            DateOffset = NormalizeFormValue(dateOffset),
            CreateDatabase = createDatabase,
            ScheduledJobsDeny = scheduledJobsDeny ? "on" : "off"
        };
    }

    private static void FillCreateInfobaseConfirmationContent(
        StackPanel content,
        OneCInfobaseCreateRequest request)
    {
        content.Children.Clear();
        content.Children.Add(new TextBlock
        {
            Text = request.Name,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            LineHeight = 20
        });
        content.Children.Add(new TextBlock
        {
            Text = $"СУБД: {GetDbmsDisplayName(request.Dbms)}{Environment.NewLine}Сервер БД: {request.DbServer}{Environment.NewLine}База данных: {request.DbName}",
            LineHeight = 20,
            TextWrapping = TextWrapping.WrapWholeWords
        });

        if (string.Equals(request.DbServer, request.DbName, StringComparison.OrdinalIgnoreCase))
        {
            content.Children.Add(new TextBlock
            {
                Text = "Имя базы данных совпадает с именем сервера. Проверьте, что это именно имя базы, например tradeCRM.",
                Foreground = GetThemeBrush("SystemFillColorCautionBrush", 255, 157, 93, 0),
                LineHeight = 20,
                TextWrapping = TextWrapping.WrapWholeWords
            });
        }

        content.Children.Add(new TextBlock
        {
            Text = request.CreateDatabase
                ? "Если базы данных нет, приложение передаст параметр создания базы в СУБД."
                : "Параметр создания базы данных в СУБД не будет передан.",
            Foreground = GetThemeBrush("TextFillColorSecondaryBrush", 255, 96, 96, 96),
            LineHeight = 20,
            TextWrapping = TextWrapping.WrapWholeWords
        });
    }

    private async Task ExecuteCreateInfobaseAsync(OneCInfobaseCreateRequest request)
    {
        var diagnostics = _administrationToolDiagnostics;
        if (diagnostics?.RacTool is null)
        {
            StatusText.Text = "rac.exe не найден";
            ErrorInfoBar.Title = "Не удалось создать информационную базу";
            ErrorInfoBar.Message = "rac.exe не найден";
            ErrorInfoBar.IsOpen = true;
            return;
        }

        SetClusterCommandRunning(true);
        ErrorInfoBar.IsOpen = false;
        StatusText.Text = "Создание информационной базы...";

        try
        {
            var result = await _clusterInventory.CreateInfobaseAsync(diagnostics.RacTool.FilePath, request);
            if (!result.IsSuccess)
            {
                ErrorInfoBar.Title = "Не удалось создать информационную базу";
                ErrorInfoBar.Message = BuildCreateInfobaseErrorMessage(result);
                ErrorInfoBar.IsOpen = true;
                StatusText.Text = "Информационная база не была создана";
                return;
            }

            if (await RefreshServicesAsync())
            {
                StatusText.Text = $"Информационная база \"{request.Name}\" была создана";
            }
        }
        catch (Exception exception)
        {
            ErrorInfoBar.Title = "Не удалось создать информационную базу";
            ErrorInfoBar.Message = exception.Message;
            ErrorInfoBar.IsOpen = true;
            StatusText.Text = "Информационная база не была создана";
        }
        finally
        {
            SetClusterCommandRunning(false);
        }
    }

    private static string BuildCreateInfobaseErrorMessage(OneCClusterCommandResult result)
    {
        if (result.Message.Contains(
            "принадлежности клиентского и серверного процессов одному компьютеру",
            StringComparison.OrdinalIgnoreCase))
        {
            return string.Join(
                Environment.NewLine,
                result.Message,
                "1С не смогла сопоставить локальный клиент, RAS и сервер 1С как один компьютер. Проверьте, что RAS подключен к агенту по имени компьютера, а не через localhost. После изменения перезапустите временный RAS и повторите создание.");
        }

        return result.Message;
    }

    private async Task<bool> ExecuteInfobaseRestrictionsUpdateAsync(
        OneCInfobaseSummaryInfo infobase,
        string? sessionsDeny,
        string? scheduledJobsDeny,
        string title,
        string description,
        string completedText)
    {
        var diagnostics = _administrationToolDiagnostics;
        if (diagnostics?.RacTool is null)
        {
            StatusText.Text = "rac.exe не найден";
            ErrorInfoBar.Title = "Не удалось обновить информационную базу";
            ErrorInfoBar.Message = "rac.exe не найден";
            ErrorInfoBar.IsOpen = true;
            return false;
        }

        if (string.IsNullOrWhiteSpace(infobase.ClusterUuid) || string.IsNullOrWhiteSpace(infobase.Uuid))
        {
            StatusText.Text = "Информационная база не была обновлена";
            ErrorInfoBar.Title = "Не удалось обновить информационную базу";
            ErrorInfoBar.Message = "Не удалось определить кластер или идентификатор информационной базы.";
            ErrorInfoBar.IsOpen = true;
            return false;
        }

        if (!await ConfirmInfobaseRestrictionsUpdateAsync(infobase, title, description))
        {
            return false;
        }

        var request = new OneCInfobaseRestrictionsUpdateRequest
        {
            AdministrationServerAddress = diagnostics.AdministrationServerAddress,
            ClusterUuid = infobase.ClusterUuid,
            InfobaseUuid = infobase.Uuid,
            InfobaseName = infobase.NameText,
            SessionsDeny = sessionsDeny,
            ScheduledJobsDeny = scheduledJobsDeny
        };

        SetClusterCommandRunning(true);
        ErrorInfoBar.IsOpen = false;
        StatusText.Text = "Обновление информационной базы...";

        try
        {
            var result = await _clusterInventory.UpdateInfobaseRestrictionsAsync(diagnostics.RacTool.FilePath, request);
            if (!result.IsSuccess)
            {
                ErrorInfoBar.Title = "Не удалось обновить информационную базу";
                ErrorInfoBar.Message = result.Message;
                ErrorInfoBar.IsOpen = true;
                StatusText.Text = "Информационная база не была обновлена";
                return false;
            }

            if (await RefreshServicesAsync())
            {
                StatusText.Text = $"{completedText}: {infobase.NameText}";
            }

            return true;
        }
        catch (Exception exception)
        {
            ErrorInfoBar.Title = "Не удалось обновить информационную базу";
            ErrorInfoBar.Message = exception.Message;
            ErrorInfoBar.IsOpen = true;
            StatusText.Text = "Информационная база не была обновлена";
            return false;
        }
        finally
        {
            SetClusterCommandRunning(false);
        }
    }

    private async Task<bool> ConfirmInfobaseRestrictionsUpdateAsync(
        OneCInfobaseSummaryInfo infobase,
        string title,
        string description)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = $"{infobase.NameText}{Environment.NewLine}{Environment.NewLine}{description}",
            PrimaryButtonText = "Выполнить",
            CloseButtonText = "Отмена",
            DefaultButton = ContentDialogButton.Close
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private async Task StartTemporaryRasAsync()
    {
        var diagnostics = _administrationToolDiagnostics;
        if (diagnostics?.RasTool is null)
        {
            StatusText.Text = "ras.exe не найден";
            return;
        }

        SetClusterCommandRunning(true);
        ErrorInfoBar.IsOpen = false;
        StatusText.Text = "Запуск временного RAS...";

        try
        {
            _temporaryRasProcessId = await _administrationServerLauncher.StartTemporaryAsync(
                diagnostics.RasTool.FilePath,
                diagnostics.AdministrationServerPort,
                diagnostics.AgentAddress);

            if (await RefreshServicesAsync())
            {
                StatusText.Text = $"RAS был временно запущен: {diagnostics.AdministrationServerAddress}";
            }
        }
        catch (Exception exception)
        {
            _temporaryRasProcessId = null;
            ErrorInfoBar.Title = "Не удалось запустить RAS";
            ErrorInfoBar.Message = exception.Message;
            ErrorInfoBar.IsOpen = true;
            StatusText.Text = "RAS не был запущен";
        }
        finally
        {
            SetClusterCommandRunning(false);
        }
    }

    private async Task StopTemporaryRasAsync()
    {
        if (!IsTemporaryRasRunning() || _temporaryRasProcessId is null)
        {
            StatusText.Text = "Временный RAS не запущен";
            await RefreshServicesAsync();
            return;
        }

        var processId = _temporaryRasProcessId.Value;

        SetClusterCommandRunning(true);
        ErrorInfoBar.IsOpen = false;
        StatusText.Text = "Остановка временного RAS...";

        try
        {
            await _administrationServerLauncher.StopTemporaryAsync(processId);
            _temporaryRasProcessId = null;

            if (await RefreshServicesAsync())
            {
                StatusText.Text = "Временный RAS был остановлен";
            }
        }
        catch (Exception exception)
        {
            ErrorInfoBar.Title = "Не удалось остановить RAS";
            ErrorInfoBar.Message = exception.Message;
            ErrorInfoBar.IsOpen = true;
            StatusText.Text = "RAS не был остановлен";
        }
        finally
        {
            SetClusterCommandRunning(false);
        }
    }

    private void SetClusterCommandRunning(bool isRunning)
    {
        ClustersCard.IsHitTestVisible = !isRunning;
        ClustersCard.Opacity = isRunning ? 0.65 : 1;
        LicensesCard.IsHitTestVisible = !isRunning;
        LicensesCard.Opacity = isRunning ? 0.65 : 1;
        StartTemporaryRasButton.IsEnabled = !isRunning;
        StopTemporaryRasButton.IsEnabled = !isRunning;
        RefreshButton.IsEnabled = !isRunning;
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
                case OneCServiceControlAction.Delete:
                    await _serviceController.DeleteAsync(serviceName);
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
            ErrorInfoBar.Message = "Подтвердите запрос прав администратора или запустите приложение от имени администратора.";
            ErrorInfoBar.IsOpen = true;
            StatusText.Text = "Действие не выполнено";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Действие было отменено";
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

        var serviceDisplayName = node.Service?.DisplayNameText ?? node.Title;
        var content = action switch
        {
            OneCServiceControlAction.Stop or OneCServiceControlAction.Restart =>
                $"{serviceDisplayName}{Environment.NewLine}{Environment.NewLine}Активные подключения к этому компоненту могут быть прерваны.",
            OneCServiceControlAction.Delete =>
                BuildDeleteServiceConfirmationText(node, serviceDisplayName),
            _ => string.Empty
        };

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = action switch
            {
                OneCServiceControlAction.Stop => "Остановить службу 1С?",
                OneCServiceControlAction.Restart => "Перезапустить службу 1С?",
                OneCServiceControlAction.Delete => "Удалить службу 1С?",
                _ => "Выполнить действие со службой?"
            },
            Content = content,
            PrimaryButtonText = action switch
            {
                OneCServiceControlAction.Stop => "Остановить",
                OneCServiceControlAction.Restart => "Перезапустить",
                OneCServiceControlAction.Delete => "Удалить",
                _ => "Выполнить"
            },
            CloseButtonText = "Отмена",
            DefaultButton = ContentDialogButton.Close
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private static string BuildDeleteServiceConfirmationText(
        OneCServiceProcessNode node,
        string serviceDisplayName)
    {
        var text =
            $"{serviceDisplayName}{Environment.NewLine}{Environment.NewLine}" +
            "Будет удалена только запись службы Windows. Файлы платформы 1С и каталог данных не удаляются.";

        if (node.Service?.State == "Running")
        {
            text +=
                $"{Environment.NewLine}{Environment.NewLine}" +
                "Служба сейчас работает. Приложение сначала остановит её, затем удалит запись службы Windows.";
        }

        return text;
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
            OneCServiceControlAction.Delete => "Остановка и удаление службы...",
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
            OneCServiceControlAction.Delete => "Служба была удалена",
            _ => "Действие было выполнено"
        };
    }

    private static TextBlock CreateDialogSectionTitle(string text)
    {
        return new TextBlock
        {
            Text = text,
            Foreground = GetThemeBrush("TextFillColorPrimaryBrush", 255, 32, 32, 32),
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            FontSize = 15,
            LineHeight = 20
        };
    }

    private static Microsoft.UI.Xaml.Media.Brush GetThemeBrush(
        string resourceName,
        byte alpha,
        byte red,
        byte green,
        byte blue)
    {
        if (Microsoft.UI.Xaml.Application.Current.Resources.TryGetValue(resourceName, out var resource)
            && resource is Microsoft.UI.Xaml.Media.Brush brush)
        {
            return brush;
        }

        return new Microsoft.UI.Xaml.Media.SolidColorBrush(
            Microsoft.UI.ColorHelper.FromArgb(alpha, red, green, blue));
    }

    private static TextBlock AddDialogFormRow(Grid grid, int row, string labelText, FrameworkElement control)
    {
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        control.HorizontalAlignment = HorizontalAlignment.Stretch;
        control.VerticalAlignment = VerticalAlignment.Center;

        var label = new TextBlock
        {
            Text = labelText,
            FontSize = 14,
            Foreground = GetThemeBrush("TextFillColorPrimaryBrush", 255, 32, 32, 32),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            LineHeight = 18,
            TextAlignment = TextAlignment.Right,
            TextWrapping = TextWrapping.WrapWholeWords
        };

        Grid.SetRow(label, row);
        Grid.SetRow(control, row);
        Grid.SetColumn(control, 1);

        grid.Children.Add(label);
        grid.Children.Add(control);

        return label;
    }

    private static void AddDialogCheckBoxRow(Grid grid, int row, string labelText, CheckBox checkBox)
    {
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var label = new TextBlock
        {
            Text = labelText,
            FontSize = 14,
            Foreground = GetThemeBrush("TextFillColorPrimaryBrush", 255, 32, 32, 32),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            LineHeight = 18,
            TextAlignment = TextAlignment.Right,
            TextWrapping = TextWrapping.WrapWholeWords
        };

        Grid.SetRow(label, row);
        Grid.SetRow(checkBox, row);
        Grid.SetColumn(checkBox, 1);

        grid.Children.Add(label);
        grid.Children.Add(checkBox);
    }

    private static ComboBox CreateComboBox(string? header, params string[] items)
    {
        var comboBox = new ComboBox
        {
            Width = 300,
            Height = 34,
            MinWidth = 0
        };

        if (!string.IsNullOrWhiteSpace(header))
        {
            comboBox.Header = header;
        }

        foreach (var item in items)
        {
            comboBox.Items.Add(item);
        }

        comboBox.SelectedIndex = 0;
        return comboBox;
    }

    private static string GetSelectedText(ComboBox comboBox)
    {
        return comboBox.SelectedItem as string ?? string.Empty;
    }

    private static bool TryValidateWindowsCredentials(string accountName, string password, out string message)
    {
        if (string.IsNullOrWhiteSpace(accountName))
        {
            message = "Укажите имя пользователя.";
            return false;
        }

        var (domain, userName) = SplitWindowsAccountName(accountName);
        if (string.IsNullOrWhiteSpace(userName))
        {
            message = "Укажите пользователя в формате DOMAIN\\user, .\\user или user@domain.";
            return false;
        }

        if (LogonUser(
                userName,
                domain,
                password,
                Logon32LogonNetwork,
                Logon32ProviderDefault,
                out var token))
        {
            CloseHandle(token);
            message = "Пароль подходит.";
            return true;
        }

        var error = Marshal.GetLastWin32Error();
        message = $"Пароль не подходит: {new Win32Exception(error).Message}";
        return false;
    }

    private static (string? Domain, string UserName) SplitWindowsAccountName(string accountName)
    {
        var normalized = accountName.Trim();
        var separatorIndex = normalized.IndexOf('\\', StringComparison.Ordinal);
        if (separatorIndex > 0 && separatorIndex < normalized.Length - 1)
        {
            var domain = normalized[..separatorIndex];
            if (string.Equals(domain, ".", StringComparison.Ordinal))
            {
                domain = Environment.MachineName;
            }

            return (domain, normalized[(separatorIndex + 1)..]);
        }

        return (null, normalized);
    }

    private static string? NormalizeFormValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string GetDbmsOptionValue(ComboBox comboBox)
    {
        return GetSelectedText(comboBox) switch
        {
            "MS SQL Server" => "MSSQLServer",
            "PostgreSQL" => "PostgreSQL",
            "IBM DB2" => "IBMDB2",
            "Oracle Database" => "OracleDatabase",
            _ => "MSSQLServer"
        };
    }

    private static string GetDbmsDisplayName(string dbms)
    {
        return dbms switch
        {
            "MSSQLServer" => "MS SQL Server",
            "IBMDB2" => "IBM DB2",
            "OracleDatabase" => "Oracle Database",
            _ => dbms
        };
    }

    private static string GetLocaleOptionValue(ComboBox comboBox)
    {
        return GetSelectedText(comboBox) switch
        {
            "русский (Россия)" => "ru_RU",
            _ => "ru_RU"
        };
    }

    private static string GetSecurityLevelOptionValue(ComboBox comboBox)
    {
        return GetSelectedText(comboBox) switch
        {
            "только соединение" => "1",
            "постоянно" => "2",
            _ => "0"
        };
    }

    private static string GetLicenseDistributionOptionValue(ComboBox comboBox)
    {
        return GetSelectedText(comboBox) == "Нет" ? "deny" : "allow";
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool LogonUser(
        string lpszUsername,
        string? lpszDomain,
        string? lpszPassword,
        int dwLogonType,
        int dwLogonProvider,
        out IntPtr phToken);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);
}
