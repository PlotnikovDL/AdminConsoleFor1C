using AdminConsoleFor1C.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using System.Diagnostics;
using System.Collections.ObjectModel;
using AdminConsoleFor1C.Application.Services;
using CommunityToolkit.Mvvm.Input;

namespace AdminConsoleFor1C.App;

public sealed partial class ServiceRegistrationViewModel : ObservableObject
{
    private readonly IReadOnlyList<OneCServiceInfo> existingServices;
    private readonly IWindowsUserAccountInventory accountInventory;

    public ServiceRegistrationViewModel(
        IReadOnlyList<OneCServiceInfo> existingServices,
        IEnumerable<string> executablePaths,
        string? selectedPath,
        IWindowsUserAccountInventory accountInventory)
    {
        this.existingServices = existingServices;
        this.accountInventory = accountInventory;
        CurrentUserName = accountInventory.CurrentUserName;
        UserNames.Add(CurrentUserName);
        UserName = CurrentUserName;
        ExecutablePaths = executablePaths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        ExecutablePath = selectedPath ?? ExecutablePaths.FirstOrDefault() ?? string.Empty;

        var port = 1540;
        while (port < 64000 && existingServices.Any(service => new OneCServiceRegistrationRequest
        {
            AgentPort = port, ClusterPort = port + 1, ProcessPortStart = port + 20, ProcessPortEnd = port + 51,
            DebugPort = port + 10
        }.ConflictsWithPorts(service)))
            port += 1000;

        AgentPort = port;
        ClusterPort = port + 1;
        ProcessPortStart = port + 20;
        ProcessPortEnd = port + 51;
        DebugPort = port + 10;
        DataDirectory = BuildDataDirectory(port);
    }

