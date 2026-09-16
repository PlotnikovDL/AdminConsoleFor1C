using System.Collections.ObjectModel;
using AdminConsoleFor1C.Application.Services;
using AdminConsoleFor1C.Core.Administration;
using AdminConsoleFor1C.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AdminConsoleFor1C.App;

public sealed partial class ServerConnectionEditorViewModel : ObservableObject
{
    private readonly Guid id;
    private string suggestedName = string.Empty;
    private readonly IOneCServiceInventory services;
    private readonly IOneCAdministrationToolInventory tools;
    public ServerConnectionEditorViewModel(OneCServerConnectionProfile? profile,
        IOneCServiceInventory services, IOneCAdministrationToolInventory tools)
    {
        this.services = services;
        this.tools = tools;
        id = profile?.Id ?? Guid.NewGuid();
        Name = profile?.Name ?? "";
        Host = profile?.Host ?? "";
        AgentPort = profile?.AgentPort ?? 1540;
        PlatformDirectory = profile?.PlatformDirectory ?? "";
        ClusterUser = profile?.ClusterUser ?? "";
        suggestedName = SuggestedName();
    }

    public ObservableCollection<string> PlatformDirectories { get; } = [];
    public ObservableCollection<LocalAgentChoice> LocalAgents { get; } = [];
    [ObservableProperty] public partial LocalAgentChoice? LocalAgent { get; set; }
    [ObservableProperty] public partial string Name { get; set; }
    [ObservableProperty] public partial string Host { get; set; }
    [ObservableProperty] public partial double AgentPort { get; set; }
    [ObservableProperty] public partial string PlatformDirectory { get; set; }
    [ObservableProperty] public partial string ClusterUser { get; set; }
    [ObservableProperty] public partial string? DiscoveryMessage { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasError))] public partial string? Error { get; set; }
    public bool HasError => !string.IsNullOrWhiteSpace(Error);

    public async Task LoadAsync()
    {
        IReadOnlyList<OneCServiceInfo> local = [];
        try { local = await services.GetServicesAsync(); }
        catch (Exception) { DiscoveryMessage = "Локальные службы недоступны. Укажите сервер и порт вручную."; }
        try
        {
            var inventory = await tools.GetToolsAsync(local.Select(s => s.ExecutablePath).OfType<string>().ToArray());
            foreach (var tool in inventory.Where(t => t.Kind == OneCAdministrationToolKind.Ras))
            {
                var bin = Path.GetDirectoryName(tool.FilePath)!;
                if (File.Exists(Path.Combine(bin, "rac.exe")) && !PlatformDirectories.Contains(bin)) PlatformDirectories.Add(bin);
            }
            foreach (var service in local.Where(s => s.Kind == OneCServiceKind.ServerAgent && s.ExecutablePath is not null))
                LocalAgents.Add(new(service.DisplayName, service.AgentPort ?? 1540, Path.GetDirectoryName(service.ExecutablePath!)!));
            if (string.IsNullOrWhiteSpace(PlatformDirectory)) PlatformDirectory = PlatformDirectories.FirstOrDefault() ?? "";
        }
        catch (Exception ex) { DiscoveryMessage = "Не удалось найти платформы: " + ex.Message; }
    }

    partial void OnHostChanged(string value) => UpdateSuggestedName();
    partial void OnAgentPortChanged(double value) => UpdateSuggestedName();
    private string SuggestedName() => string.IsNullOrWhiteSpace(Host) ? "" : $"{Host.Trim()}:{AgentPort:0}";
    private void UpdateSuggestedName()
    {
        var next = SuggestedName();
        if (string.IsNullOrWhiteSpace(Name) || Name == suggestedName) Name = next;
        suggestedName = next;
    }

    partial void OnLocalAgentChanged(LocalAgentChoice? value)
    {
        if (value is null) return;
        Host = "localhost";
        AgentPort = value.Port;
        PlatformDirectory = value.PlatformDirectory;
        Name = $"{Environment.MachineName}:{value.Port}";
    }

    public OneCServerConnectionProfile? Validate()
    {
        try
        {
            if (!double.IsFinite(AgentPort) || AgentPort != Math.Truncate(AgentPort) || AgentPort is < 1 or > 65535)
                throw new ArgumentException("Укажите целый порт агента от 1 до 65535.");
            var profile = new OneCServerConnectionProfile
            {
                Id = id, Name = Name.Trim(), Host = Host.Trim(), AgentPort = (int)AgentPort,
                PlatformDirectory = PlatformDirectory.Trim(), ClusterUser = ClusterUser.Trim(),
                PlatformVersion = AdminConsoleViewModelFactory.GetAdministrationPlatformVersion(PlatformDirectory.Trim())
            };
            profile.Validate();
            Error = null;
            return profile;
        }
        catch (Exception ex) { Error = ex.Message; return null; }
    }
}

public sealed record LocalAgentChoice(string Name, int Port, string PlatformDirectory)
{
    public override string ToString() => Name;
}
