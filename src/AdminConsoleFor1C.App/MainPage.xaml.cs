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
    private enum MainPageSection
    {
        Agents,
        Infobases,
        Sessions,
        Licenses,
        Settings
    }

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
    private MainPageSection _currentSection = MainPageSection.Agents;
    private string _agentsStatusText = "Готово";

    public MainPage()
    {
        InitializeComponent();
        ServiceTreeRepeater.ItemsSource = _nodes;
        AdministrationToolsCard.DataContext = new OneCAdministrationToolDiagnosticsViewModel([], [], []);
        var initialClusterDiagnostics = new OneCClusterDiagnosticsViewModel(
            CreateUnavailableClusterResult("localhost:1545", "Данные еще не обновлены"),
            "localhost:1540",
            canStartTemporaryRas: false,
            canStopTemporaryRas: false);
        ClustersCard.DataContext = initialClusterDiagnostics;
        InfobasesCard.DataContext = initialClusterDiagnostics;
        LicensesCard.DataContext = initialClusterDiagnostics;
        RootNavigation.SelectedItem = AgentsNavigationItem;
        ApplyCurrentSection();
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

    private void RootNavigation_SelectionChanged(
        NavigationView sender,
        NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer?.Tag is not string tag)
        {
            return;
        }

        _currentSection = tag switch
        {
            "infobases" => MainPageSection.Infobases,
            "sessions" => MainPageSection.Sessions,
            "licenses" => MainPageSection.Licenses,
            "settings" => MainPageSection.Settings,
            _ => MainPageSection.Agents
        };

        ApplyCurrentSection();
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
        OpenWindowsServices();
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

    private async void OpenTransferInfobaseDialogButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: OneCInfobaseSummaryInfo infobase })
        {
            await ShowTransferInfobaseDialogAsync(infobase);
        }
    }

    private async void OpenTransferInfobaseFromClusterButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: OneCClusterViewModel cluster })
        {
            return;
        }

        if (cluster.Infobases.Count == 0)
        {
            StatusText.Text = "Перенос информационной базы не начат";
            ErrorInfoBar.Title = "Не удалось перенести информационную базу";
            ErrorInfoBar.Message = "В выбранном кластере нет информационных баз.";
            ErrorInfoBar.IsOpen = true;
            return;
        }

        var infobase = cluster.Infobases.Count == 1
            ? cluster.Infobases[0]
            : await SelectInfobaseForTransferAsync(cluster.Infobases);

        if (infobase is not null)
        {
            await ShowTransferInfobaseDialogAsync(infobase);
        }
    }

    private async Task<OneCInfobaseSummaryInfo?> SelectInfobaseForTransferAsync(
        IReadOnlyList<OneCInfobaseSummaryInfo> infobases)
    {
        var comboBox = new ComboBox
        {
            Width = 420,
            MinWidth = 0,
            Height = 34,
            ItemsSource = infobases,
            DisplayMemberPath = nameof(OneCInfobaseSummaryInfo.NameText),
            SelectedIndex = 0
        };

        var content = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                new TextBlock
                {
                    Text = "Выберите информационную базу, которую нужно зарегистрировать на другом агенте.",
                    TextWrapping = TextWrapping.WrapWholeWords,
                    LineHeight = 20
                },
                comboBox
            }
        };

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Перенос информационной базы",
            Content = content,
            PrimaryButtonText = "Продолжить",
            CloseButtonText = "Отмена",
            DefaultButton = ContentDialogButton.Primary
        };
        dialog.Resources["ContentDialogMaxWidth"] = 520d;
        dialog.Resources["ContentDialogMinWidth"] = 480d;

        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary
            ? comboBox.SelectedItem as OneCInfobaseSummaryInfo
            : null;
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
        _agentsStatusText = "Обновление...";
        if (_currentSection == MainPageSection.Agents)
        {
            StatusText.Text = _agentsStatusText;
        }

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
            var nodes = BuildServiceProcessTree(services, processes, serverAgentSetup.Candidates);
            var previousServiceName = _selectedNode?.Service?.Name;

            _administrationToolDiagnostics = administrationToolDiagnostics;
            AdministrationToolsCard.DataContext = administrationToolDiagnostics;
            ClustersCard.DataContext = clusterDiagnostics;
            InfobasesCard.DataContext = clusterDiagnostics;
            LicensesCard.DataContext = clusterDiagnostics;

            _nodes.Clear();
            foreach (var node in nodes)
            {
                _nodes.Add(node);
            }

            SelectNode(GetNodeToSelect(previousServiceName));

            UpdateEmptyState();
            _agentsStatusText = serverAgentSetup.IsVisible && services.All(static service => service.Kind != OneCServiceKind.ServerAgent)
                ? "Найдены компоненты сервера 1С без службы Windows"
                : $"Найдено служб: {services.Count}, процессов: {processes.Count}";
            ApplyCurrentSection();
            return true;
        }
        catch (Exception exception)
        {
            ErrorInfoBar.Message = exception.Message;
            ErrorInfoBar.IsOpen = true;
            _agentsStatusText = "Ошибка обновления";
            StatusText.Text = _agentsStatusText;
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
        var isAgentsSection = _currentSection == MainPageSection.Agents;

        ServiceTreeRepeater.Visibility = isAgentsSection && hasItems ? Visibility.Visible : Visibility.Collapsed;
        ComponentDetailsCard.Visibility = isAgentsSection && hasItems && _selectedNode is not null
            ? Visibility.Visible
            : Visibility.Collapsed;
        ServicesTable.Visibility = isAgentsSection && hasItems ? Visibility.Visible : Visibility.Collapsed;
        EmptyState.Visibility = isAgentsSection && !hasItems ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyCurrentSection()
    {
        var isAgentsSection = _currentSection == MainPageSection.Agents;

        PageTitleText.Text = _currentSection switch
        {
            MainPageSection.Infobases => "Информационные базы",
            MainPageSection.Sessions => "Сеансы",
            MainPageSection.Licenses => "Лицензии",
            MainPageSection.Settings => "Настройки",
            _ => "Агенты сервера 1С"
        };

        StatusText.Text = _currentSection switch
        {
            MainPageSection.Infobases => "Информационные базы найденных кластеров 1С",
            MainPageSection.Sessions => "Активные пользователи и сеансы информационных баз",
            MainPageSection.Licenses => "Аппаратные, программные и занятые лицензии",
            MainPageSection.Settings => "Параметры приложения",
            _ => _agentsStatusText
        };

        ServiceListColumn.Width = isAgentsSection
            ? new GridLength(520)
            : new GridLength(0);
        ContentGrid.ColumnSpacing = isAgentsSection ? 24 : 0;

        AdministrationToolsCard.Visibility = isAgentsSection ? Visibility.Visible : Visibility.Collapsed;
        InfobasesCard.Visibility = _currentSection == MainPageSection.Infobases ? Visibility.Visible : Visibility.Collapsed;
        ClustersCard.Visibility = Visibility.Collapsed;
        SessionsCard.Visibility = _currentSection == MainPageSection.Sessions ? Visibility.Visible : Visibility.Collapsed;
        LicensesCard.Visibility = _currentSection == MainPageSection.Licenses ? Visibility.Visible : Visibility.Collapsed;
        SettingsCard.Visibility = _currentSection == MainPageSection.Settings ? Visibility.Visible : Visibility.Collapsed;

        Grid.SetRow(ClustersCard, 3);
        Grid.SetRow(LicensesCard, _currentSection == MainPageSection.Licenses ? 0 : 2);

        UpdateEmptyState();
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
        ComponentDetailsCard.Visibility = _currentSection == MainPageSection.Agents
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private static IReadOnlyList<OneCServiceProcessNode> BuildServiceProcessTree(
        IReadOnlyList<OneCServiceInfo> services,
        IReadOnlyList<OneCProcessInfo> processes,
        IReadOnlyList<OneCServerAgentSetupCandidateViewModel> serverAgentSetupCandidates)
    {
        var nodes = new List<OneCServiceProcessNode>();
        var assignedProcessIds = new HashSet<uint>();

        foreach (var service in services
            .OrderBy(static service => string.IsNullOrWhiteSpace(service.Version))
            .ThenBy(static service => ParseVersion(service.Version))
            .ThenBy(static service => service.AgentPort ?? int.MaxValue)
            .ThenBy(static service => service.DisplayNameText, StringComparer.OrdinalIgnoreCase))
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

        foreach (var candidate in serverAgentSetupCandidates)
        {
            nodes.Add(new OneCServiceProcessNode
            {
                Service = null,
                ServerAgentSetupCandidate = candidate,
                Processes = []
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

    private static Version ParseVersion(string? version)
    {
        return Version.TryParse(version, out var parsed)
            ? parsed
            : new Version();
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

    private void OpenWindowsServices()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "services.msc",
                UseShellExecute = true
            });

            StatusText.Text = "Открыто окно служб Windows";
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
        const double dialogLabelWidth = 180;
        const double dialogFieldWidth = 420;

        var serviceNameTextBox = CreateReadOnlyDialogTextBox(candidate.ExpectedServiceNameText);
        serviceNameTextBox.Width = dialogFieldWidth;
        var ragentPathTextBox = CreateReadOnlyDialogTextBox(candidate.RagentPathText);
        ragentPathTextBox.Width = dialogFieldWidth;
        var dataDirectoryTextBox = new TextBox
        {
            Width = dialogFieldWidth,
            Height = 34,
            IsSpellCheckEnabled = false,
            Text = candidate.SuggestedServiceDataDirectory
        };
        var dataDirectoryCautionBrush = GetThemeBrush("SystemFillColorCautionBrush", 255, 157, 93, 0);
        var existingDataDirectoryTextBlock = new TextBlock
        {
            Foreground = GetThemeBrush("TextFillColorSecondaryBrush", 255, 96, 96, 96),
            LineHeight = 18,
            Text = "Каталог уже содержит файлы. Могут восстановиться прежние настройки кластера и список баз.",
            TextWrapping = TextWrapping.WrapWholeWords
        };
        var existingDataDirectoryHintPanel = new Grid
        {
            ColumnSpacing = 6,
            Visibility = Visibility.Collapsed
        };
        existingDataDirectoryHintPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        existingDataDirectoryHintPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var existingDataDirectoryIcon = new FontIcon
        {
            FontSize = 12,
            Foreground = dataDirectoryCautionBrush,
            Glyph = "\uE946",
            Margin = new Thickness(0, 5, 0, 0),
            VerticalAlignment = VerticalAlignment.Top
        };
        Grid.SetColumn(existingDataDirectoryIcon, 0);
        Grid.SetColumn(existingDataDirectoryTextBlock, 1);
        existingDataDirectoryHintPanel.Children.Add(existingDataDirectoryIcon);
        existingDataDirectoryHintPanel.Children.Add(existingDataDirectoryTextBlock);
        var dataDirectoryPanel = new StackPanel
        {
            Width = dialogFieldWidth,
            Spacing = 4,
            Children =
            {
                dataDirectoryTextBox,
                existingDataDirectoryHintPanel
            }
        };
        var agentPortTextBox = CreatePortTextBox(candidate.AgentPortText);
        agentPortTextBox.Width = dialogFieldWidth;
        var clusterPortTextBox = CreatePortTextBox(candidate.ClusterPortText);
        clusterPortTextBox.Width = dialogFieldWidth;
        var processRangeTextBox = new TextBox
        {
            Width = dialogFieldWidth,
            Height = 34,
            IsSpellCheckEnabled = false,
            Text = candidate.ProcessRangeText
        };
        var debugModeComboBox = CreateServiceDialogComboBox(
            "Выключен",
            "TCP",
            "HTTP");
        debugModeComboBox.Width = dialogFieldWidth;
        var debugServerPortTextBox = CreatePortTextBox(candidate.DebugServerPortText);
        debugServerPortTextBox.Width = dialogFieldWidth;
        var serviceUserComboBox = CreateServiceDialogComboBox(GetServiceAccountOptions());
        serviceUserComboBox.Width = double.NaN;
        serviceUserComboBox.HorizontalAlignment = HorizontalAlignment.Stretch;
        var serviceUserPanel = new Grid
        {
            Width = dialogFieldWidth,
            Children =
            {
                serviceUserComboBox
            }
        };
        var serviceCustomUserTextBox = CreateOptionalDialogTextBox(@"DOMAIN\user или .\user");
        serviceCustomUserTextBox.Width = dialogFieldWidth;
        serviceCustomUserTextBox.HorizontalAlignment = HorizontalAlignment.Stretch;
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
        var servicePasswordInputGrid = new Grid
        {
            Width = dialogFieldWidth,
            ColumnSpacing = 8,
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto }
            }
        };
        servicePasswordBox.Width = double.NaN;
        servicePasswordBox.HorizontalAlignment = HorizontalAlignment.Stretch;
        Grid.SetColumn(servicePasswordBox, 0);
        Grid.SetColumn(checkServicePasswordButton, 1);
        servicePasswordInputGrid.Children.Add(servicePasswordBox);
        servicePasswordInputGrid.Children.Add(checkServicePasswordButton);
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
                servicePasswordInputGrid,
                servicePasswordStatusTextBlock
            }
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
                new ColumnDefinition { Width = new GridLength(dialogLabelWidth) },
                new ColumnDefinition { Width = new GridLength(dialogFieldWidth) }
            }
        };

        var row = 0;
        AddDialogFormRow(formGrid, row++, "Имя службы Windows:", serviceNameTextBox);
        AddDialogFormRow(formGrid, row++, "ragent.exe:", ragentPathTextBox);
        var dataDirectoryLabel = AddDialogFormRow(formGrid, row++, "Каталог данных:", dataDirectoryPanel);
        dataDirectoryLabel.VerticalAlignment = VerticalAlignment.Top;
        dataDirectoryLabel.Margin = new Thickness(0, 7, 0, 0);
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
                new ColumnDefinition { Width = new GridLength(dialogLabelWidth) },
                new ColumnDefinition { Width = new GridLength(dialogFieldWidth) }
            }
        };
        var serviceAccountRow = 0;
        AddDialogFormRow(serviceAccountGrid, serviceAccountRow++, "Пользователь службы:", serviceUserPanel);
        var serviceCustomUserLabel = AddDialogFormRow(serviceAccountGrid, serviceAccountRow++, "Имя пользователя:", serviceCustomUserTextBox);
        var servicePasswordLabel = AddDialogFormRow(serviceAccountGrid, serviceAccountRow, "Пароль пользователя:", servicePasswordPanel);
        servicePasswordLabel.VerticalAlignment = VerticalAlignment.Top;
        servicePasswordLabel.Margin = new Thickness(0, 7, 0, 0);

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
        var contentPanel = new StackPanel
        {
            Width = 636,
            Spacing = 12,
            Children =
            {
                formCard
            }
        };
        var content = new ScrollViewer
        {
            Content = contentPanel,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            MaxHeight = Math.Clamp(XamlRoot.Size.Height - 110, 420, 920),
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
            }
        }

        void UpdateDataDirectoryInfo()
        {
            var showInfo = HasExistingDataDirectoryContent(dataDirectoryTextBox.Text);
            existingDataDirectoryHintPanel.Visibility = showInfo ? Visibility.Visible : Visibility.Collapsed;
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

        dataDirectoryTextBox.TextChanged += (_, _) =>
        {
            UpdateDataDirectoryInfo();
            UpdatePreview();
        };
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
        UpdateDataDirectoryInfo();
        UpdatePreview();

        dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Создание службы агента сервера 1С",
            Content = content,
            CloseButtonText = "Закрыть",
            DefaultButton = ContentDialogButton.Primary,
            PrimaryButtonText = "Зарегистрировать"
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
            IsSpellCheckEnabled = false,
            Text = text,
            TextWrapping = TextWrapping.Wrap
        };
    }

    private static bool HasExistingDataDirectoryContent(string? path)
    {
        var normalizedPath = NormalizeFormValue(path);
        if (string.IsNullOrWhiteSpace(normalizedPath) || !Directory.Exists(normalizedPath))
        {
            return false;
        }

        try
        {
            return Directory.EnumerateFileSystemEntries(normalizedPath).Any();
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException
            or IOException
            or ArgumentException
            or NotSupportedException
            or PathTooLongException)
        {
            return true;
        }
    }

    private static TextBox CreatePortTextBox(string text)
    {
        return new TextBox
        {
            Width = 420,
            Height = 34,
            IsSpellCheckEnabled = false,
            Text = text
        };
    }

    private static TextBox CreateOptionalDialogTextBox(string placeholderText)
    {
        return new TextBox
        {
            Width = 420,
            Height = 34,
            IsSpellCheckEnabled = false,
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

    private sealed record TransferTargetAgentItem(OneCAgentEndpoint Agent)
    {
        public string DisplayText => Agent.DisplayText;
    }

    private sealed record TransferTargetClusterItem(OneCClusterInfo Cluster)
    {
        public string DisplayText => $"{Cluster.NameText} ({Cluster.AddressText})";
    }

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

    private async Task ShowTransferInfobaseDialogAsync(OneCInfobaseSummaryInfo infobase)
    {
        if (!TryCreateSourceAgentEndpoint(out var sourceAgent, out var sourceAgentError))
        {
            StatusText.Text = "Перенос информационной базы не начат";
            ErrorInfoBar.Title = "Не удалось перенести информационную базу";
            ErrorInfoBar.Message = sourceAgentError;
            ErrorInfoBar.IsOpen = true;
            return;
        }

        if (string.IsNullOrWhiteSpace(infobase.ClusterUuid) || string.IsNullOrWhiteSpace(infobase.Uuid))
        {
            StatusText.Text = "Перенос информационной базы не начат";
            ErrorInfoBar.Title = "Не удалось перенести информационную базу";
            ErrorInfoBar.Message = "Не удалось определить кластер или идентификатор информационной базы.";
            ErrorInfoBar.IsOpen = true;
            return;
        }

        var targetAgents = GetTransferTargetAgents(sourceAgent).ToList();
        if (targetAgents.Count == 0)
        {
            StatusText.Text = "Перенос информационной базы не начат";
            ErrorInfoBar.Title = "Не удалось перенести информационную базу";
            ErrorInfoBar.Message = "Другой агент сервера 1С не найден. Сначала создайте или запустите службу агента, на которую нужно перенести регистрацию ИБ.";
            ErrorInfoBar.IsOpen = true;
            return;
        }

        OneCInfobaseSummaryInfo sourceDetails = infobase;
        IReadOnlyList<OneCSessionInfo> activeSessions = [];
        try
        {
            sourceDetails = await _clusterInventory.GetInfobaseDetailsAsync(
                    sourceAgent.RacPath,
                    sourceAgent.AdministrationServerAddress,
                    infobase.ClusterUuid,
                    infobase)
                ?? infobase;

            activeSessions = await _clusterInventory.GetInfobaseSessionsAsync(
                sourceAgent.RacPath,
                sourceAgent.AdministrationServerAddress,
                infobase.ClusterUuid,
                infobase.Uuid);
        }
        catch
        {
            // Детали и сеансы уточняются еще раз при выполнении переноса.
        }

        var targetAgentComboBox = new ComboBox
        {
            Width = 430,
            MinWidth = 0,
            Height = 34,
            ItemsSource = targetAgents,
            DisplayMemberPath = nameof(TransferTargetAgentItem.DisplayText),
            SelectedIndex = 0
        };
        var targetClusterComboBox = new ComboBox
        {
            Width = 430,
            MinWidth = 0,
            Height = 34,
            DisplayMemberPath = nameof(TransferTargetClusterItem.DisplayText)
        };
        var dbPasswordBox = new PasswordBox
        {
            Width = 430,
            MinWidth = 0,
            Height = 34,
            PlaceholderText = string.IsNullOrWhiteSpace(sourceDetails.DbUser)
                ? "не требуется, если пользователь БД не указан"
                : "пароль пользователя БД"
        };
        var clusterStatusTextBlock = new TextBlock
        {
            Foreground = GetThemeBrush("TextFillColorSecondaryBrush", 255, 96, 96, 96),
            LineHeight = 18,
            TextWrapping = TextWrapping.WrapWholeWords
        };
        var validationTextBlock = new TextBlock
        {
            Foreground = GetThemeBrush("SystemFillColorCriticalBrush", 255, 196, 43, 28),
            LineHeight = 20,
            TextWrapping = TextWrapping.WrapWholeWords,
            Visibility = Visibility.Collapsed
        };
        var targetProgressRing = new ProgressRing
        {
            Width = 18,
            Height = 18,
            IsActive = false,
            Visibility = Visibility.Collapsed
        };
        var transferButton = new Button
        {
            Content = "Перенести",
            MinWidth = 170,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = GetThemeBrush("AccentFillColorDefaultBrush", 255, 0, 120, 212),
            BorderBrush = GetThemeBrush("AccentFillColorDefaultBrush", 255, 0, 120, 212),
            Foreground = GetThemeBrush("TextOnAccentFillColorPrimaryBrush", 255, 255, 255, 255)
        };
        var closeButton = new Button
        {
            Content = "Закрыть",
            MinWidth = 170,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var infoBar = new InfoBar
        {
            IsOpen = true,
            IsClosable = false,
            Severity = InfoBarSeverity.Informational,
            Title = "Будет создана регистрация ИБ на другом агенте.",
            Message = "База данных в СУБД не переносится и не удаляется."
        };
        var sourceGrid = CreateTransferSummaryGrid(sourceAgent, sourceDetails, activeSessions.Count);
        var targetGrid = new Grid
        {
            ColumnSpacing = 12,
            RowSpacing = 6,
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(180) },
                new ColumnDefinition { Width = new GridLength(430) }
            }
        };

        var targetRow = 0;
        AddDialogFormRow(targetGrid, targetRow++, "Целевой агент:", targetAgentComboBox);
        AddDialogFormRow(targetGrid, targetRow++, "Целевой кластер:", targetClusterComboBox);
        AddDialogFormRow(targetGrid, targetRow++, "Пароль пользователя БД:", dbPasswordBox);

        var statusPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                targetProgressRing,
                clusterStatusTextBlock
            }
        };
        Grid.SetRow(statusPanel, targetRow);
        Grid.SetColumn(statusPanel, 1);
        targetGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        targetGrid.Children.Add(statusPanel);

        var content = new StackPanel
        {
            Width = 650,
            Spacing = 14,
            Children =
            {
                infoBar,
                new Border
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
                            CreateDialogSectionTitle("Источник"),
                            sourceGrid
                        }
                    }
                },
                new Border
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
                            CreateDialogSectionTitle("Цель"),
                            targetGrid,
                            validationTextBlock
                        }
                    }
                }
            }
        };
        var footer = new Grid
        {
            Margin = new Thickness(0, 12, 0, 0),
            ColumnSpacing = 8,
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
            },
            Children =
            {
                transferButton,
                closeButton
            }
        };
        Grid.SetColumn(closeButton, 1);
        content.Children.Add(footer);

        ContentDialog? dialog = null;
        OneCInfobaseTransferPlan? planToExecute = null;

        async Task LoadTargetClustersAsync()
        {
            targetClusterComboBox.ItemsSource = null;
            targetClusterComboBox.SelectedIndex = -1;
            targetClusterComboBox.IsEnabled = false;
            transferButton.IsEnabled = false;
            validationTextBlock.Visibility = Visibility.Collapsed;
            clusterStatusTextBlock.Text = "Проверка целевого агента...";
            targetProgressRing.IsActive = true;
            targetProgressRing.Visibility = Visibility.Visible;

            if (targetAgentComboBox.SelectedItem is not TransferTargetAgentItem targetAgentItem)
            {
                clusterStatusTextBlock.Text = "Выберите целевой агент.";
                targetProgressRing.IsActive = false;
                targetProgressRing.Visibility = Visibility.Collapsed;
                return;
            }

            try
            {
                var result = await _clusterInventory.GetAgentClustersAsync(targetAgentItem.Agent);
                if (!result.IsAvailable)
                {
                    clusterStatusTextBlock.Text = $"Целевой агент недоступен: {result.Message}";
                    return;
                }

                var clusters = result.Clusters
                    .Select(static cluster => new TransferTargetClusterItem(cluster))
                    .ToList();

                if (clusters.Count == 0)
                {
                    clusterStatusTextBlock.Text = "На целевом агенте не найден кластер. Создание нового кластера будет добавлено отдельным сценарием.";
                    return;
                }

                targetClusterComboBox.ItemsSource = clusters;
                targetClusterComboBox.SelectedIndex = 0;
                targetClusterComboBox.IsEnabled = true;
                transferButton.IsEnabled = true;
                clusterStatusTextBlock.Text = $"Найдено кластеров: {clusters.Count}.";
            }
            catch (Exception exception)
            {
                clusterStatusTextBlock.Text = $"Целевой агент недоступен: {exception.Message}";
            }
            finally
            {
                targetProgressRing.IsActive = false;
                targetProgressRing.Visibility = Visibility.Collapsed;
            }
        }

        targetAgentComboBox.SelectionChanged += async (_, _) => await LoadTargetClustersAsync();
        transferButton.Click += (_, _) =>
        {
            if (targetAgentComboBox.SelectedItem is not TransferTargetAgentItem targetAgentItem
                || targetClusterComboBox.SelectedItem is not TransferTargetClusterItem targetClusterItem)
            {
                validationTextBlock.Text = "Выберите целевой агент и кластер.";
                validationTextBlock.Visibility = Visibility.Visible;
                return;
            }

            planToExecute = new OneCInfobaseTransferPlan
            {
                SourceAgent = sourceAgent,
                SourceClusterUuid = infobase.ClusterUuid,
                SourceInfobase = sourceDetails,
                TargetAgent = targetAgentItem.Agent,
                TargetClusterUuid = targetClusterItem.Cluster.Uuid,
                DbPassword = NormalizeFormValue(dbPasswordBox.Password)
            };
            dialog?.Hide();
        };
        closeButton.Click += (_, _) => dialog?.Hide();

        dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Перенос информационной базы",
            Content = content
        };
        dialog.Resources["ContentDialogMaxWidth"] = 760d;
        dialog.Resources["ContentDialogMinWidth"] = 720d;

        await LoadTargetClustersAsync();
        await dialog.ShowAsync();

        if (planToExecute is not null)
        {
            await ExecuteInfobaseTransferAsync(planToExecute);
        }
    }

    private async Task ExecuteInfobaseTransferAsync(OneCInfobaseTransferPlan plan)
    {
        SetClusterCommandRunning(true);
        ErrorInfoBar.IsOpen = false;
        StatusText.Text = "Перенос информационной базы...";

        OneCInfobaseSummaryInfo? sourceDetailsForRollback = null;
        var restoreSourceSessionsOnFailure = false;
        var targetRegistrationCreated = false;

        try
        {
            var sourceDetails = await _clusterInventory.GetInfobaseDetailsAsync(
                    plan.SourceAgent.RacPath,
                    plan.SourceAgent.AdministrationServerAddress,
                    plan.SourceClusterUuid,
                    plan.SourceInfobase)
                ?? plan.SourceInfobase;
            sourceDetailsForRollback = sourceDetails;

            var targetInventory = await _clusterInventory.GetAgentClustersAsync(plan.TargetAgent);
            if (!targetInventory.IsAvailable)
            {
                ShowInfobaseTransferError($"Целевой агент недоступен: {targetInventory.Message}");
                return;
            }

            var targetCluster = targetInventory.Clusters.FirstOrDefault(
                cluster => string.Equals(cluster.Uuid, plan.TargetClusterUuid, StringComparison.OrdinalIgnoreCase));

            if (targetCluster is null)
            {
                ShowInfobaseTransferError("Целевой кластер не найден.");
                return;
            }

            if (FindMatchingInfobase(targetCluster, sourceDetails) is not null)
            {
                ShowInfobaseTransferError("Информационная база уже зарегистрирована на целевом агенте.");
                return;
            }

            var denyResult = await _clusterInventory.UpdateInfobaseRestrictionsAsync(
                plan.SourceAgent.RacPath,
                new OneCInfobaseRestrictionsUpdateRequest
                {
                    AdministrationServerAddress = plan.SourceAgent.AdministrationServerAddress,
                    ClusterUuid = plan.SourceClusterUuid,
                    InfobaseUuid = sourceDetails.Uuid,
                    InfobaseName = sourceDetails.NameText,
                    SessionsDeny = "on"
                });

            if (!denyResult.IsSuccess)
            {
                ShowInfobaseTransferError($"Не удалось запретить вход пользователей: {denyResult.Message}");
                return;
            }
            restoreSourceSessionsOnFailure = !sourceDetails.AreSessionsDenied;

            var sessions = await _clusterInventory.GetInfobaseSessionsAsync(
                plan.SourceAgent.RacPath,
                plan.SourceAgent.AdministrationServerAddress,
                plan.SourceClusterUuid,
                sourceDetails.Uuid);

            var terminateResult = await _clusterInventory.TerminateInfobaseSessionsAsync(
                plan.SourceAgent.RacPath,
                plan.SourceAgent.AdministrationServerAddress,
                plan.SourceClusterUuid,
                sourceDetails.Uuid,
                plan.SessionTerminationMessage);

            if (!terminateResult.IsSuccess)
            {
                ShowInfobaseTransferError($"Не удалось завершить активные сеансы: {terminateResult.Message}");
                return;
            }

            var remainingSessions = await _clusterInventory.GetInfobaseSessionsAsync(
                plan.SourceAgent.RacPath,
                plan.SourceAgent.AdministrationServerAddress,
                plan.SourceClusterUuid,
                sourceDetails.Uuid);

            if (remainingSessions.Count > 0)
            {
                ShowInfobaseTransferError(
                    $"Активные сеансы не завершились: {remainingSessions.Count}. Регистрация на целевом агенте не была создана.");
                return;
            }

            var effectivePlan = plan with { SourceInfobase = sourceDetails };
            var createResult = await _clusterInventory.CreateInfobaseRegistrationAsync(
                plan.TargetAgent.RacPath,
                effectivePlan);

            if (!createResult.IsSuccess)
            {
                ShowInfobaseTransferError($"Не удалось создать регистрацию ИБ на целевом агенте: {createResult.Message}");
                return;
            }
            targetRegistrationCreated = true;

            var verifyInventory = await _clusterInventory.GetAgentClustersAsync(plan.TargetAgent);
            var createdInfobase = verifyInventory.Clusters
                .FirstOrDefault(cluster => string.Equals(cluster.Uuid, plan.TargetClusterUuid, StringComparison.OrdinalIgnoreCase))
                ?.Infobases
                .FirstOrDefault(candidate => IsSameInfobaseRegistration(candidate, sourceDetails));

            if (createdInfobase is null)
            {
                ShowInfobaseTransferError("Регистрация была создана, но приложение не увидело ИБ на целевом агенте после проверки.");
                return;
            }

            var dropOldRegistration = await ConfirmDropOldInfobaseRegistrationAsync(
                effectivePlan,
                sessions.Count);

            if (dropOldRegistration)
            {
                var dropResult = await _clusterInventory.DropInfobaseRegistrationAsync(
                    plan.SourceAgent.RacPath,
                    plan.SourceAgent.AdministrationServerAddress,
                    plan.SourceClusterUuid,
                    sourceDetails.Uuid,
                    dropDatabase: false);

                if (!dropResult.IsSuccess)
                {
                    ShowInfobaseTransferError(
                        $"Регистрация на целевом агенте создана, но старую регистрацию удалить не удалось: {dropResult.Message}");
                    return;
                }
            }

            if (await RefreshServicesAsync())
            {
                StatusText.Text = dropOldRegistration
                    ? $"Информационная база была перенесена: {sourceDetails.NameText}"
                    : $"Регистрация ИБ была создана на целевом агенте: {sourceDetails.NameText}";
            }
        }
        catch (Exception exception)
        {
            ShowInfobaseTransferError(exception.Message);
        }
        finally
        {
            if (restoreSourceSessionsOnFailure && !targetRegistrationCreated && sourceDetailsForRollback is not null)
            {
                try
                {
                    await _clusterInventory.UpdateInfobaseRestrictionsAsync(
                        plan.SourceAgent.RacPath,
                        new OneCInfobaseRestrictionsUpdateRequest
                        {
                            AdministrationServerAddress = plan.SourceAgent.AdministrationServerAddress,
                            ClusterUuid = plan.SourceClusterUuid,
                            InfobaseUuid = sourceDetailsForRollback.Uuid,
                            InfobaseName = sourceDetailsForRollback.NameText,
                            SessionsDeny = "off"
                        });
                }
                catch
                {
                    // Ошибка переноса уже показана пользователю. Откат запрета входа не должен скрывать исходную причину.
                }
            }

            SetClusterCommandRunning(false);
        }
    }

    private void ShowInfobaseTransferError(string message)
    {
        ErrorInfoBar.Title = "Не удалось перенести информационную базу";
        ErrorInfoBar.Message = message;
        ErrorInfoBar.IsOpen = true;
        StatusText.Text = "Информационная база не была перенесена";
    }

    private async Task<bool> ConfirmDropOldInfobaseRegistrationAsync(
        OneCInfobaseTransferPlan plan,
        int terminatedSessionCount)
    {
        var content = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock
                {
                    Text = plan.SourceInfobase.NameText,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    LineHeight = 20
                },
                new TextBlock
                {
                    Text =
                        $"Регистрация на целевом агенте создана.{Environment.NewLine}" +
                        $"Завершено сеансов: {terminatedSessionCount}.{Environment.NewLine}" +
                        "База данных в СУБД не переносилась и не будет удаляться.",
                    LineHeight = 20,
                    TextWrapping = TextWrapping.WrapWholeWords
                },
                new TextBlock
                {
                    Text = "Удалить регистрацию со старого агента?",
                    LineHeight = 20,
                    TextWrapping = TextWrapping.WrapWholeWords
                }
            }
        };

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Старая регистрация ИБ",
            Content = content,
            PrimaryButtonText = "Удалить регистрацию",
            SecondaryButtonText = "Оставить",
            DefaultButton = ContentDialogButton.Secondary
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private Grid CreateTransferSummaryGrid(
        OneCAgentEndpoint sourceAgent,
        OneCInfobaseSummaryInfo infobase,
        int activeSessionCount)
    {
        var grid = new Grid
        {
            ColumnSpacing = 12,
            RowSpacing = 6,
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(180) },
                new ColumnDefinition { Width = new GridLength(430) }
            }
        };

        var row = 0;
        AddDialogValueRow(grid, row++, "Исходный агент:", sourceAgent.DisplayText);
        AddDialogValueRow(grid, row++, "Информационная база:", infobase.NameText);
        AddDialogValueRow(grid, row++, "Сервер БД:", infobase.DbServer ?? "—");
        AddDialogValueRow(grid, row++, "База данных:", infobase.DbName ?? "—");
        AddDialogValueRow(grid, row, "Активные сеансы:", activeSessionCount.ToString(CultureInfo.InvariantCulture));

        return grid;
    }

    private static void AddDialogValueRow(Grid grid, int row, string labelText, string valueText)
    {
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var label = new TextBlock
        {
            Text = labelText,
            FontSize = 14,
            Foreground = GetThemeBrush("TextFillColorSecondaryBrush", 255, 96, 96, 96),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            TextAlignment = TextAlignment.Right,
            TextWrapping = TextWrapping.WrapWholeWords
        };
        var value = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(valueText) ? "—" : valueText,
            FontSize = 14,
            Foreground = GetThemeBrush("TextFillColorPrimaryBrush", 255, 32, 32, 32),
            TextWrapping = TextWrapping.WrapWholeWords
        };

        Grid.SetRow(label, row);
        Grid.SetRow(value, row);
        Grid.SetColumn(value, 1);

        grid.Children.Add(label);
        grid.Children.Add(value);
    }

    private bool TryCreateSourceAgentEndpoint(
        out OneCAgentEndpoint sourceAgent,
        out string errorMessage)
    {
        var diagnostics = _administrationToolDiagnostics;
        if (diagnostics?.RacTool is null)
        {
            sourceAgent = CreateEmptyAgentEndpoint();
            errorMessage = "rac.exe не найден.";
            return false;
        }

        var service = _selectedNode?.Service;
        var agentPort = service?.AgentPort ?? diagnostics.AgentPort;
        var administrationServerPort = service?.AdministrationServerPort ?? diagnostics.AdministrationServerPort;
        var version = FirstNonEmpty(
            service?.Version,
            diagnostics.TargetVersion,
            diagnostics.RacTool.Version,
            "—");

        sourceAgent = new OneCAgentEndpoint
        {
            DisplayName = service?.DisplayNameText ?? $"Агент {agentPort}",
            AgentAddress = $"{Environment.MachineName}:{agentPort.ToString(CultureInfo.InvariantCulture)}",
            AdministrationServerAddress = $"localhost:{administrationServerPort.ToString(CultureInfo.InvariantCulture)}",
            AgentPort = agentPort,
            Version = version,
            RacPath = diagnostics.RacTool.FilePath,
            ServiceName = service?.Name,
            WindowsDisplayName = service?.DisplayName
        };
        errorMessage = string.Empty;
        return true;
    }

    private static OneCAgentEndpoint CreateEmptyAgentEndpoint()
    {
        return new OneCAgentEndpoint
        {
            DisplayName = string.Empty,
            AgentAddress = string.Empty,
            AdministrationServerAddress = string.Empty,
            AgentPort = 0,
            Version = string.Empty,
            RacPath = string.Empty
        };
    }

    private IEnumerable<TransferTargetAgentItem> GetTransferTargetAgents(OneCAgentEndpoint sourceAgent)
    {
        return _nodes
            .Select(static node => node.Service)
            .Where(static service => service?.Kind == OneCServiceKind.ServerAgent)
            .Select(service => TryCreateAgentEndpoint(service!, out var endpoint) ? endpoint : null)
            .Where(endpoint => endpoint is not null && !IsSameAgent(sourceAgent, endpoint))
            .Select(endpoint => new TransferTargetAgentItem(endpoint!))
            .OrderBy(static item => ParseVersion(item.Agent.Version))
            .ThenBy(static item => item.Agent.AgentPort);
    }

    private bool TryCreateAgentEndpoint(OneCServiceInfo service, out OneCAgentEndpoint? endpoint)
    {
        endpoint = null;
        if (service.AgentPort is null)
        {
            return false;
        }

        var racTool = FindRacToolForVersion(service.Version);
        if (racTool is null)
        {
            return false;
        }

        var agentPort = service.AgentPort.Value;
        var administrationServerPort = service.AdministrationServerPort ?? agentPort + 5;
        var version = FirstNonEmpty(service.Version, racTool.Version, "—");

        endpoint = new OneCAgentEndpoint
        {
            DisplayName = service.DisplayNameText,
            AgentAddress = $"{Environment.MachineName}:{agentPort.ToString(CultureInfo.InvariantCulture)}",
            AdministrationServerAddress = $"localhost:{administrationServerPort.ToString(CultureInfo.InvariantCulture)}",
            AgentPort = agentPort,
            Version = version,
            RacPath = racTool.FilePath,
            ServiceName = service.Name,
            WindowsDisplayName = service.DisplayName
        };
        return true;
    }

    private OneCAdministrationToolInfo? FindRacToolForVersion(string? version)
    {
        var tools = _administrationToolDiagnostics?.Tools ?? [];
        var racTools = tools
            .Where(static tool => tool.Kind == OneCAdministrationToolKind.Rac)
            .ToList();

        if (racTools.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(version))
        {
            var exactTool = racTools.FirstOrDefault(
                tool => string.Equals(tool.Version, version, StringComparison.OrdinalIgnoreCase));

            if (exactTool is not null)
            {
                return exactTool;
            }
        }

        return racTools
            .OrderByDescending(static tool => ParseVersion(tool.Version))
            .FirstOrDefault();
    }

    private static bool IsSameAgent(OneCAgentEndpoint first, OneCAgentEndpoint? second)
    {
        if (second is null)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(first.ServiceName)
            && !string.IsNullOrWhiteSpace(second.ServiceName)
            && string.Equals(first.ServiceName, second.ServiceName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return first.AgentPort == second.AgentPort
            && string.Equals(first.Version, second.Version, StringComparison.OrdinalIgnoreCase);
    }

    private static OneCInfobaseSummaryInfo? FindMatchingInfobase(
        OneCClusterInfo cluster,
        OneCInfobaseSummaryInfo sourceInfobase)
    {
        return cluster.Infobases.FirstOrDefault(candidate => IsSameInfobaseRegistration(candidate, sourceInfobase));
    }

    private static bool IsSameInfobaseRegistration(
        OneCInfobaseSummaryInfo candidate,
        OneCInfobaseSummaryInfo sourceInfobase)
    {
        if (string.Equals(candidate.Name, sourceInfobase.Name, StringComparison.OrdinalIgnoreCase)
            || string.Equals(candidate.NameText, sourceInfobase.NameText, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(candidate.DbServer)
            && !string.IsNullOrWhiteSpace(candidate.DbName)
            && string.Equals(candidate.DbServer, sourceInfobase.DbServer, StringComparison.OrdinalIgnoreCase)
            && string.Equals(candidate.DbName, sourceInfobase.DbName, StringComparison.OrdinalIgnoreCase);
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
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
