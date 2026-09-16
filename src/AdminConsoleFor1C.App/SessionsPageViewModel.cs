using System.Collections.ObjectModel;
using AdminConsoleFor1C.Application.Services;
using AdminConsoleFor1C.Core.Administration;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;

namespace AdminConsoleFor1C.App;

public sealed partial class SessionsPageViewModel(IOneCServerSessionClient client, IOneCServerConnectionStore store) : ObservableObject
{
    private readonly Dictionary<Guid, string> passwords = [];
    private bool loaded;
    private bool synchronizingSelection;
    public ObservableCollection<ServerConnectionItemViewModel> Connections { get; } = [];
    public ObservableCollection<ServerSessionRow> Sessions { get; } = [];
    public ObservableCollection<SessionConnectionFilter> ConnectionFilters { get; } = [new(null, "Все серверы")];
    public ObservableCollection<string> InfobaseFilters { get; } = ["Все базы"];
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanEdit)), NotifyPropertyChangedFor(nameof(CanManageSelected))]
    public partial bool IsBusy { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanEdit)), NotifyPropertyChangedFor(nameof(CanManageSelected))]
    public partial bool IsLoaded { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanManageSelected)), NotifyPropertyChangedFor(nameof(SelectedConnectionError))]
    public partial ServerConnectionItemViewModel? SelectedConnection { get; set; }
    [ObservableProperty] public partial SessionConnectionFilter? ConnectionFilter { get; set; }
    [ObservableProperty] public partial string? InfobaseFilter { get; set; }
    [ObservableProperty] public partial string SearchText { get; set; } = string.Empty;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasMessage))] public partial string? Message { get; set; }
    [ObservableProperty] public partial InfoBarSeverity MessageSeverity { get; set; } = InfoBarSeverity.Informational;
    [ObservableProperty] public partial string Status { get; set; } = "Добавьте сервер для просмотра сеансов";
    public bool HasMessage => !string.IsNullOrWhiteSpace(Message);
    public bool CanEdit => IsLoaded && !IsBusy;
    public bool CanManageSelected => CanEdit && SelectedConnection is not null;
    public string SelectedConnectionError => SelectedConnection?.Error ?? string.Empty;
    public bool HasConnectionError => !string.IsNullOrWhiteSpace(SelectedConnectionError);
    public string? GetPassword(Guid id) => passwords.GetValueOrDefault(id);

    public async Task LoadAsync()
    {
        if (loaded) return;
        loaded = true;
        IsBusy = true;
        try
        {
            foreach (var profile in await store.LoadAsync()) Connections.Add(new(profile));
            IsLoaded = true;
            RebuildFilters();
            RebuildSessions();
        }
        catch (Exception ex) { ShowError("Не удалось загрузить подключения: " + ex.Message); }
        finally { IsBusy = false; }
    }

    public async Task SaveConnectionAsync(OneCServerConnectionProfile profile, string? password)
    {
        if (!CanEdit) throw new InvalidOperationException("Дождитесь завершения текущей операции.");
        IsBusy = true;
        try
        {
            var profiles = Connections.Select(c => c.Profile).Where(p => p.Id != profile.Id).Append(profile).ToArray();
            await store.SaveAsync(profiles);
            var old = Connections.FirstOrDefault(c => c.Profile.Id == profile.Id);
            if (old is not null) Connections.Remove(old);
            var item = new ServerConnectionItemViewModel(profile);
            Connections.Add(item);
            passwords[profile.Id] = password ?? string.Empty;
            RebuildFilters();
            SelectedConnection = item;
            RebuildSessions();
        }
        finally { IsBusy = false; }
    }

    public async Task RemoveSelectedAsync()
    {
        if (!CanManageSelected || SelectedConnection is not { } selected) return;
        IsBusy = true;
        try
        {
            await store.SaveAsync(Connections.Where(c => c != selected).Select(c => c.Profile).ToArray());
            Connections.Remove(selected);
            passwords.Remove(selected.Profile.Id);
            SelectedConnection = null;
            RebuildFilters();
            RebuildSessions();
        }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(CanManageSelected))]
    private async Task ConnectSelectedAsync()
    {
        if (SelectedConnection is not { } connection) return;
        await RefreshConnectionsAsync([connection]);
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private Task RefreshAllAsync() => RefreshConnectionsAsync(Connections.Where(c => c.IsConnected).ToArray());

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private Task ConnectAllAsync() => RefreshConnectionsAsync(Connections.ToArray());

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private Task RefreshScopeAsync() => RefreshConnectionsAsync(SelectedConnection is { } selected
        ? [selected] : Connections.ToArray());

    [RelayCommand(CanExecute = nameof(CanManageSelected))]
    private void DisconnectSelected()
    {
        if (SelectedConnection is not { } connection) return;
        connection.IsConnected = false;
        connection.Snapshot = null;
        connection.Status = "Не подключён";
        connection.Error = null;
        RebuildFilters();
        RebuildSessions();
    }

    private async Task RefreshConnectionsAsync(IReadOnlyList<ServerConnectionItemViewModel> connections)
    {
        if (!CanEdit || connections.Count == 0) return;
        IsBusy = true;
        Message = null;
        try
        {
            using var gate = new SemaphoreSlim(3);
            await Task.WhenAll(connections.Select(async connection =>
            {
                await gate.WaitAsync();
                try { await ReadConnectionAsync(connection); }
                finally { gate.Release(); }
            }));
            RebuildFilters();
            RebuildSessions();
        }
        finally { IsBusy = false; }
    }

    private async Task ReadConnectionAsync(ServerConnectionItemViewModel connection)
    {
        connection.Status = "Подключение…";
        connection.Error = null;
        connection.IsConnected = true;
        try
        {
            connection.Snapshot = await client.ReadAsync(connection.Profile, GetPassword(connection.Profile.Id));
            var errors = connection.Snapshot.Clusters.Where(c => !string.IsNullOrWhiteSpace(c.DetailsMessage))
                .Select(c => $"{c.NameText}: {c.DetailsMessage}").ToArray();
            connection.Error = errors.Length > 0 ? string.Join("\n", errors) : null;
            connection.Status = errors.Length > 0 ? "Частичные данные · проверьте доступ"
                : $"Кластеров: {connection.Snapshot.Clusters.Count} · баз: {connection.Snapshot.Clusters.Sum(c => c.Infobases.Count)} · сеансов: {connection.Snapshot.Clusters.Sum(c => c.Sessions.Count)} · {connection.Snapshot.UpdatedAt:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            // Failed refresh must not leave stale sessions available for termination.
            connection.Snapshot = null;
            connection.Status = "Ошибка подключения";
            connection.Error = ex.Message;
        }
        OnPropertyChanged(nameof(SelectedConnectionError));
        OnPropertyChanged(nameof(HasConnectionError));
    }

    public async Task TerminateAsync(IReadOnlyList<ServerSessionRow> rows, string reason)
    {
        if (!CanEdit || rows.Count == 0) return;
        IsBusy = true;
        Message = null;
        var success = 0;
        var errors = new List<string>();
        try
        {
            foreach (var group in rows.GroupBy(r => r.Profile.Id))
            {
                var connection = Connections.SingleOrDefault(c => c.Profile.Id == group.Key);
                if (connection is null || !connection.IsConnected || connection.Snapshot is null
                    || group.Any(r => r.Profile != connection.Profile))
                {
                    errors.Add("Параметры подключения изменились. Выберите сеансы заново.");
                    continue;
                }
                try
                {
                    var results = await client.TerminateAsync(connection.Profile, GetPassword(group.Key),
                        group.Select(r => r.Target).ToArray(), reason);
                    success += results.Count(r => r.Success);
                    errors.AddRange(results.Where(r => !r.Success).Select(r => $"{connection.Name} · {r.SessionUuid}: {r.Message}"));
                }
                catch (Exception ex) { errors.Add(connection.Name + ": " + ex.Message); }
                await ReadConnectionAsync(connection);
            }
            MessageSeverity = errors.Count > 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Success;
            Message = $"Завершено сеансов: {success}." + (errors.Count > 0 ? "\n" + string.Join("\n", errors) : string.Empty);
            RebuildFilters();
            RebuildSessions();
        }
        finally { IsBusy = false; }
    }

    private IEnumerable<ServerSessionRow> AllRows() => Connections.Where(c => c.IsConnected && c.Snapshot is not null)
        .SelectMany(c => c.Snapshot!.Clusters.SelectMany(cluster => cluster.Sessions.Select(session => new ServerSessionRow(
            c.Profile, cluster.Uuid, cluster.NameText,
            cluster.Infobases.FirstOrDefault(b => b.Uuid == session.InfobaseUuid)?.NameText ?? session.InfobaseText, session))));

    private void RebuildFilters()
    {
        var selected = ConnectionFilter?.Id;
        synchronizingSelection = true;
        try
        {
            ConnectionFilters.Clear();
            ConnectionFilters.Add(new(null, "Все серверы"));
            foreach (var connection in Connections) ConnectionFilters.Add(new(connection.Profile.Id, connection.Name, connection));
            ConnectionFilter = ConnectionFilters.FirstOrDefault(f => f.Id == selected) ?? ConnectionFilters[0];
            SelectedConnection = Connections.FirstOrDefault(c => c.Profile.Id == ConnectionFilter.Id);
            RebuildInfobaseFilters();
        }
        finally { synchronizingSelection = false; }
    }

    private void RebuildInfobaseFilters()
    {
        var infobase = InfobaseFilter;
        var selected = ConnectionFilter?.Id;
        InfobaseFilters.Clear();
        InfobaseFilters.Add("Все базы");
        foreach (var name in Connections.Where(c => c.IsConnected && c.Snapshot is not null
                && (selected is null || c.Profile.Id == selected))
            .SelectMany(c => c.Snapshot!.Clusters.SelectMany(cluster => cluster.Infobases.Select(b => b.NameText)))
            .Concat(AllRows().Where(r => selected is null || r.Profile.Id == selected).Select(r => r.InfobaseName))
            .Distinct().Order()) InfobaseFilters.Add(name);
        InfobaseFilter = infobase is not null && InfobaseFilters.Contains(infobase) ? infobase : InfobaseFilters[0];
    }

    private void RebuildSessions()
    {
        var rows = AllRows().Where(r => (ConnectionFilter?.Id is not { } id || r.Profile.Id == id)
            && (string.IsNullOrEmpty(InfobaseFilter) || InfobaseFilter == "Все базы" || r.InfobaseName == InfobaseFilter)
            && (string.IsNullOrWhiteSpace(SearchText) || $"{r.ServerText} {r.ServerDetails} {r.ClusterName} {r.InfobaseName} {r.UserName} {r.Host} {r.Application} {r.SessionId}"
                .Contains(SearchText, StringComparison.CurrentCultureIgnoreCase)))
            .OrderBy(r => r.ServerText).ThenBy(r => r.InfobaseName).ThenBy(r => r.UserName).ToArray();
        Sessions.Clear();
        foreach (var row in rows) Sessions.Add(row);
        Status = $"Подключений: {Connections.Count} · показывается сеансов: {Sessions.Count}";
        OnPropertyChanged(nameof(SelectedConnectionError));
        OnPropertyChanged(nameof(HasConnectionError));
    }

    public void ShowError(string message) { MessageSeverity = InfoBarSeverity.Error; Message = message; }
    partial void OnConnectionFilterChanged(SessionConnectionFilter? value)
    {
        if (synchronizingSelection) return;
        synchronizingSelection = true;
        try
        {
            SelectedConnection = Connections.FirstOrDefault(c => c.Profile.Id == value?.Id);
            RebuildInfobaseFilters();
        }
        finally { synchronizingSelection = false; }
        RebuildSessions();
    }
    partial void OnInfobaseFilterChanged(string? value)
    {
        if (!synchronizingSelection) RebuildSessions();
    }
    partial void OnSearchTextChanged(string value) => RebuildSessions();
    partial void OnSelectedConnectionChanged(ServerConnectionItemViewModel? value)
    {
        NotifyCommands();
        OnPropertyChanged(nameof(HasConnectionError));
        if (synchronizingSelection) return;
        synchronizingSelection = true;
        try
        {
            ConnectionFilter = ConnectionFilters.FirstOrDefault(f => f.Id == value?.Profile.Id) ?? ConnectionFilters[0];
            RebuildInfobaseFilters();
        }
        finally { synchronizingSelection = false; }
        RebuildSessions();
    }
    partial void OnIsBusyChanged(bool value) => NotifyCommands();
    partial void OnIsLoadedChanged(bool value) => NotifyCommands();
    private void NotifyCommands()
    {
        ConnectSelectedCommand.NotifyCanExecuteChanged();
        ConnectAllCommand.NotifyCanExecuteChanged();
        RefreshAllCommand.NotifyCanExecuteChanged();
        RefreshScopeCommand.NotifyCanExecuteChanged();
        DisconnectSelectedCommand.NotifyCanExecuteChanged();
    }
}