    public IReadOnlyList<string> ExecutablePaths { get; }
    public IReadOnlyList<string> Accounts { get; } =
        ["Сетевая служба (NetworkService)", "Локальная система (LocalSystem)", "Пользователь Windows", "Локальная служба (LocalService)"];
    public ObservableCollection<string> UserNames { get; } = [];
    public string CurrentUserName { get; }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(CommandLinePreview))]
    public partial string ExecutablePath { get; set; } = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ServiceName))]
    public partial string? PlatformVersion { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CommandLinePreview))]
    public partial string DataDirectory { get; set; } = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ServiceName))]
    [NotifyPropertyChangedFor(nameof(CommandLinePreview))]
    public partial double AgentPort { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CommandLinePreview))] public partial double ClusterPort { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CommandLinePreview))] public partial double ProcessPortStart { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CommandLinePreview))] public partial double ProcessPortEnd { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CommandLinePreview))] public partial double DebugPort { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CommandLinePreview))] public partial bool EnableDebug { get; set; }
    [ObservableProperty] public partial bool AutomaticStart { get; set; } = true;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(UserAccountVisibility))]
    public partial int AccountIndex { get; set; }
    [ObservableProperty] public partial string UserName { get; set; } = string.Empty;
    [ObservableProperty] public partial string? AccountDiscoveryMessage { get; set; }
    public Visibility UserAccountVisibility => AccountIndex == (int)OneCServiceAccount.WindowsUser ? Visibility.Visible : Visibility.Collapsed;

    public async Task LoadAccountsAsync()
    {
        try
        {
            var accounts = await accountInventory.GetUserNamesAsync();
            foreach (var account in accounts.Where(name => !UserNames.Contains(name, StringComparer.OrdinalIgnoreCase)))
                UserNames.Add(account);
        }
        catch (Exception)
        {
            AccountDiscoveryMessage = "Список локальных пользователей недоступен. Можно указать имя вручную.";
        }
    }

    [RelayCommand]
    private void UseCurrentUser() => UserName = CurrentUserName;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    [NotifyPropertyChangedFor(nameof(BusyVisibility))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorMessage { get; set; }

    public bool CanEdit => !IsBusy;
    public Visibility BusyVisibility => IsBusy ? Visibility.Visible : Visibility.Collapsed;
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public string ServiceName => PlatformVersion is not null && double.IsFinite(AgentPort)
        && AgentPort == Math.Truncate(AgentPort) && AgentPort is >= 1 and <= 65535
        ? OneCServiceNaming.BuildServerAgentName(PlatformVersion, (int)AgentPort)
        : string.Empty;

    partial void OnExecutablePathChanged(string value)
    {
        PlatformVersion = OneCServiceCommandLineParser.GetVersionFromExecutablePath(value);
        if (PlatformVersion is null && File.Exists(value))
        {
            try
            {
                var version = FileVersionInfo.GetVersionInfo(value);
                PlatformVersion = $"{version.FileMajorPart}.{version.FileMinorPart}.{version.FileBuildPart}.{version.FilePrivatePart}";
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            {
                PlatformVersion = null;
            }
        }
    }

    partial void OnAgentPortChanged(double oldValue, double newValue)
    {
        if (double.IsFinite(newValue) && newValue == Math.Truncate(newValue) && newValue is >= 1 and <= 65535
            && (string.IsNullOrEmpty(DataDirectory) || DataDirectory == BuildDataDirectory(oldValue)))
            DataDirectory = BuildDataDirectory(newValue);
    }

    private static string BuildDataDirectory(double port) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "1cv8", "srvinfo", port.ToString("0"));

    public string CommandLinePreview
    {
        get
        {
            if (!PortsAreValid())
                return "Для предпросмотра укажите корректные порты.";
            try { return (CreateRequest() with { Account = OneCServiceAccount.NetworkService, UserName = null }).BuildCommandLine(); }
            catch (ArgumentException) { return "Для предпросмотра укажите сервер, каталог данных и непересекающиеся порты."; }
        }
    }

    private bool PortsAreValid() => new[] { AgentPort, ClusterPort, ProcessPortStart, ProcessPortEnd }
        .Concat(EnableDebug ? [DebugPort] : Array.Empty<double>())
        .All(p => double.IsFinite(p) && p == Math.Truncate(p) && p is >= 1 and <= 65535);

    private OneCServiceRegistrationRequest CreateRequest() => new()
    {
        ServiceName = ServiceName,
        ExecutablePath = ExecutablePath.Trim(), DataDirectory = DataDirectory.Trim(),
        AgentPort = (int)AgentPort, ClusterPort = (int)ClusterPort,
        ProcessPortStart = (int)ProcessPortStart, ProcessPortEnd = (int)ProcessPortEnd,
        DebugPort = EnableDebug ? (int)DebugPort : null,
        AutomaticStart = AutomaticStart, Account = (OneCServiceAccount)AccountIndex,
        UserName = AccountIndex == (int)OneCServiceAccount.WindowsUser ? UserName.Trim() : null
    };

    public OneCServiceRegistrationRequest? Validate(string password, string confirmation)
    {
        if (PlatformVersion is null)
        {
            ErrorMessage = "Не удалось определить версию сервера для имени службы. Выберите установленный ragent.exe.";
            return null;
        }
        if (!PortsAreValid())
        {
            ErrorMessage = "Заполните порты целыми числами от 1 до 65535.";
            return null;
        }

        var request = CreateRequest();
        var errors = request.Validate(existingServices).ToList();
        if (request.Account == OneCServiceAccount.WindowsUser)
        {
            if (string.IsNullOrEmpty(password)) errors.Add("Введите пароль учетной записи Windows (не PIN-код входа).");
            else if (password != confirmation) errors.Add("Пароль и подтверждение не совпадают.");
        }
        if (errors.Count == 0 && !File.Exists(request.ExecutablePath))
            errors.Add("Файл ragent.exe не найден. Выберите установленный сервер 1С или укажите путь вручную.");
        if (Directory.Exists(request.DataDirectory) || File.Exists(request.DataDirectory))
            errors.Add("Каталог данных уже существует. Укажите новый каталог для новой службы.");
        ErrorMessage = errors.Count == 0 ? null : string.Join(Environment.NewLine, errors);
        return HasError ? null : request;
    }
}
