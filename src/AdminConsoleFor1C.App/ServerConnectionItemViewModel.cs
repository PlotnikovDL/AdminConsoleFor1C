using AdminConsoleFor1C.Core.Administration;
using AdminConsoleFor1C.Core.Services;
using AdminConsoleFor1C.Application.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AdminConsoleFor1C.App;

public sealed partial class ServerConnectionItemViewModel(OneCServerConnectionProfile profile) : ObservableObject
{
    public OneCServerConnectionProfile Profile { get; } = profile;
    public bool IsDiscovered { get; init; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(SourceText))]
    public partial OneCServiceInfo? LocalService { get; set; }
    public string SourceText => LocalService is { } service
        ? $"Локальная служба · {service.State switch { "Stopped" => "Остановлена", "Paused" => "Приостановлена", _ => service.StateDisplayName }}"
        : "Сохранённое подключение";
    public string Name => ServerConnectionPresentation.DisplayName(Profile, Environment.MachineName);
    public string AddressText => $"Агент: {Profile.AgentAddress} · Платформа: {Profile.PlatformVersion}";
    public override string ToString() => $"{Name} · {AddressText}";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Summary))] public partial string Status { get; set; } = "Не подключён";
    [ObservableProperty] public partial string? Error { get; set; }
    [ObservableProperty] public partial bool IsConnected { get; set; }
    [ObservableProperty] public partial bool IsMonitoring { get; set; }
    public OneCServerSessionSnapshot? Snapshot { get; set; }
    public string Summary => Snapshot is not null && string.IsNullOrWhiteSpace(Error)
        ? $"Сеансов: {Snapshot.Clusters.Sum(c => c.Sessions.Count)}" : Status;
}

public sealed record ServerSessionRow(OneCServerConnectionProfile Profile, string ClusterUuid, string ClusterName,
    string InfobaseName, OneCSessionInfo Session)
{
    public string ServerText => ServerConnectionPresentation.DisplayName(Profile, Environment.MachineName);
    public string ServerDetails => $"Агент: {Profile.AgentAddress} · Платформа: {Profile.PlatformVersion}";
    public string SessionId => Session.Properties.TryGetValue("session-id", out var value) ? value : Session.Uuid;
    public string UserName => Session.UserName ?? "—";
    public string Host => Session.Host ?? "—";
    public string Application => Session.Application ?? "—";
    public string StartedAt => FormatDate(Session.StartedAt);
    public string LastActiveAt => FormatDate(Session.LastActiveAt);
    public string ConfirmationText => $"{Profile.AgentAddress} ({Profile.PlatformVersion}) · {InfobaseName} · {UserName} · сеанс {SessionId}";
    public OneCSessionTarget Target => new(Profile.Id, ClusterUuid, Session);
    public override string ToString() => ConfirmationText;
    private static string FormatDate(string? value) => DateTime.TryParse(value, out var date) ? date.ToString("g") : value ?? "—";
}

public sealed record ServerConnectionFilter(Guid? Id, string Name, ServerConnectionItemViewModel? Connection = null,
    string ScopeDescription = "Общий список сеансов")
{
    public string Details => Connection?.AddressText ?? ScopeDescription;
    public string SelectionText => Connection is null ? Name : $"{Name} · {Connection.Profile.PlatformVersion}";
    public override string ToString() => Name;
}
