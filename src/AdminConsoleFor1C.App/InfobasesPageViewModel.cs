using System.Collections.ObjectModel;
using AdminConsoleFor1C.Application.Services;
using AdminConsoleFor1C.Core.Administration;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;

namespace AdminConsoleFor1C.App;

public sealed partial class InfobasesPageViewModel : ObservableObject
{
    private readonly OneCInfobaseInventoryReader reader;
    private readonly Dictionary<Guid, OneCInfobaseServerInventory> snapshots = [];
    private bool synchronizingSelection;
    private Guid? singleClusterSelectionServerId;
    private Task? activationTask;
    private Task? readTask;
    private bool activationRequested;
    private bool isActivating;

    public InfobasesPageViewModel(IOneCInfobaseClient client, SessionsPageViewModel connections)
    {
        reader = new(client);
        ConnectionOwner = connections;
        // Both view models are cached for the application lifetime by the composition root.
        ConnectionOwner.PropertyChanged += Owner_Changed;
        ConnectionFilter = ConnectionFilters[0];
        SelectedCluster = ServerClusterFilter.All;
        InfobaseFilter = ServerInfobaseFilter.All;
    }

    // Connection profiles and credentials are shared; each page tracks its own loaded data.
    public SessionsPageViewModel ConnectionOwner { get; }
    public ObservableCollection<ServerConnectionItemViewModel> Connections { get; } = [];
    public ObservableCollection<ServerConnectionFilter> ConnectionFilters { get; } = [new(null, "Все серверы", ScopeDescription: "Общий список баз")];
    public ObservableCollection<ServerClusterFilter> Clusters { get; } = [ServerClusterFilter.All];
    public ObservableCollection<ServerInfobaseFilter> InfobaseFilters { get; } = [ServerInfobaseFilter.All];
    public ObservableCollection<ServerInfobaseRow> Infobases { get; } = [];
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanEdit)), NotifyPropertyChangedFor(nameof(CanCreate)), NotifyPropertyChangedFor(nameof(CanManageSelected)), NotifyPropertyChangedFor(nameof(CanRemoveSelected)), NotifyPropertyChangedFor(nameof(IsUpdating))]
    public partial bool IsBusy { get; set; }
    [ObservableProperty] public partial ServerConnectionFilter? ConnectionFilter { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanCreate)), NotifyPropertyChangedFor(nameof(CanManageSelected)), NotifyPropertyChangedFor(nameof(CanRemoveSelected))]
    public partial ServerConnectionItemViewModel? SelectedServer { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanCreate))] public partial ServerClusterFilter? SelectedCluster { get; set; }
    [ObservableProperty] public partial ServerInfobaseFilter? InfobaseFilter { get; set; }
    [ObservableProperty] public partial string SearchText { get; set; } = "";
    [ObservableProperty] public partial bool IsAutoRefreshEnabled { get; set; }
    [ObservableProperty] public partial string Status { get; set; } = "Загрузка информационных баз…";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasMessage))] public partial string? Message { get; set; }
    [ObservableProperty] public partial InfoBarSeverity MessageSeverity { get; set; } = InfoBarSeverity.Informational;
    public bool CanEdit => !IsBusy && ConnectionOwner.CanEdit;
    public bool IsUpdating => IsBusy || isActivating || ConnectionOwner.IsBusy;
    public bool CanManageSelected => CanEdit && SelectedServer is not null;
    public bool CanRemoveSelected => CanManageSelected && SelectedServer is { IsDiscovered: false };
    public bool HasMessage => !string.IsNullOrWhiteSpace(Message);
    private IEnumerable<ServerConnectionItemViewModel> Scope => Connections.Where(c => ConnectionFilter?.Id is not { } id || c.Profile.Id == id);
    public OneCClusterInfo? CreationCluster => SelectedServer is { IsConnected: true } server && SelectedCluster is { IsAll: false } cluster
        && cluster.ServerId == server.Profile.Id && snapshots.TryGetValue(server.Profile.Id, out var snapshot) && snapshot.Profile == server.Profile
            ? snapshot.Clusters.FirstOrDefault(c => SameId(c.Uuid, cluster.ClusterUuid)) : null;
    public bool CanCreate => CanManageSelected && CreationCluster is { DetailsMessage: null or "" }
        && SelectedServer?.LocalService is null or { State: "Running" };
    public string ConnectionErrors => string.Join("\n", Scope.Where(c => !string.IsNullOrWhiteSpace(c.Error)).Select(c => $"{c.Name}: {c.Error}"));
    public bool HasConnectionError => !string.IsNullOrWhiteSpace(ConnectionErrors);
    public InfoBarSeverity ConnectionErrorSeverity => Scope.Any(c => c.IsConnected) ? InfoBarSeverity.Warning : InfoBarSeverity.Error;
    public string CreationHint => CanCreate ? "Новая база создаётся пустой, без конфигурации."
        : "Для создания базы выберите конкретный сервер и кластер.";
    public string ListSummary => $"Баз: {Infobases.Count} из {ScopedRows().Count()} · {CreationHint}";
    public string EmptyStateText => Connections.Count == 0 ? "Добавьте сервер, чтобы просматривать базы."
        : !Scope.Any(c => c.IsMonitoring) ? "Нажмите «Обновить», чтобы получить базы выбранного сервера или всех серверов."
        : HasConnectionError ? Scope.Any(c => c.IsConnected)
            ? "Список баз получен не полностью. Проверьте состояние серверов и сообщения подключения."
            : "Не удалось получить базы. Проверьте состояние серверов и сообщения подключения."
        : !string.IsNullOrWhiteSpace(SearchText) || InfobaseFilter is { IsAll: false } ? "По выбранным фильтрам базы не найдены."
        : "В выбранных кластерах пока нет баз.";

    public Task ActivateAsync()
    {
        activationRequested = true;
        if (activationTask is { IsCompleted: false }) return activationTask;
        return activationTask = ActivateCoreAsync();
    }

    private async Task ActivateCoreAsync()
    {
        isActivating = true;
        OnPropertyChanged(nameof(IsUpdating));
        try
        {
            do
            {
                activationRequested = false;
                if (readTask is { IsCompleted: false } pendingRead) await pendingRead;
                await ConnectionOwner.WaitForIdleAsync();
                await LoadAsync();
                if (!ConnectionOwner.IsLoaded) return;
                await RefreshAsync();
            } while (activationRequested);
        }
        finally { isActivating = false; OnPropertyChanged(nameof(IsUpdating)); }
    }

    public async Task LoadAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            await ConnectionOwner.LoadAsync();
            SynchronizeConnections();
            if (!ConnectionOwner.IsLoaded) ShowMessage(ConnectionOwner.Message ?? "Не удалось загрузить подключения.", InfoBarSeverity.Error);
        }
        finally { IsBusy = false; }
    }

    public async Task RefreshServersAsync()
    {
        await ConnectionOwner.RefreshLocalConnectionsAsync();
        SynchronizeConnections();
    }

    public void SelectServer(Guid id) => ConnectionFilter = ConnectionFilters.FirstOrDefault(f => f.Id == id) ?? ConnectionFilters[0];

    private void SynchronizeConnections()
    {
        foreach (var old in Connections.Where(c => !ConnectionOwner.Connections.Any(saved => saved.Profile == c.Profile)).ToArray())
        {
            Connections.Remove(old);
            snapshots.Remove(old.Profile.Id);
        }
        foreach (var source in ConnectionOwner.Connections)
        {
            var item = Connections.FirstOrDefault(c => c.Profile == source.Profile);
            if (item is null || item.IsDiscovered != source.IsDiscovered)
            {
                var previous = item;
                if (previous is not null) Connections.Remove(previous);
                item = new(source.Profile)
                {
                    IsDiscovered = source.IsDiscovered,
                    IsMonitoring = previous?.IsMonitoring ?? false,
                    IsConnected = previous?.IsConnected ?? false,
                    Status = previous?.Status ?? "Не подключён",
                    Error = previous?.Error
                };
                Connections.Add(item);
            }
            item.LocalService = source.LocalService;
        }
        RebuildServerFilters();
    }

    private void RebuildServerFilters()
    {
        var selectedId = ConnectionFilter?.Id;
        synchronizingSelection = true;
        try
        {
            ConnectionFilters.Clear();
            ConnectionFilters.Add(new(null, "Все серверы", ScopeDescription: "Общий список баз"));
            foreach (var connection in ServerConnectionPresentation.OrderForDisplay(Connections,
                c => c.Profile, c => c.LocalService is not null || c.IsDiscovered, Environment.MachineName))
                ConnectionFilters.Add(new(connection.Profile.Id, connection.Name, connection));
            ConnectionFilter = ConnectionFilters.FirstOrDefault(f => f.Id == selectedId) ?? ConnectionFilters[0];
            SelectedServer = Connections.FirstOrDefault(c => c.Profile.Id == ConnectionFilter.Id);
            RebuildClusterFilters();
        }
        finally { synchronizingSelection = false; }
        RebuildInfobases();
    }

    private void RebuildClusterFilters()
    {
        var selected = SelectedCluster;
        Clusters.Clear();
        Clusters.Add(ServerClusterFilter.All);
        foreach (var (profile, cluster) in ScopedClusters().OrderBy(x => x.Profile.Name).ThenBy(x => x.Cluster.NameText))
            Clusters.Add(new(profile.Id, cluster.Uuid, cluster.NameText, ServerDataScopePresentation.ClusterDetails(profile, cluster), cluster.AddressText));
        var matching = Clusters.FirstOrDefault(c => c.ServerId == selected?.ServerId && SameId(c.ClusterUuid, selected?.ClusterUuid));
        if (matching is null && selected is { IsAll: false } && IsUnavailable(selected.ServerId, selected.ClusterUuid, clusterList: true))
        {
            Clusters.Add(selected);
            matching = selected;
        }
        SelectedCluster = matching ?? Clusters[0];
        if (singleClusterSelectionServerId is { } serverId && SelectedServer is { IsConnected: true } server
            && server.Profile.Id == serverId && snapshots.TryGetValue(serverId, out var snapshot) && snapshot.Profile == server.Profile)
        {
            if (snapshot.Clusters.Count == 1)
                SelectedCluster = Clusters.FirstOrDefault(c => c.ServerId == serverId && SameId(c.ClusterUuid, snapshot.Clusters[0].Uuid)) ?? Clusters[0];
            singleClusterSelectionServerId = null;
        }
        RebuildInfobaseFilters();
    }

    private void RebuildInfobaseFilters()
    {
        var selected = InfobaseFilter;
        InfobaseFilters.Clear();
        InfobaseFilters.Add(ServerInfobaseFilter.All);
        foreach (var row in ScopedRows().OrderBy(r => r.NameText).ThenBy(r => r.ServerText))
            InfobaseFilters.Add(new(row.Profile.Id, row.Cluster.Uuid, row.Infobase.Uuid, row.NameText,
                ServerDataScopePresentation.InfobaseDetails(row.Profile, row.Cluster), $"{row.Profile.AgentAddress} · {row.Cluster.AddressText}"));
        var matching = InfobaseFilters.FirstOrDefault(f => f.ServerId == selected?.ServerId && SameId(f.ClusterUuid, selected?.ClusterUuid)
            && SameId(f.InfobaseUuid, selected?.InfobaseUuid));
        if (matching is null && selected is { IsAll: false }
            && (SelectedCluster is null or { IsAll: true } || SelectedCluster.ServerId == selected.ServerId && SameId(SelectedCluster.ClusterUuid, selected.ClusterUuid))
            && IsUnavailable(selected.ServerId, selected.ClusterUuid, clusterList: false))
        {
            InfobaseFilters.Add(selected);
            matching = selected;
        }
        InfobaseFilter = matching ?? InfobaseFilters[0];
    }

    private bool IsUnavailable(Guid? serverId, string? clusterUuid, bool clusterList)
    {
        var connection = Scope.FirstOrDefault(c => c.Profile.Id == serverId);
        if (connection is null || string.IsNullOrWhiteSpace(connection.Error)) return false;
        if (!snapshots.TryGetValue(connection.Profile.Id, out var snapshot) || snapshot.Profile != connection.Profile) return true;
        return !clusterList && snapshot.Clusters.Any(c => SameId(c.Uuid, clusterUuid) && !string.IsNullOrWhiteSpace(c.DetailsMessage));
    }

    private IEnumerable<(OneCServerConnectionProfile Profile, OneCClusterInfo Cluster)> ScopedClusters()
        => Scope.Where(c => c.IsConnected && snapshots.TryGetValue(c.Profile.Id, out var snapshot) && snapshot.Profile == c.Profile)
            .SelectMany(c => snapshots[c.Profile.Id].Clusters.Select(cluster => (c.Profile, cluster)));
    private IEnumerable<ServerInfobaseRow> ScopedRows()
        => ScopedClusters().Where(x => SelectedCluster is null or { IsAll: true }
                || SelectedCluster.ServerId == x.Profile.Id && SameId(SelectedCluster.ClusterUuid, x.Cluster.Uuid))
            .SelectMany(x => x.Cluster.Infobases.Select(infobase => new ServerInfobaseRow(x.Profile, x.Cluster, infobase)));

    [RelayCommand(CanExecute = nameof(CanManageSelected))]
    private Task ConnectSelectedAsync() => ReadScopeAsync(SelectedServer?.Profile.Id);
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private Task ConnectAllAsync() => ReadScopeAsync(null);
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private Task ConnectScopeAsync() => RefreshAsync();
    [RelayCommand(CanExecute = nameof(CanEdit))]
    public Task RefreshAsync() => ReadScopeAsync(SelectedServer?.Profile.Id ?? SelectedCluster?.ServerId ?? InfobaseFilter?.ServerId);
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private Task RefreshAllAsync() => ReadScopeAsync(null);
    [RelayCommand(CanExecute = nameof(CanManageSelected))]
    private void DisconnectSelected()
    {
        if (SelectedServer is not { } connection) return;
        connection.IsMonitoring = false;
        connection.IsConnected = false;
        connection.Error = null;
        connection.Status = "Не подключён";
        snapshots.Remove(connection.Profile.Id);
        RebuildServerFilters();
    }

    private Task ReadScopeAsync(Guid? serverId)
    {
        if (readTask is { IsCompleted: false }) return readTask;
        return readTask = ReadScopeCoreAsync(serverId);
    }

    private async Task ReadScopeCoreAsync(Guid? serverId)
    {
        // Shared catalog operations are serialized below; do not discard a queued activation while the owner is busy.
        if (IsBusy || !ConnectionOwner.IsLoaded) return;
        IsBusy = true;
        Message = null;
        try
        {
            await RefreshServersAsync();
            var selected = Connections.Where(c => serverId is null || c.Profile.Id == serverId).ToArray();
            foreach (var connection in selected)
            {
                connection.IsMonitoring = true;
                connection.Status = "Подключение…";
            }
            var available = selected.Where(c => c.LocalService is null or { State: "Running" }).ToArray();
            foreach (var stopped in selected.Except(available))
                SetFailure(stopped, "Локальная служба агента не работает. Запустите её в разделе «Службы», затем обновите список.");
            var results = await reader.ReadAsync(available.Select(c => c.Profile).ToArray(), ConnectionOwner.GetPassword);
            // Another page may edit profiles or refresh discovery while the server read is pending.
            SynchronizeConnections();
            foreach (var result in results)
            {
                var connection = Connections.FirstOrDefault(c => c.Profile == result.Profile);
                if (connection is null) continue;
                if (result.Error is { } error) { SetFailure(connection, error); continue; }
                snapshots[result.Profile.Id] = result;
                connection.IsConnected = true;
                var errors = result.Clusters.Where(c => !string.IsNullOrWhiteSpace(c.DetailsMessage)).Select(c => $"{c.NameText}: {c.DetailsMessage}").ToArray();
                connection.Error = errors.Length == 0 ? null : string.Join("\n", errors);
                connection.Status = errors.Length > 0 ? "Частичные данные · проверьте сообщения подключения"
                    : $"Баз: {result.Clusters.Sum(c => c.Infobases.Count)} · кластеров: {result.Clusters.Count} · {result.UpdatedAt:HH:mm:ss}";
            }
            RebuildServerFilters();
        }
        catch (Exception ex) { ShowMessage(ex.Message, InfoBarSeverity.Error); }
        finally { IsBusy = false; }
    }

    private void SetFailure(ServerConnectionItemViewModel connection, string error)
    {
        snapshots.Remove(connection.Profile.Id);
        connection.IsConnected = false;
        connection.Error = error;
        connection.Status = connection.LocalService is { State: not "Running" } ? "Локальная служба не работает" : "Ошибка подключения";
    }

    public async Task RemoveSelectedServerAsync()
    {
        if (!CanRemoveSelected || SelectedServer is not { } selected) return;
        ConnectionOwner.SelectedConnection = ConnectionOwner.Connections.FirstOrDefault(c => c.Profile.Id == selected.Profile.Id);
        await ConnectionOwner.RemoveSelectedAsync();
        SynchronizeConnections();
    }

    private void RebuildInfobases()
    {
        Infobases.Clear();
        var search = SearchText.Trim();
        foreach (var row in ScopedRows().Where(r => InfobaseFilter is null or { IsAll: true }
                || InfobaseFilter.ServerId == r.Profile.Id && SameId(InfobaseFilter.ClusterUuid, r.Cluster.Uuid) && SameId(InfobaseFilter.InfobaseUuid, r.Infobase.Uuid))
            .Where(r => search.Length == 0 || $"{r.NameText} {r.DescriptionText} {r.ServerText} {r.Profile.AgentAddress} {r.PlatformText} {r.ClusterText}"
                .Contains(search, StringComparison.CurrentCultureIgnoreCase))
            .OrderBy(r => r.NameText).ThenBy(r => r.ServerText).ThenBy(r => r.ClusterText)) Infobases.Add(row);
        Status = $"Серверов: {Connections.Count} · подключено: {Connections.Count(c => c.IsConnected)} · показывается баз: {Infobases.Count}"
            + (Connections.Any(c => c.Error is { Length: > 0 }) ? $" · с ошибками: {Connections.Count(c => c.Error is { Length: > 0 })}" : "");
        foreach (var property in new[] { nameof(ListSummary), nameof(EmptyStateText), nameof(ConnectionErrors), nameof(HasConnectionError), nameof(ConnectionErrorSeverity), nameof(CanCreate), nameof(CreationHint) })
            OnPropertyChanged(property);
    }

    public void ShowMessage(string text, InfoBarSeverity severity) { MessageSeverity = severity; Message = text; }
    private void Owner_Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(SessionsPageViewModel.IsBusy) or nameof(SessionsPageViewModel.IsLoaded))) return;
        foreach (var property in new[] { nameof(CanEdit), nameof(CanManageSelected), nameof(CanRemoveSelected), nameof(CanCreate), nameof(CreationHint), nameof(ListSummary), nameof(IsUpdating) })
            OnPropertyChanged(property);
        NotifyCommands();
    }
    partial void OnConnectionFilterChanged(ServerConnectionFilter? value)
    {
        if (synchronizingSelection) return;
        synchronizingSelection = true;
        try
        {
            singleClusterSelectionServerId = value?.Id;
            SelectedServer = Connections.FirstOrDefault(c => c.Profile.Id == value?.Id);
            RebuildClusterFilters();
        }
        finally { synchronizingSelection = false; }
        RebuildInfobases();
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
                SelectedServer = Connections.FirstOrDefault(c => c.Profile.Id == ConnectionFilter.Id);
                RebuildClusterFilters();
            }
            else RebuildInfobaseFilters();
        }
        finally { synchronizingSelection = false; }
        RebuildInfobases();
    }
    partial void OnInfobaseFilterChanged(ServerInfobaseFilter? value) { if (!synchronizingSelection) RebuildInfobases(); }
    partial void OnSearchTextChanged(string value) => RebuildInfobases();
    partial void OnSelectedServerChanged(ServerConnectionItemViewModel? value) => NotifyCommands();
    partial void OnIsBusyChanged(bool value) { NotifyCommands(); OnPropertyChanged(nameof(CreationHint)); OnPropertyChanged(nameof(ListSummary)); }
    private static bool SameId(string? left, string? right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    private void NotifyCommands()
    {
        RefreshCommand.NotifyCanExecuteChanged();
        RefreshAllCommand.NotifyCanExecuteChanged();
        ConnectScopeCommand.NotifyCanExecuteChanged();
        ConnectAllCommand.NotifyCanExecuteChanged();
        ConnectSelectedCommand.NotifyCanExecuteChanged();
        DisconnectSelectedCommand.NotifyCanExecuteChanged();
    }
}

public sealed record ServerInfobaseRow(OneCServerConnectionProfile Profile, OneCClusterInfo Cluster, OneCInfobaseSummaryInfo Infobase)
{
    public string NameText => Infobase.NameText;
    public string DescriptionText => Infobase.DescriptionText;
    public string ServerText => ServerConnectionPresentation.DisplayName(Profile, Environment.MachineName);
    public string ServerDetails => $"Агент: {Profile.AgentAddress} · Платформа: {Profile.PlatformVersion}";
    public string PlatformText => Profile.PlatformVersion;
    public string ClusterDisplayText => ServerDataScopePresentation.ClusterCaption(Cluster.NameText, Cluster.AddressText);
    public string ClusterText => $"{Cluster.NameText} · {Cluster.AddressText}";
    public override string ToString() => $"{NameText} · {ServerText} · {ClusterText}";
}
