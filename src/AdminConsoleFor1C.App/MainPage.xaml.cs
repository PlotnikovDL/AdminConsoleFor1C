using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
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
    private readonly IOneCServiceInventory _serviceInventory = new WindowsOneCServiceInventory();
    private readonly IOneCProcessInventory _processInventory = new WindowsOneCProcessInventory();
    private readonly IOneCAdministrationToolInventory _administrationToolInventory = new WindowsOneCAdministrationToolInventory();
    private readonly IOneCClusterInventory _clusterInventory = new RacOneCClusterInventory();
    private readonly IOneCAdministrationServerLauncher _administrationServerLauncher = new RasOneCAdministrationServerLauncher();
    private readonly IOneCServiceController _serviceController = new WindowsOneCServiceController();
    private readonly ObservableCollection<OneCServiceProcessNode> _nodes = [];
    private OneCAdministrationToolDiagnosticsViewModel? _administrationToolDiagnostics;
    private OneCServiceProcessNode? _selectedNode;
    private int? _temporaryRasProcessId;

    public MainPage()
    {
        InitializeComponent();
        ServiceTreeRepeater.ItemsSource = _nodes;
        AdministrationToolsCard.DataContext = new OneCAdministrationToolDiagnosticsViewModel([], [], []);
        ClustersCard.DataContext = new OneCClusterDiagnosticsViewModel(
            CreateUnavailableClusterResult("localhost:1545", "Данные еще не обновлены"),
            "localhost:1540",
            canStartTemporaryRas: false,
            canStopTemporaryRas: false);
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
            var clusterDiagnostics = await GetClusterDiagnosticsAsync(administrationToolDiagnostics);
            var nodes = BuildServiceProcessTree(services, processes);
            var previousServiceName = _selectedNode?.Service?.Name;

            _administrationToolDiagnostics = administrationToolDiagnostics;
            AdministrationToolsCard.DataContext = administrationToolDiagnostics;
            ClustersCard.DataContext = clusterDiagnostics;

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

    private async Task<OneCClusterDiagnosticsViewModel> GetClusterDiagnosticsAsync(
        OneCAdministrationToolDiagnosticsViewModel administrationTools)
    {
        var address = administrationTools.AdministrationServerAddress;
        var canStopTemporaryRas = IsTemporaryRasRunning();
        var canStartTemporaryRas = administrationTools.RasTool is not null
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
        var draftCommand = string.Empty;

        void UpdateCommandPreview()
        {
            draftCommand = BuildInfobaseCreateDraftCommand(
                cluster,
                _administrationToolDiagnostics?.AdministrationServerAddress ?? "localhost:1545",
                infobaseNameTextBox.Text,
                null,
                null,
                GetDbmsOptionValue(dbmsComboBox),
                dbServerTextBox.Text,
                dbNameTextBox.Text,
                dbUserTextBox.Text,
                dbPasswordBox.Password,
                GetLocaleOptionValue(localeComboBox),
                GetSelectedText(dateOffsetComboBox),
                GetSecurityLevelOptionValue(securityLevelComboBox),
                scheduledJobsDenyCheckBox.IsChecked == true ? "on" : "off",
                GetLicenseDistributionOptionValue(licenseDistributionComboBox),
                descriptionTextBox.Text,
                createDatabaseCheckBox.IsChecked == true);
        }

        infobaseNameTextBox.TextChanged += (_, _) => UpdateCommandPreview();
        descriptionTextBox.TextChanged += (_, _) => UpdateCommandPreview();
        securityLevelComboBox.SelectionChanged += (_, _) => UpdateCommandPreview();
        dbServerTextBox.TextChanged += (_, _) => UpdateCommandPreview();
        dbmsComboBox.SelectionChanged += (_, _) => UpdateCommandPreview();
        dbNameTextBox.TextChanged += (_, _) => UpdateCommandPreview();
        dbUserTextBox.TextChanged += (_, _) => UpdateCommandPreview();
        dbPasswordBox.PasswordChanged += (_, _) => UpdateCommandPreview();
        licenseDistributionComboBox.SelectionChanged += (_, _) => UpdateCommandPreview();
        localeComboBox.SelectionChanged += (_, _) => UpdateCommandPreview();
        dateOffsetComboBox.SelectionChanged += (_, _) => UpdateCommandPreview();
        createDatabaseCheckBox.Checked += (_, _) => UpdateCommandPreview();
        createDatabaseCheckBox.Unchecked += (_, _) => UpdateCommandPreview();
        scheduledJobsDenyCheckBox.Checked += (_, _) => UpdateCommandPreview();
        scheduledJobsDenyCheckBox.Unchecked += (_, _) => UpdateCommandPreview();
        UpdateCommandPreview();

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

        var content = new StackPanel
        {
            Width = 590,
            Spacing = 0,
            Children =
            {
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
                            CreateDialogSectionTitle("Параметры информационной базы"),
                            formGrid
                        }
                    }
                },
                footer
            }
        };

        ContentDialog? dialog = null;
        okButton.Click += (_, _) =>
        {
            SetClipboardText(draftCommand);
            StatusText.Text = "Черновик команды создания базы был скопирован, rac не запускался";
            dialog?.Hide();
        };
        cancelButton.Click += (_, _) => dialog?.Hide();

        dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Новая информационная база",
            Content = content,
        };
        dialog.Resources["ContentDialogMaxWidth"] = 700d;
        dialog.Resources["ContentDialogMinWidth"] = 650d;

        await dialog.ShowAsync();
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

    private static void AddDialogFormRow(Grid grid, int row, string labelText, FrameworkElement control)
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

    private static string BuildInfobaseCreateDraftCommand(
        OneCClusterViewModel cluster,
        string administrationServerAddress,
        string? infobaseName,
        string? clusterUser,
        string? clusterPassword,
        string? dbms,
        string? dbServer,
        string? dbName,
        string? dbUser,
        string? dbPassword,
        string? locale,
        string? dateOffset,
        string? securityLevel,
        string? scheduledJobsDeny,
        string? licenseDistribution,
        string? description,
        bool createDatabase)
    {
        var arguments = new List<string>
        {
            "rac.exe",
            administrationServerAddress,
            "infobase",
            "create",
            $"--cluster={cluster.UuidText}"
        };

        AddOptionalQuotedOption(arguments, "--cluster-user", clusterUser);
        AddOptionalQuotedOption(arguments, "--cluster-pwd", clusterPassword);

        if (createDatabase)
        {
            arguments.Add("--create-database");
        }

        AddQuotedOption(arguments, "--name", infobaseName, "<имя базы>");
        arguments.Add($"--dbms={NormalizeOptionValue(dbms, "MSSQLServer")}");
        AddQuotedOption(arguments, "--db-server", dbServer, "<сервер БД>");
        AddQuotedOption(arguments, "--db-name", dbName, "<имя БД>");
        arguments.Add($"--locale={NormalizeOptionValue(locale, "ru_RU")}");
        AddOptionalQuotedOption(arguments, "--db-user", dbUser);
        AddOptionalQuotedOption(arguments, "--db-pwd", dbPassword);
        AddOptionalPlainOption(arguments, "--date-offset", dateOffset);
        AddOptionalPlainOption(arguments, "--security-level", securityLevel);
        AddOptionalPlainOption(arguments, "--scheduled-jobs-deny", scheduledJobsDeny);
        AddOptionalPlainOption(arguments, "--license-distribution", licenseDistribution);

        if (!string.IsNullOrWhiteSpace(description))
        {
            AddQuotedOption(arguments, "--descr", description, "<описание>");
        }

        return string.Join(" ", arguments);
    }

    private static void AddQuotedOption(
        List<string> arguments,
        string optionName,
        string? value,
        string placeholder)
    {
        arguments.Add($"{optionName}={QuoteCommandValue(NormalizeOptionValue(value, placeholder))}");
    }

    private static void AddOptionalQuotedOption(List<string> arguments, string optionName, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            arguments.Add($"{optionName}={QuoteCommandValue(value.Trim())}");
        }
    }

    private static void AddOptionalPlainOption(List<string> arguments, string optionName, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            arguments.Add($"{optionName}={value.Trim()}");
        }
    }

    private static string NormalizeOptionValue(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static string QuoteCommandValue(string value)
    {
        return $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    }
}
