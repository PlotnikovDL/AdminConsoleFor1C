using System.Collections.ObjectModel;
using AdminConsoleFor1C.Application.Services;
using AdminConsoleFor1C.Core.Administration;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;

namespace AdminConsoleFor1C.App;

public sealed partial class SessionsPageViewModel(IOneCServerSessionClient client, IOneCServerConnectionStore store,
    LocalServerConnectionCatalog localServers) : ObservableObject
{
    private readonly Dictionary<Guid, string> passwords = [];
    private readonly SemaphoreSlim operationGate = new(1, 1);
    private bool loaded;
    private bool synchronizingSelection;
    private Guid? singleClusterSelectionServerId;
    private Task? loadTask;
    private Task? activationTask;
    private bool activationRequested;
    public ObservableCollection<ServerConnectionItemViewModel> Connections { get; } = [];
    public ObservableCollection<ServerSessionRow> Sessions { get; } = [];
    public ObservableCollection<ServerConnectionFilter> ConnectionFilters { get; } = [new(null, "Все серверы")];
    public ObservableCollection<ServerClusterFilter> Clusters { get; } = [ServerClusterFilter.All];
    public ObservableCollection<ServerInfobaseFilter> InfobaseFilters { get; } = [ServerInfobaseFilter.All];
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanEdit)), NotifyPropertyChangedFor(nameof(CanManageSelected)), NotifyPropertyChangedFor(nameof(CanRemoveSelected))]
    public partial bool IsBusy { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanEdit)), NotifyPropertyChangedFor(nameof(CanManageSelected)), NotifyPropertyChangedFor(nameof(CanRemoveSelected))]
    public partial bool IsLoaded { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanManageSelected)), NotifyPropertyChangedFor(nameof(CanRemoveSelected)), NotifyPropertyChangedFor(nameof(SelectedConnectionError))]
    public partial ServerConnectionItemViewModel? SelectedConnection { get; set; }
    [ObservableProperty] public partial ServerConnectionFilter? ConnectionFilter { get; set; }
    [ObservableProperty] public partial ServerClusterFilter? SelectedCluster { get; set; } = ServerClusterFilter.All;
    [ObservableProperty] public partial ServerInfobaseFilter? InfobaseFilter { get; set; } = ServerInfobaseFilter.All;
    [ObservableProperty] public partial string SearchText { get; set; } = string.Empty;
    [ObservableProperty] public partial bool IsAutoRefreshEnabled { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasMessage))] public partial string? Message { get; set; }
    [ObservableProperty] public partial InfoBarSeverity MessageSeverity { get; set; } = InfoBarSeverity.Informational;
    [ObservableProperty] public partial string Status { get; set; } = "Добавьте сервер для просмотра сеансов";
    public bool HasMessage => !string.IsNullOrWhiteSpace(Message);
    public bool CanEdit => IsLoaded && !IsBusy;
    public bool CanManageSelected => CanEdit && SelectedConnection is not null;
    public bool CanRemoveSelected => CanManageSelected && SelectedConnection is { IsDiscovered: false };
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasDiscoveryError))]
    public partial string? DiscoveryError { get; set; }
    public bool HasDiscoveryError => !string.IsNullOrWhiteSpace(DiscoveryError);
    private Guid? RefreshServerId => ConnectionFilter?.Id ?? SelectedCluster?.ServerId ?? InfobaseFilter?.ServerId;
    private IEnumerable<ServerConnectionItemViewModel> CurrentScope => Connections.Where(c => RefreshServerId is not { } id || c.Profile.Id == id);
    public string SelectedConnectionError => string.Join("\n", CurrentScope.Where(c => !string.IsNullOrWhiteSpace(c.Error)).Select(c => $"{c.Name}: {c.Error}"));
    public InfoBarSeverity ConnectionErrorSeverity => CurrentScope.Any(c => c.IsConnected)
        ? InfoBarSeverity.Warning : InfoBarSeverity.Error;
    public bool HasConnectionError => !string.IsNullOrWhiteSpace(SelectedConnectionError);
    public string EmptyStateText
    {
        get
        {
            if (Connections.Count == 0) return "Добавьте сервер, чтобы просматривать сеансы.";
            var scope = CurrentScope.ToArray();
            if (!scope.Any(c => c.IsMonitoring))
                return "Нажмите «Обновить», чтобы получить сеансы выбранной области.";
            if (scope.Any(c => c.IsMonitoring && !string.IsNullOrWhiteSpace(c.Error)))
                return scope.Any(c => c.IsConnected && c.Snapshot is not null)
                    ? "Данные о сеансах неполные. Проверьте состояние серверов и сообщения подключения."
                    : "Не удалось получить сеансы. Проверьте состояние серверов и сообщения подключения.";
            return "Сеансы не найдены. Проверьте выбранные кластер, базу и поиск.";
        }
    }
    public string? GetPassword(Guid id) => passwords.GetValueOrDefault(id);

    public Task ActivateAsync()
    {
        activationRequested = true;
        if (activationTask is { IsCompleted: false }) return activationTask;
        return activationTask = ActivateCoreAsync();
    }

    private async Task ActivateCoreAsync()
    {
        await LoadAsync();
        if (!IsLoaded) return;
        do
        {
            await RefreshConnectionsAsync(() =>
            {
                activationRequested = false;
                return RefreshServerId;
            });
        }
        while (activationRequested);
    }

    public async Task WaitForIdleAsync()
    {
        await operationGate.WaitAsync();
        operationGate.Release();
    }

    private async Task RunOperationAsync(Func<Task> operation)
    {
        await operationGate.WaitAsync();
        IsBusy = true;
        try { await operation(); }
        finally { IsBusy = false; operationGate.Release(); }
    }

    public Task LoadAsync()
    {
        if (loaded) return Task.CompletedTask;
        if (loadTask is { IsCompleted: false }) return loadTask;
        return loadTask = RunOperationAsync(async () =>
        {
            try
            {
                var profiles = await store.LoadAsync();
                Connections.Clear();
                foreach (var profile in profiles) Connections.Add(new(profile));
                await RefreshLocalConnectionsCoreAsync();
                loaded = true;
                IsLoaded = true;
                RebuildFilters();
                RebuildSessions();
            }
            catch (Exception ex) { loaded = false; IsLoaded = false; ShowError("Не удалось загрузить подключения: " + ex.Message); }
        });
    }

    public Task RefreshLocalConnectionsAsync()
    {
        return !IsLoaded ? LoadAsync() : RunOperationAsync(RefreshLocalConnectionsCoreAsync);
    }

    private async Task RefreshLocalConnectionsCoreAsync()
    {
        try
        {
            var entries = await localServers.ReadAsync(Connections.Where(c => !c.IsDiscovered).Select(c => c.Profile).ToArray());
            // Retain unchanged item instances, selection and session snapshots during refresh.
            foreach (var old in Connections.Where(c => c.IsDiscovered && !entries.Any(e => e.Profile == c.Profile)).ToArray())
                Connections.Remove(old);
            foreach (var entry in entries)
            {
                var item = Connections.FirstOrDefault(c => c.Profile == entry.Profile);
                if (item is null)
                {
                    item = new(entry.Profile) { IsDiscovered = entry.IsDiscovered };
                    Connections.Add(item);
                }
                item.LocalService = entry.LocalService;
            }
            DiscoveryError = null;
            RebuildFilters();
            RebuildSessions();
        }
        catch (Exception ex) { DiscoveryError = "Не удалось обновить локальные службы: " + ex.Message; }
    }

    public Task SaveConnectionAsync(OneCServerConnectionProfile profile, string? password)
    {
        if (!CanEdit) throw new InvalidOperationException("Дождитесь завершения текущей операции.");
        return RunOperationAsync(async () =>
        {
            var profiles = Connections.Where(c => !c.IsDiscovered).Select(c => c.Profile).Where(p => p.Id != profile.Id).Append(profile).ToArray();
            await store.SaveAsync(profiles);
            var old = Connections.FirstOrDefault(c => c.Profile.Id == profile.Id);
            if (old is not null) Connections.Remove(old);
            var item = new ServerConnectionItemViewModel(profile);
            Connections.Add(item);
            passwords[profile.Id] = password ?? string.Empty;
            await RefreshLocalConnectionsCoreAsync();
            RebuildFilters();
            SelectedConnection = item;
            RebuildSessions();
        });
    }

    public Task RemoveSelectedAsync()
    {
        if (!CanRemoveSelected || SelectedConnection is not { } selected) return Task.CompletedTask;
        return RunOperationAsync(async () =>
        {
            try
            {
                await store.SaveAsync(Connections.Where(c => c != selected && !c.IsDiscovered).Select(c => c.Profile).ToArray());
                Connections.Remove(selected);
                passwords.Remove(selected.Profile.Id);
                SelectedConnection = null;
                await RefreshLocalConnectionsCoreAsync();
                RebuildFilters();
                RebuildSessions();
            }
            catch (Exception ex) { ShowError(ex.Message); }
        });
    }

    [RelayCommand(CanExecute = nameof(CanManageSelected))]
    private async Task ConnectSelectedAsync()
    {
        if (SelectedConnection is not { } connection) return;
        await RefreshConnectionsAsync(() => connection.Profile.Id);
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private Task RefreshAllAsync() => RefreshConnectionsAsync(() => null);

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private Task ConnectAllAsync() => RefreshAllAsync();

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private Task ConnectScopeAsync() => RefreshScopeAsync();

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private Task RefreshScopeAsync() => RefreshConnectionsAsync(() => RefreshServerId);

    private Task RefreshConnectionsAsync(Func<Guid?> getServerId)
    {
        return RunOperationAsync(async () =>
        {
            if (!IsLoaded) return;
            Message = null;
            var connectionId = getServerId();
            await RefreshLocalConnectionsCoreAsync();
            var connections = Connections.Where(c => connectionId is null || c.Profile.Id == connectionId).ToArray();
            foreach (var connection in connections) connection.IsMonitoring = true;
            using var gate = new SemaphoreSlim(3);
            await Task.WhenAll(connections.Select(async connection =>
            {
                await gate.WaitAsync();
                try { await ReadConnectionAsync(connection); }
                finally { gate.Release(); }
            }));
            RebuildFilters();
            RebuildSessions();
        });
    }

    private async Task ReadConnectionAsync(ServerConnectionItemViewModel connection)
    {
        connection.Status = "Подключение…";
        connection.Error = null;
        try
        {
            if (connection.LocalService is { State: not "Running" })
                throw new InvalidOperationException("Локальная служба агента не работает. Запустите её в разделе «Службы», затем обновите список.");
            connection.Snapshot = await client.ReadAsync(connection.Profile, GetPassword(connection.Profile.Id));
            connection.IsConnected = true;
            var errors = connection.Snapshot.Clusters.Where(c => !string.IsNullOrWhiteSpace(c.DetailsMessage))
                .Select(c => $"{c.NameText}: {c.DetailsMessage}").ToArray();
            connection.Error = errors.Length > 0 ? string.Join("\n", errors) : null;
            connection.Status = errors.Length > 0 ? "Частичные данные · проверьте сообщения подключения"
                : $"Кластеров: {connection.Snapshot.Clusters.Count} · баз: {connection.Snapshot.Clusters.Sum(c => c.Infobases.Count)} · сеансов: {connection.Snapshot.Clusters.Sum(c => c.Sessions.Count)} · {connection.Snapshot.UpdatedAt:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            // Failed refresh must not leave stale sessions available for termination.
            connection.IsConnected = false;
            connection.Snapshot = null;
            connection.Status = connection.LocalService is { State: not "Running" }
                ? "Локальная служба не работает" : "Ошибка подключения";
            connection.Error = ex.Message;
        }
        OnPropertyChanged(nameof(SelectedConnectionError));
        OnPropertyChanged(nameof(HasConnectionError));
    }

    public Task TerminateAsync(IReadOnlyList<ServerSessionRow> rows, string reason)
    {
        if (!CanEdit || rows.Count == 0) return Task.CompletedTask;
        return RunOperationAsync(async () =>
        {
            Message = null;
            var success = 0;
            var errors = new List<string>();
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
        });
    }

    private IEnumerable<ServerSessionRow> AllRows() => Connections.Where(c => c.IsConnected && c.Snapshot is not null)
        .SelectMany(c => c.Snapshot!.Clusters.SelectMany(cluster => cluster.Sessions.Select(session => new ServerSessionRow(
            c.Profile, cluster.Uuid, cluster.NameText,
            cluster.Infobases.FirstOrDefault(b => UuidEquals(b.Uuid, session.InfobaseUuid))?.NameText ?? session.InfobaseText, session))));

    private void RebuildFilters()
    {
        var selected = ConnectionFilter?.Id;
        synchronizingSelection = true;
        try
        {
            ConnectionFilters.Clear();
            ConnectionFilters.Add(new(null, "Все серверы"));
            foreach (var connection in ServerConnectionPresentation.OrderForDisplay(Connections,
                c => c.Profile, c => c.LocalService is not null || c.IsDiscovered, Environment.MachineName))
                ConnectionFilters.Add(new(connection.Profile.Id, connection.Name, connection));
            ConnectionFilter = ConnectionFilters.FirstOrDefault(f => f.Id == selected) ?? ConnectionFilters[0];
            SelectedConnection = Connections.FirstOrDefault(c => c.Profile.Id == ConnectionFilter.Id);
            RebuildClusterFilters();
            RebuildInfobaseFilters();
        }
        finally { synchronizingSelection = false; }
    }

    private IEnumerable<(ServerConnectionItemViewModel Connection, OneCClusterInfo Cluster)> ScopedClusters()
        => Connections.Where(c => c.IsConnected && c.Snapshot is not null
                && (ConnectionFilter?.Id is not { } id || c.Profile.Id == id))
            .SelectMany(c => c.Snapshot!.Clusters.Select(cluster => (c, cluster)));

    private void RebuildClusterFilters()
    {
        var selected = SelectedCluster;
        Clusters.Clear();
        Clusters.Add(ServerClusterFilter.All);
        foreach (var choice in ScopedClusters()
            .Select(c => new ServerClusterFilter(c.Connection.Profile.Id, c.Cluster.Uuid, c.Cluster.NameText,
                ServerDataScopePresentation.ClusterDetails(c.Connection.Profile, c.Cluster), c.Cluster.AddressText))
            .DistinctBy(c => (c.ServerId, c.ClusterUuid?.ToUpperInvariant()))
            .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(c => c.Details, StringComparer.CurrentCultureIgnoreCase))
            Clusters.Add(choice);
        var selectedChoice = selected is { IsAll: false }
            ? Clusters.FirstOrDefault(c => c.ServerId == selected.ServerId && UuidEquals(c.ClusterUuid, selected.ClusterUuid)) : ServerClusterFilter.All;
        if (selectedChoice is null && selected is { IsAll: false } && CanKeepPendingCluster(selected))
        {
            Clusters.Add(selected);
            selectedChoice = selected;
        }
        SelectedCluster = selectedChoice ?? ServerClusterFilter.All;
        if (singleClusterSelectionServerId is { } serverId && SelectedConnection is { IsConnected: true, Snapshot: { } snapshot } connection
            && connection.Profile.Id == serverId)
        {
            if (snapshot.Clusters.Count == 1)
                SelectedCluster = Clusters.FirstOrDefault(c => c.ServerId == serverId && UuidEquals(c.ClusterUuid, snapshot.Clusters[0].Uuid)) ?? ServerClusterFilter.All;
            singleClusterSelectionServerId = null;
        }
    }

    private void RebuildInfobaseFilters()
    {
        var selected = InfobaseFilter;
        InfobaseFilters.Clear();
        InfobaseFilters.Add(ServerInfobaseFilter.All);
        foreach (var choice in ScopedClusters()
            .Where(c => MatchesCluster(c.Connection.Profile.Id, c.Cluster.Uuid))
            .SelectMany(c =>
            {
                var details = ServerDataScopePresentation.InfobaseDetails(c.Connection.Profile, c.Cluster);
                var target = $"{c.Connection.Profile.AgentAddress} · {c.Cluster.AddressText}";
                return c.Cluster.Infobases.Select(b => new ServerInfobaseFilter(c.Connection.Profile.Id, c.Cluster.Uuid, b.Uuid, b.NameText, details, target))
                    .Concat(c.Cluster.Sessions.Where(s => !c.Cluster.Infobases.Any(b => UuidEquals(b.Uuid, s.InfobaseUuid)))
                        .Select(s => new ServerInfobaseFilter(c.Connection.Profile.Id, c.Cluster.Uuid, s.InfobaseUuid,
                            string.IsNullOrWhiteSpace(s.InfobaseUuid) ? "База не определена" : s.InfobaseText, details, target)));
            })
            .DistinctBy(c => (c.ServerId, c.ClusterUuid?.ToUpperInvariant(), c.InfobaseUuid?.ToUpperInvariant()))
            .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(c => c.Details, StringComparer.CurrentCultureIgnoreCase))
            InfobaseFilters.Add(choice);
        var selectedChoice = selected is { IsAll: false }
            ? InfobaseFilters.FirstOrDefault(c => c.ServerId == selected.ServerId && UuidEquals(c.ClusterUuid, selected.ClusterUuid)
                && UuidEquals(c.InfobaseUuid, selected.InfobaseUuid)) : ServerInfobaseFilter.All;
        if (selectedChoice is null && selected is { IsAll: false } && CanKeepPendingInfobase(selected))
        {
            InfobaseFilters.Add(selected);
            selectedChoice = selected;
        }
        InfobaseFilter = selectedChoice ?? ServerInfobaseFilter.All;
    }

    private bool CanKeepPendingCluster(ServerClusterFilter selected)
        => (ConnectionFilter?.Id is not { } id || selected.ServerId == id)
            && Connections.Any(c => c.Profile.Id == selected.ServerId && !c.IsConnected && !string.IsNullOrWhiteSpace(c.Error));

    private bool CanKeepPendingInfobase(ServerInfobaseFilter selected)
    {
        if (ConnectionFilter?.Id is { } id && selected.ServerId != id
            || SelectedCluster is { IsAll: false } cluster && (cluster.ServerId != selected.ServerId || !UuidEquals(cluster.ClusterUuid, selected.ClusterUuid)))
            return false;
        var connection = Connections.FirstOrDefault(c => c.Profile.Id == selected.ServerId);
        return connection is { IsConnected: false, Error: not null and not "" }
            || connection?.Snapshot?.Clusters.Any(c => UuidEquals(c.Uuid, selected.ClusterUuid) && !string.IsNullOrWhiteSpace(c.DetailsMessage)) == true;
    }

    private static bool UuidEquals(string? left, string? right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    private bool MatchesCluster(Guid serverId, string? clusterUuid) => SelectedCluster is not { IsAll: false } cluster
        || cluster.ServerId == serverId && UuidEquals(cluster.ClusterUuid, clusterUuid);
    private bool MatchesInfobase(ServerSessionRow row) => InfobaseFilter is not { IsAll: false } infobase
        || infobase.ServerId == row.Profile.Id && UuidEquals(infobase.ClusterUuid, row.ClusterUuid)
            && UuidEquals(infobase.InfobaseUuid, row.Session.InfobaseUuid);

    private void RebuildSessions()
    {
        var rows = AllRows().Where(r => (ConnectionFilter?.Id is not { } id || r.Profile.Id == id)
            && MatchesCluster(r.Profile.Id, r.ClusterUuid) && MatchesInfobase(r)
            && (string.IsNullOrWhiteSpace(SearchText) || $"{r.ServerText} {r.ServerDetails} {r.ClusterName} {r.InfobaseName} {r.UserName} {r.Host} {r.Application} {r.SessionId}"
                .Contains(SearchText, StringComparison.CurrentCultureIgnoreCase)))
            .OrderBy(r => r.ServerText).ThenBy(r => r.InfobaseName).ThenBy(r => r.UserName).ToArray();
        Sessions.Clear();
        foreach (var row in rows) Sessions.Add(row);
        var errors = Connections.Count(c => c.IsMonitoring && !string.IsNullOrWhiteSpace(c.Error));
        Status = Connections.Count == 0 ? "Добавьте сервер для просмотра сеансов"
            : $"Серверов: {Connections.Count} · подключено: {Connections.Count(c => c.IsConnected)} · показывается сеансов: {Sessions.Count}"
                + (errors > 0 ? $" · с ошибками: {errors}" : string.Empty);
        OnPropertyChanged(nameof(SelectedConnectionError));
        OnPropertyChanged(nameof(HasConnectionError));
        OnPropertyChanged(nameof(ConnectionErrorSeverity));
        OnPropertyChanged(nameof(EmptyStateText));
    }

    public void ShowError(string message) { MessageSeverity = InfoBarSeverity.Error; Message = message; }
    partial void OnConnectionFilterChanged(ServerConnectionFilter? value)
    {
        if (synchronizingSelection) return;
        synchronizingSelection = true;
        try
        {
            singleClusterSelectionServerId = value?.Id;
            SelectedConnection = Connections.FirstOrDefault(c => c.Profile.Id == value?.Id);
            RebuildClusterFilters();
            RebuildInfobaseFilters();
        }
        finally { synchronizingSelection = false; }
        RebuildSessions();
    }
    partial void OnSelectedClusterChanged(ServerClusterFilter? value)
    {
        if (synchronizingSelection) return;
        synchronizingSelection = true;
        try
        {
            singleClusterSelectionServerId = null;
            if (value is { IsAll: false })
            {
                ConnectionFilter = ConnectionFilters.FirstOrDefault(f => f.Id == value.ServerId) ?? ConnectionFilters[0];
                SelectedConnection = Connections.FirstOrDefault(c => c.Profile.Id == ConnectionFilter.Id);
                RebuildClusterFilters();
            }
            RebuildInfobaseFilters();
        }
        finally { synchronizingSelection = false; }
        RebuildSessions();
    }
    partial void OnInfobaseFilterChanged(ServerInfobaseFilter? value)
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
            singleClusterSelectionServerId = value?.Profile.Id;
            ConnectionFilter = ConnectionFilters.FirstOrDefault(f => f.Id == value?.Profile.Id) ?? ConnectionFilters[0];
            RebuildClusterFilters();
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
        ConnectScopeCommand.NotifyCanExecuteChanged();
        RefreshAllCommand.NotifyCanExecuteChanged();
        RefreshScopeCommand.NotifyCanExecuteChanged();
    }
}
