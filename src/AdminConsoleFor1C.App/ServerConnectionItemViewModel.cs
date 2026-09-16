using AdminConsoleFor1C.Core.Administration;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AdminConsoleFor1C.App;

public sealed partial class ServerConnectionItemViewModel(OneCServerConnectionProfile profile) : ObservableObject
{
    public OneCServerConnectionProfile Profile { get; } = profile;
    public string Name => Profile.Name;
    public string AddressText => $"{Profile.AgentAddress} · {Profile.PlatformVersion}";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Summary))] public partial string Status { get; set; } = "Не подключён";
    [ObservableProperty] public partial string? Error { get; set; }
    [ObservableProperty] public partial bool IsConnected { get; set; }
    public OneCServerSessionSnapshot? Snapshot { get; set; }
    public string Summary => Snapshot is not null && string.IsNullOrWhiteSpace(Error)
        ? $"Сеансов: {Snapshot.Clusters.Sum(c => c.Sessions.Count)}" : Status;
}

public sealed record ServerSessionRow(OneCServerConnectionProfile Profile, string ClusterUuid, string ClusterName,
    string InfobaseName, OneCSessionInfo Session)
{
    public string ServerText => Profile.Name;
    public string ServerDetails => $"{Profile.AgentAddress} · {Profile.PlatformVersion}";
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

public sealed record SessionConnectionFilter(Guid? Id, string Name, ServerConnectionItemViewModel? Connection = null)
{
    public string Details => Connection?.Profile.PlatformVersion ?? "Общий список сеансов";
    public override string ToString() => Name;
}
