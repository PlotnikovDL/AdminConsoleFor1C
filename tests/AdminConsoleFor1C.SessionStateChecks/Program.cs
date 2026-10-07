using AdminConsoleFor1C.App;
using AdminConsoleFor1C.Application.Services;
using AdminConsoleFor1C.Core.Administration;
using AdminConsoleFor1C.Core.Services;

var passed = 0;
var remote = Profile("server.example");
await ProfileLoadAndUnifiedRefreshAsync();
await FailureRetryAsync();
await StoppedServiceAndRecoveryAsync();
await RemovedSelectedServiceAsync();
await PartialEmptySnapshotAsync();
await DuplicateBaseOwnersAsync();
await LiteralAllBaseNameAsync();
await FilterSelectionRefreshAsync();
await UnregisteredSessionBasesAsync();
await FirstActivationReadsAllAsync();
await ScopedReturnRemembersFiltersAsync();
await FailedScopeRecoveryAsync();
await PartialPendingBaseRecoveryAsync();
await CoalescedActivationAsync();
await SharedPendingLoadAsync();
await ActivationWaitsForSaveAsync();
await ClusterOwnerAndReverseSelectionAsync();
await DelayedSingleClusterSelectionAsync();
await MultiClusterAndSingletonSelectionAsync();
await DirectConnectionSelectionAsync();
Console.WriteLine($"PASS: {passed} state scenarios; no real service or session operations.");

async Task ProfileLoadAndUnifiedRefreshAsync()
{
    var (vm, client, inventory) = Create([remote]);
    await vm.LoadAsync();
    Require(client.ReadCalls == 0 && vm.Connections.All(c => !c.IsMonitoring && !c.IsConnected), "Load must not connect");
    Require(vm.EmptyStateText.Contains("Обновить"), "Profile-only load must offer the single refresh action");
    await vm.RefreshAllCommand.ExecuteAsync(null);
    vm.SelectedConnection = vm.Connections[0];
    await vm.RefreshScopeCommand.ExecuteAsync(null);
    Require(client.ReadCalls == 2 && vm.Connections[0].IsConnected, "Manual refresh must read even a previously unread profile");
    Require(inventory.ReadCalls == 3, "Each refresh must read the service catalog");
    Pass("Load remains profile-only; unified manual refresh performs the first read");
}

async Task FailureRetryAsync()
{
    var (vm, client, _) = Create([remote]);
    await vm.LoadAsync();
    vm.SelectedConnection = vm.Connections[0];
    var item = vm.SelectedConnection;
    await vm.ConnectSelectedCommand.ExecuteAsync(null);
    var staleRows = vm.Sessions.ToArray();
    Require(item.IsMonitoring && item.IsConnected && item.Snapshot is not null && staleRows.Length == 1, "Explicit connect must start monitoring and expose a successful snapshot");
    client.NextRead = () => throw new InvalidOperationException("RAS unavailable");
    await vm.RefreshAllCommand.ExecuteAsync(null);
    Require(item.IsMonitoring && !item.IsConnected && item.Snapshot is null && vm.Sessions.Count == 0, "Failed read must remove stale sessions while keeping retries");
    Require(vm.EmptyStateText.Contains("Не удалось") && !vm.EmptyStateText.Contains("Сеансы не найдены"), "Error must not masquerade as an empty session list");
    await vm.TerminateAsync(staleRows, "ignored by fake client");
    Require(client.TerminateCalls == 0, "Rows from a failed snapshot must not be submitted for termination");
    client.NextRead = () => FullSnapshot();
    await vm.RefreshAllCommand.ExecuteAsync(null);
    Require(client.ReadCalls == 3 && item.IsConnected && item.IsMonitoring && vm.Sessions.Count == 1, "Next automatic refresh must retry a failed monitored server");
    Pass("Failure removes snapshot; retry recovers; stale rows cannot terminate");

}

async Task StoppedServiceAndRecoveryAsync()
{
    var service = Service("Running");
    var (vm, client, inventory) = Create([], [service]);
    await vm.LoadAsync();
    vm.SelectedConnection = vm.Connections.Single();
    var item = vm.SelectedConnection;
    await vm.ConnectSelectedCommand.ExecuteAsync(null);
    Require(client.ReadCalls == 1 && item.IsConnected, "Running local service can be read");
    inventory.Services = [service with { State = "Stopped" }];
    await vm.RefreshAllCommand.ExecuteAsync(null);
    Require(inventory.ReadCalls == 3 && client.ReadCalls == 1, "Refresh must detect newly stopped local service without a network read");
    Require(item.IsMonitoring && !item.IsConnected && item.Snapshot is null && item.Error!.Contains("«Службы»"), "Stopped local service must clear stale data and explain how to start it");
    inventory.Services = [service];
    await vm.RefreshAllCommand.ExecuteAsync(null);
    Require(client.ReadCalls == 2 && item.IsConnected && item.IsMonitoring, "Monitoring must resume after the service starts");
    Pass("Catalog refresh detects stopped service without client read and recovers after start");
}

async Task RemovedSelectedServiceAsync()
{
    var (vm, client, inventory) = Create([remote], [Service("Running")]);
    await vm.LoadAsync();
    await vm.ConnectAllCommand.ExecuteAsync(null);
    var discovered = vm.Connections.Single(c => c.IsDiscovered);
    vm.SelectedConnection = discovered;
    var readsBefore = client.ReadCalls;
    inventory.Services = [];
    await vm.RefreshScopeCommand.ExecuteAsync(null);
    Require(vm.Connections.All(c => c.Profile.Id != discovered.Profile.Id), "Removed discovered service must leave the catalog");
    Require(vm.SelectedConnection is null && vm.ConnectionFilter?.Id is null, "Removed selected service must reset selection to All servers");
    Require(client.ReadCalls == readsBefore, "Refresh of a removed selection must not redirect to another server");
    Require(vm.Sessions.Count == 1 && vm.Sessions[0].Profile.Id == remote.Id, "Removed server rows must disappear while other valid snapshots remain");
    Pass("Removed selected discovered service resets selection without reading another server");
}

async Task PartialEmptySnapshotAsync()
{
    var (vm, client, _) = Create([remote]);
    client.NextRead = () => new([new OneCClusterInfo { Uuid = "cluster-1", Name = "Cluster", DetailsMessage = "Session list unavailable" }], DateTimeOffset.Now);
    await vm.LoadAsync();
    vm.SelectedConnection = vm.Connections.Single();
    await vm.ConnectSelectedCommand.ExecuteAsync(null);
    var item = vm.SelectedConnection;
    Require(item.IsConnected && item.IsMonitoring && item.Snapshot is not null && !string.IsNullOrEmpty(item.Error), "Partial response is a connected snapshot with an explicit error");
    Require(vm.Sessions.Count == 0 && vm.EmptyStateText.Contains("неполные") && !vm.EmptyStateText.Contains("Сеансы не найдены"), "Partial zero-session reply must not claim there are no sessions");
    Require(vm.Status.Contains("с ошибками: 1") && item.Status.Contains("Частичные данные"), "Partial response must remain visible in server and page status");
    vm.ConnectionFilter = vm.ConnectionFilters[0];
    Require(vm.EmptyStateText.Contains("неполные"), "All-server scope must also explain the partial reply");
    Pass("Partial snapshot with zero sessions keeps honest connection and empty-state status");
}

async Task DuplicateBaseOwnersAsync()
{
    var second = Profile("second.example");
    var (vm, client, _) = Create([remote, second]);
    client.ReadProfile = _ => new([
        Cluster("shared-cluster", "Same cluster", [Base("same-base", "Accounting")], [Session("first-session", "same-base")]),
        Cluster("other-cluster", "Same cluster", [Base("same-base", "Accounting")], [Session("second-session", "same-base")])
    ], DateTimeOffset.Now);
    await vm.LoadAsync();
    await vm.ConnectAllCommand.ExecuteAsync(null);
    Require(vm.Clusters.Count == 5 && vm.InfobaseFilters.Count == 5 && vm.Sessions.Count == 4, "Same names and UUIDs in different owners must remain four separate choices");
    vm.InfobaseFilter = vm.InfobaseFilters.Single(b => b.ServerId == second.Id && b.ClusterUuid == "other-cluster");
    Require(vm.Sessions.Count == 1 && vm.Sessions[0].Profile.Id == second.Id && vm.Sessions[0].ClusterUuid == "other-cluster", "Base filter must match server + cluster + base, not its name or UUID alone");
    vm.SelectedCluster = vm.Clusters.Single(c => c.ServerId == remote.Id && c.ClusterUuid == "other-cluster");
    Require(vm.InfobaseFilter!.IsAll && vm.Sessions.Count == 1 && vm.Sessions[0].Profile.Id == remote.Id,
        "Switching to another cluster owner must reset an unavailable base choice");
    Require(vm.InfobaseFilters.Count == 2, "Base options must follow the selected composite cluster");
    Pass("Same base/cluster names and UUIDs stay distinct across servers and clusters");
}

async Task LiteralAllBaseNameAsync()
{
    var (vm, client, _) = Create([remote]);
    client.NextRead = () => new([
        Cluster("cluster", "Cluster", [Base("literal-all", "Все базы"), Base("other", "Other")],
            [Session("literal-session", "literal-all"), Session("other-session", "other")])
    ], DateTimeOffset.Now);
    await vm.LoadAsync();
    await vm.ConnectAllCommand.ExecuteAsync(null);
    vm.InfobaseFilter = vm.InfobaseFilters.Single(b => !b.IsAll && b.Name == "Все базы");
    Require(vm.Sessions.Count == 1 && vm.Sessions[0].Session.InfobaseUuid == "literal-all", "A real base named All bases must remain selectable");
    vm.InfobaseFilter = ServerInfobaseFilter.All;
    Require(vm.Sessions.Count == 2, "The explicit All sentinel must include every base");
    Pass("Real base named 'Все базы' is distinct from the All sentinel");
}

async Task FilterSelectionRefreshAsync()
{
    var (vm, client, _) = Create([remote]);
    client.NextRead = () => new([
        Cluster("CLUSTER-1", "Old name", [Base("BASE-1", "Old base")], [Session("session-one", "BASE-1")]),
        Cluster("cluster-2", "Second", [Base("base-2", "Other")], [Session("session-two", "base-2")])
    ], DateTimeOffset.Now);
    await vm.LoadAsync();
    await vm.ConnectAllCommand.ExecuteAsync(null);
    vm.SelectedCluster = vm.Clusters.Single(c => c.ClusterUuid == "CLUSTER-1");
    vm.InfobaseFilter = vm.InfobaseFilters.Single(b => b.InfobaseUuid == "BASE-1");
    client.NextRead = () => new([
        Cluster("cluster-1", "Renamed", [Base("base-1", "Renamed base")], [Session("session-one", "base-1")]),
        Cluster("cluster-2", "Second", [Base("base-2", "Other")], [Session("session-two", "base-2")])
    ], DateTimeOffset.Now);
    await vm.RefreshAllCommand.ExecuteAsync(null);
    Require(vm.SelectedCluster!.ServerId == remote.Id && vm.SelectedCluster.Name == "Renamed"
        && vm.InfobaseFilter!.InfobaseUuid == "base-1" && vm.InfobaseFilter.Name == "Renamed base" && vm.Sessions.Count == 1,
        "Refresh must preserve composite selection across name and UUID casing changes");
    client.NextRead = () => new([Cluster("cluster-2", "Second", [Base("base-2", "Other")], [Session("session-two", "base-2")])], DateTimeOffset.Now);
    await vm.RefreshAllCommand.ExecuteAsync(null);
    Require(vm.SelectedCluster!.IsAll && vm.InfobaseFilter!.IsAll && vm.Sessions.Count == 1,
        "Disappearing cluster and base choices must reset to explicit All sentinels");
    Pass("Composite filter selection survives rename/refresh and resets when stale");
}

async Task UnregisteredSessionBasesAsync()
{
    var (vm, client, _) = Create([remote]);
    client.NextRead = () => new([
        Cluster("cluster", "Cluster", [Base("registered", "Registered")],
            [Session("registered-session", "registered"), Session("orphan-one", "orphan"), Session("orphan-two", "ORPHAN"), Session("unknown", null)])
    ], DateTimeOffset.Now);
    await vm.LoadAsync();
    await vm.ConnectAllCommand.ExecuteAsync(null);
    Require(vm.InfobaseFilters.Count == 4, "Registration, orphan base and unknown base must each have one choice plus All");
    vm.InfobaseFilter = vm.InfobaseFilters.Single(b => b.InfobaseUuid == "orphan");
    Require(vm.Sessions.Count == 2, "Orphan base choice must match its sessions even without a registration");
    vm.InfobaseFilter = vm.InfobaseFilters.Single(b => !b.IsAll && b.InfobaseUuid is null);
    Require(vm.Sessions.Count == 1 && vm.Sessions[0].Session.Uuid == "unknown", "Unknown base choice must remain distinct from All");
    Pass("Unregistered and unknown session bases remain selectable without name grouping");
}

async Task FirstActivationReadsAllAsync()
{
    var second = Profile("second.example");
    var (vm, client, _) = Create([remote, second], [Service("Running")]);
    await vm.ActivateAsync();
    Require(vm.IsLoaded && client.ReadCalls == 3 && vm.Sessions.Count == 3, "First entry must load profiles and read all saved/discovered servers");
    Require(vm.ConnectionFilter?.Id is null && vm.SelectedCluster!.IsAll && vm.InfobaseFilter!.IsAll && vm.SearchText.Length == 0,
        "New ViewModel must begin in the All scope");
    var reads = client.ReadCalls;
    await vm.LoadAsync();
    Require(client.ReadCalls == reads && !vm.IsBusy, "Shared-owner Load must not trigger session reads");
    Pass("First activation automatically reads All; shared-owner Load stays profile-only");
}

async Task ScopedReturnRemembersFiltersAsync()
{
    var second = Profile("second.example");
    var (vm, client, _) = Create([remote, second]);
    client.ReadProfile = _ => ScopeSnapshot();
    await vm.ActivateAsync();
    vm.ConnectionFilter = vm.ConnectionFilters.Single(c => c.Id == second.Id);
    vm.SelectedCluster = vm.Clusters.Single(c => c.ClusterUuid == "cluster-2");
    vm.InfobaseFilter = vm.InfobaseFilters.Single(b => b.InfobaseUuid == "base-2");
    vm.SearchText = "Test user";
    vm.IsAutoRefreshEnabled = true;
    var reads = client.ReadCalls;
    await vm.ActivateAsync();
    Require(client.ReadCalls == reads + 1 && client.ReadProfiles[^1] == second.Id && vm.Sessions.Count == 1,
        "Returning with a saved server must refresh that server only");
    Require(vm.ConnectionFilter?.Id == second.Id && vm.SelectedCluster!.ClusterUuid == "cluster-2"
        && vm.InfobaseFilter!.InfobaseUuid == "base-2" && vm.SearchText == "Test user" && vm.IsAutoRefreshEnabled,
        "Server/cluster/base/search/auto state must survive a return");
    Pass("Returning refreshes the selected server and preserves every saved filter");

    vm.ConnectionFilter = vm.ConnectionFilters[0];
    reads = client.ReadCalls;
    await vm.ActivateAsync();
    Require(client.ReadCalls == reads + 1 && client.ReadProfiles[^1] == second.Id && vm.ConnectionFilter?.Id is null
        && vm.SelectedCluster!.ServerId == second.Id && vm.Sessions.Count == 1,
        "All-server selection with a specific cluster must refresh that cluster's owner only");
    Pass("Specific cluster determines refresh owner while server filter stays All");

    vm.SelectedCluster = ServerClusterFilter.All;
    reads = client.ReadCalls;
    await vm.RefreshScopeCommand.ExecuteAsync(null);
    Require(client.ReadCalls == reads + 1 && client.ReadProfiles[^1] == second.Id && vm.SelectedCluster.IsAll
        && vm.InfobaseFilter!.ServerId == second.Id && vm.Sessions.Count == 1,
        "Specific base must determine refresh owner when server and cluster filters are All");
    Pass("Specific base determines refresh owner while server and cluster filters stay All");
}

async Task FailedScopeRecoveryAsync()
{
    var second = Profile("second.example");
    var (vm, client, _) = Create([remote, second]);
    client.ReadProfile = _ => ScopeSnapshot();
    await vm.ActivateAsync();
    vm.SelectedCluster = vm.Clusters.Single(c => c.ServerId == remote.Id && c.ClusterUuid == "cluster-2");
    vm.InfobaseFilter = vm.InfobaseFilters.Single(b => b.InfobaseUuid == "base-2");
    vm.SearchText = "Test user";
    vm.IsAutoRefreshEnabled = true;
    client.ReadProfile = _ => throw new InvalidOperationException("Target unavailable");
    var reads = client.ReadCalls;
    await vm.ActivateAsync();
    Require(client.ReadCalls == reads + 1 && client.ReadProfiles[^1] == remote.Id && vm.Sessions.Count == 0,
        "Failed scoped read must not expose another server's cached rows");
    Require(vm.ConnectionFilter?.Id == remote.Id && vm.SelectedCluster!.ServerId == remote.Id && vm.SelectedCluster.ClusterUuid == "cluster-2"
        && vm.InfobaseFilter!.InfobaseUuid == "base-2" && vm.Clusters.Contains(vm.SelectedCluster) && vm.InfobaseFilters.Contains(vm.InfobaseFilter)
        && vm.SearchText == "Test user" && vm.IsAutoRefreshEnabled,
        "A failed read must retain typed pending options and remembered filters");
    Require(vm.EmptyStateText.Contains("Не удалось"), "Pending options must keep honest error state");
    client.ReadProfile = _ => ScopeSnapshot("Renamed cluster", "Renamed base");
    await vm.ActivateAsync();
    Require(client.ReadCalls == reads + 2 && client.ReadProfiles[^1] == remote.Id && vm.Sessions.Count == 1
        && vm.SelectedCluster!.Name == "Renamed cluster" && vm.InfobaseFilter!.Name == "Renamed base",
        "Recovery must retry the pending owner and restore the same composite filter");
    Pass("Failed scoped entry retains pending filters and recovers without widening reads or rows");
}

async Task PartialPendingBaseRecoveryAsync()
{
    var (vm, client, _) = Create([remote]);
    client.NextRead = () => ScopeSnapshot();
    await vm.ActivateAsync();
    vm.SelectedCluster = vm.Clusters.Single(c => c.ClusterUuid == "cluster-2");
    vm.InfobaseFilter = vm.InfobaseFilters.Single(b => b.InfobaseUuid == "base-2");
    client.NextRead = () => new([new OneCClusterInfo { Uuid = "cluster-2", Name = "Cluster 2", DetailsMessage = "Infobase list unavailable" }], DateTimeOffset.Now);
    await vm.RefreshScopeCommand.ExecuteAsync(null);
    Require(vm.Connections[0].IsConnected && vm.SelectedCluster!.ClusterUuid == "cluster-2" && vm.InfobaseFilter!.InfobaseUuid == "base-2"
        && vm.InfobaseFilters.Contains(vm.InfobaseFilter) && vm.Sessions.Count == 0 && vm.EmptyStateText.Contains("неполные"),
        "Partial unavailable registration list must retain the pending selected base without rows");
    client.NextRead = () => ScopeSnapshot();
    await vm.RefreshScopeCommand.ExecuteAsync(null);
    Require(vm.InfobaseFilter!.InfobaseUuid == "base-2" && vm.Sessions.Count == 1, "Partial pending base must recover in its original scope");
    Pass("Partial base-list failure retains a pending base and restores it on retry");
}

async Task CoalescedActivationAsync()
{
    var second = Profile("second.example");
    var (vm, client, _) = Create([remote, second]);
    await vm.ActivateAsync();
    vm.ConnectionFilter = vm.ConnectionFilters.Single(c => c.Id == remote.Id);
    var waitingRead = new TaskCompletionSource<OneCServerSessionSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
    var readStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var pendingReads = 0;
    client.ReadAsyncProfile = _ =>
    {
        if (Interlocked.Increment(ref pendingReads) != 1) return Task.FromResult(FullSnapshot());
        readStarted.SetResult();
        return waitingRead.Task;
    };
    var reads = client.ReadCalls;
    var first = vm.ActivateAsync();
    await readStarted.Task;
    vm.ConnectionFilter = vm.ConnectionFilters.Single(c => c.Id == second.Id);
    var repeated = Enumerable.Range(0, 8).Select(_ => vm.ActivateAsync()).ToArray();
    Require(repeated.All(t => ReferenceEquals(t, first)), "Pending activation calls must share one task");
    await vm.LoadAsync();
    var idle = vm.WaitForIdleAsync();
    Require(vm.IsBusy && !vm.CanEdit && !idle.IsCompleted && !first.IsCompleted && client.ReadCalls == reads + 1,
        "Cached profile loads and idle waits must not clear another operation's busy state");
    waitingRead.SetResult(FullSnapshot());
    await Task.WhenAll(repeated.Append(first));
    await idle;
    Require(pendingReads == 2 && client.ReadCalls == reads + 2 && client.ReadProfiles[^1] == second.Id
        && vm.ConnectionFilter?.Id == second.Id && !vm.IsBusy,
        "Many pending entries must coalesce to one latest-scope retry");
    Pass("Pending entries share one task and queue one latest-scope refresh without clearing busy");
}

async Task SharedPendingLoadAsync()
{
    var second = Profile("second.example");
    var profilesReady = new TaskCompletionSource<IReadOnlyList<OneCServerConnectionProfile>>(TaskCreationOptions.RunContinuationsAsynchronously);
    var store = new FakeStore([remote, second]) { PendingLoad = profilesReady.Task };
    var client = new FakeClient();
    var vm = new SessionsPageViewModel(client, store, new(new FakeInventory(), Environment.MachineName));
    var firstLoad = vm.LoadAsync();
    var entry = vm.ActivateAsync();
    var repeatLoad = vm.LoadAsync();
    var repeatEntry = vm.ActivateAsync();
    Require(ReferenceEquals(firstLoad, repeatLoad) && ReferenceEquals(entry, repeatEntry) && vm.IsBusy && !vm.IsLoaded,
        "Shared profile loads and first pending entries must coalesce");
    profilesReady.SetResult([remote, second]);
    await Task.WhenAll(firstLoad, repeatLoad, entry, repeatEntry);
    Require(store.LoadCalls == 1 && vm.Connections.Count == 2 && client.ReadCalls == 2 && !vm.IsBusy,
        "A concurrent owner load and page entry must load once and read All once");
    Pass("Shared pending profile load and first activation coalesce without duplicate profiles or reads");
}

async Task ActivationWaitsForSaveAsync()
{
    var saveReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var store = new FakeStore([remote]) { PendingSave = saveReady.Task };
    var client = new FakeClient();
    var vm = new SessionsPageViewModel(client, store, new(new FakeInventory(), Environment.MachineName));
    await vm.LoadAsync();
    var save = vm.SaveConnectionAsync(remote with { Name = "Saved alias" }, null);
    var entry = vm.ActivateAsync();
    var repeated = Enumerable.Range(0, 4).Select(_ => vm.ActivateAsync()).ToArray();
    Require(vm.IsBusy && !entry.IsCompleted && client.ReadCalls == 0, "Activation must wait behind an active save without clearing its busy state");
    saveReady.SetResult();
    await Task.WhenAll(repeated.Append(entry).Append(save));
    Require(store.SaveCalls == 1 && client.ReadCalls == 1 && vm.Connections.Single().Name == "Saved alias" && !vm.IsBusy,
        "Entries queued before a read starts must coalesce to one refresh of the final saved profile");
    Pass("Activation waits for save and coalesces requests already queued before reading starts");
}

async Task ClusterOwnerAndReverseSelectionAsync()
{
    var second = Profile("second.example");
    var (vm, client, _) = Create([remote, second]);
    client.ReadProfile = _ => new([
        Cluster("shared-cluster", "Same cluster", [Base("same-base", "Same base")], [Session("same-session", "same-base")]),
        Cluster("other-cluster", "Same cluster", [Base("other-base", "Same base")], [Session("other-session", "other-base")])
    ], DateTimeOffset.Now);
    await vm.ActivateAsync();
    var firstChoice = vm.Clusters.Single(c => c.ServerId == remote.Id && c.ClusterUuid == "shared-cluster");
    var secondChoice = vm.Clusters.Single(c => c.ServerId == second.Id && c.ClusterUuid == "shared-cluster");
    var reads = client.ReadCalls;
    vm.SelectedCluster = secondChoice;
    Require(vm.ConnectionFilter?.Id == second.Id && vm.SelectedConnection?.Profile.Id == second.Id
        && vm.Sessions.Count == 1 && vm.Sessions[0].Profile.Id == second.Id,
        "Choosing a concrete cluster must select its owner even when another owner has the same UUID and name");
    vm.InfobaseFilter = vm.InfobaseFilters.Single(b => !b.IsAll);
    vm.ConnectionFilter = vm.ConnectionFilters[0];
    vm.SelectedCluster = firstChoice;
    Require(vm.ConnectionFilter?.Id == remote.Id && vm.SelectedConnection?.Profile.Id == remote.Id
        && vm.InfobaseFilter!.IsAll && vm.Sessions.Count == 1 && vm.Sessions[0].Profile.Id == remote.Id,
        "Reverse selection must select the other owner and clear an incompatible base without mixing identical UUIDs");

    var selectionNotifications = 0;
    vm.PropertyChanged += (_, change) =>
    {
        if (change.PropertyName is nameof(vm.ConnectionFilter) or nameof(vm.SelectedConnection)
            or nameof(vm.SelectedCluster) or nameof(vm.InfobaseFilter)) selectionNotifications++;
    };
    const int repetitions = 40;
    for (var index = 0; index < repetitions; index++)
    {
        vm.ConnectionFilter = vm.ConnectionFilters[0];
        vm.SelectedCluster = secondChoice;
        Require(vm.ConnectionFilter?.Id == second.Id && vm.Sessions.Single().Profile.Id == second.Id,
            "Repeated second-owner selection must finish in the requested scope");
        vm.ConnectionFilter = vm.ConnectionFilters[0];
        vm.SelectedCluster = firstChoice;
        Require(vm.ConnectionFilter?.Id == remote.Id && vm.Sessions.Single().Profile.Id == remote.Id,
            "Repeated first-owner selection must finish in the requested scope");
    }
    vm.ConnectionFilter = vm.ConnectionFilters[0];
    vm.SelectedCluster = ServerClusterFilter.All;
    vm.InfobaseFilter = ServerInfobaseFilter.All;
    Require(vm.ConnectionFilter?.Id is null && vm.SelectedConnection is null && vm.Sessions.Count == 4,
        "Returning to all explicit sentinels must restore all cached rows");
    Require(selectionNotifications < repetitions * 24 && client.ReadCalls == reads,
        "Selection synchronization must remain bounded and must not launch reads");
    Pass("Concrete cluster selects its owner; reverse and repeated equal-UUID owner switches finish without recursive reads");
}

async Task DelayedSingleClusterSelectionAsync()
{
    var (vm, client, _) = Create([remote]);
    await vm.LoadAsync();
    vm.ConnectionFilter = vm.ConnectionFilters.Single(c => c.Id == remote.Id);
    Require(vm.SelectedConnection?.Profile.Id == remote.Id && vm.SelectedCluster!.IsAll && client.ReadCalls == 0,
        "Selecting an unread server must wait for a confirmed cluster snapshot");
    client.NextRead = () => throw new InvalidOperationException("First read unavailable");
    await vm.RefreshScopeCommand.ExecuteAsync(null);
    Require(vm.ConnectionFilter?.Id == remote.Id && vm.SelectedCluster!.IsAll && !vm.SelectedConnection!.IsConnected,
        "A failed first read must not invent a cluster or lose the selected server");
    client.NextRead = () => FullSnapshot();
    await vm.RefreshScopeCommand.ExecuteAsync(null);
    Require(vm.SelectedCluster!.ServerId == remote.Id && vm.SelectedCluster.ClusterUuid == "cluster-1" && vm.Sessions.Count == 1,
        "The first successful singleton snapshot must complete the original explicit server selection");
    vm.SelectedCluster = ServerClusterFilter.All;
    await vm.RefreshScopeCommand.ExecuteAsync(null);
    await vm.ActivateAsync();
    Require(vm.ConnectionFilter?.Id == remote.Id && vm.SelectedCluster!.IsAll && vm.Sessions.Count == 1,
        "Manual and activation refreshes must keep an explicitly selected All clusters choice");
    var reads = client.ReadCalls;
    vm.ConnectionFilter = vm.ConnectionFilters[0];
    vm.ConnectionFilter = vm.ConnectionFilters.Single(c => c.Id == remote.Id);
    Require(vm.SelectedCluster!.ServerId == remote.Id && vm.SelectedCluster.ClusterUuid == "cluster-1" && client.ReadCalls == reads,
        "A new explicit selection of an already confirmed singleton server must select its sole cluster immediately");
    Pass("Unread singleton server selects its cluster on recovery; explicit All survives manual and activation refreshes");
}

async Task MultiClusterAndSingletonSelectionAsync()
{
    var second = Profile("second.example");
    var (vm, client, _) = Create([remote, second]);
    var firstSnapshot = ScopeSnapshot();
    client.ReadProfile = profile => profile.Id == remote.Id ? firstSnapshot : new([
        Cluster("single-cluster", "Single cluster", [Base("single-base", "Single base")], [Session("single-session", "single-base")])
    ], DateTimeOffset.Now);
    await vm.ActivateAsync();
    var reads = client.ReadCalls;
    vm.ConnectionFilter = vm.ConnectionFilters.Single(c => c.Id == remote.Id);
    Require(vm.SelectedCluster!.IsAll && vm.Sessions.Count == 2,
        "An explicit multi-cluster server choice must not select an arbitrary first cluster");
    vm.SelectedCluster = vm.Clusters.Single(c => c.ClusterUuid == "cluster-2");
    vm.InfobaseFilter = vm.InfobaseFilters.Single(b => b.InfobaseUuid == "base-2");
    vm.ConnectionFilter = vm.ConnectionFilters.Single(c => c.Id == second.Id);
    Require(vm.SelectedCluster!.ServerId == second.Id && vm.SelectedCluster.ClusterUuid == "single-cluster"
        && vm.InfobaseFilter!.IsAll && vm.Sessions.Single().Profile.Id == second.Id,
        "Choosing another singleton server must select its sole cluster and reset an incompatible old base");
    vm.ConnectionFilter = vm.ConnectionFilters.Single(c => c.Id == remote.Id);
    Require(vm.SelectedCluster!.IsAll && vm.InfobaseFilter!.IsAll && vm.Sessions.Count == 2 && client.ReadCalls == reads,
        "Returning to a multi-cluster owner must restore All when the previous cluster belongs elsewhere");
    firstSnapshot = new([Cluster("cluster-1", "Cluster 1", [Base("base-1", "Base 1")], [Session("session-one", "base-1")])], DateTimeOffset.Now);
    await vm.RefreshScopeCommand.ExecuteAsync(null);
    Require(vm.SelectedCluster!.IsAll && vm.Sessions.Count == 1,
        "Ordinary refresh must preserve All when a previously multi-cluster server becomes a singleton");
    Pass("Multi-cluster selection stays All; singleton owner switch selects its cluster and resets incompatible base");
}

async Task DirectConnectionSelectionAsync()
{
    var second = Profile("second.example");
    var (vm, client, _) = Create([remote, second]);
    await vm.LoadAsync();
    vm.SelectedConnection = vm.Connections.Single(c => c.Profile.Id == remote.Id);
    Require(vm.ConnectionFilter?.Id == remote.Id && vm.SelectedCluster!.IsAll,
        "Direct connection selection must synchronize the server filter while its snapshot is unknown");
    await vm.RefreshScopeCommand.ExecuteAsync(null);
    Require(client.ReadCalls == 1 && client.ReadProfiles[^1] == remote.Id && vm.SelectedCluster!.ServerId == remote.Id,
        "Direct unread selection must read only its owner and select that owner's sole cluster");
    vm.SelectedCluster = ServerClusterFilter.All;
    vm.SelectedConnection = vm.Connections.Single(c => c.Profile.Id == second.Id);
    await vm.RefreshScopeCommand.ExecuteAsync(null);
    Require(client.ReadCalls == 2 && client.ReadProfiles[^1] == second.Id && vm.SelectedCluster!.ServerId == second.Id,
        "Direct switching to another unread singleton must synchronize both owner and cluster");
    vm.SelectedConnection = vm.Connections.Single(c => c.Profile.Id == remote.Id);
    Require(vm.ConnectionFilter?.Id == remote.Id && vm.SelectedCluster!.ServerId == remote.Id
        && vm.Sessions.Single().Profile.Id == remote.Id && client.ReadCalls == 2,
        "The direct already-read selection path must choose the correct sole cluster without another read");
    Pass("Direct SelectedConnection path synchronizes owner and selects both unread and cached sole clusters");
}

OneCServerSessionSnapshot ScopeSnapshot(string clusterName = "Cluster 2", string baseName = "Base 2") => new([
    Cluster("cluster-1", "Cluster 1", [Base("base-1", "Base 1")], [Session("session-one", "base-1")]),
    Cluster("cluster-2", clusterName, [Base("base-2", baseName)], [Session("session-two", "base-2")])
], DateTimeOffset.Now);

OneCClusterInfo Cluster(string uuid, string name, IReadOnlyList<OneCInfobaseSummaryInfo> bases, IReadOnlyList<OneCSessionInfo> sessions)
    => new() { Uuid = uuid, Name = name, Host = "cluster.example", Port = uuid == "other-cluster" ? 2541 : 1541, Infobases = bases, Sessions = sessions };
OneCInfobaseSummaryInfo Base(string uuid, string name) => new() { Uuid = uuid, Name = name };
OneCSessionInfo Session(string uuid, string? baseUuid) => new() { Uuid = uuid, InfobaseUuid = baseUuid, UserName = "Test user" };

(SessionsPageViewModel, FakeClient, FakeInventory) Create(IReadOnlyList<OneCServerConnectionProfile> profiles, IReadOnlyList<OneCServiceInfo>? services = null)
{
    var client = new FakeClient();
    var inventory = new FakeInventory { Services = services ?? [] };
    return (new(client, new FakeStore(profiles), new(inventory, Environment.MachineName)), client, inventory);
}

OneCServerConnectionProfile Profile(string host) => new()
{
    Name = host, Host = host, PlatformDirectory = @"C:\platform\bin", PlatformVersion = "8.3.27.2170"
};

OneCServiceInfo Service(string state) => new()
{
    Name = "TestAgent", DisplayName = "Test Agent", Kind = OneCServiceKind.ServerAgent,
    State = state, Status = "OK", ExecutablePath = @"C:\platform\bin\ragent.exe",
    Version = "8.3.27.2170", AgentPort = 1540
};

static OneCServerSessionSnapshot FullSnapshot() => new(
    [new OneCClusterInfo { Uuid = "cluster-1", Name = "Cluster", Sessions = [new OneCSessionInfo { Uuid = "session-1", UserName = "Test user" }] }], DateTimeOffset.Now);
void Require(bool condition, string failure) { if (!condition) throw new InvalidOperationException(failure); }
void Pass(string scenario) { passed++; Console.WriteLine("PASS " + scenario); }

sealed class FakeClient : IOneCServerSessionClient
{
    public int ReadCalls { get; private set; }
    public List<Guid> ReadProfiles { get; } = [];
    public int TerminateCalls { get; private set; }
    public Func<OneCServerSessionSnapshot> NextRead { get; set; } = () => new(
        [new OneCClusterInfo { Uuid = "cluster-1", Name = "Cluster", Sessions = [new OneCSessionInfo { Uuid = "session-1", UserName = "Test user" }] }], DateTimeOffset.Now);
    public Func<OneCServerConnectionProfile, OneCServerSessionSnapshot>? ReadProfile { get; set; }
    public Func<OneCServerConnectionProfile, Task<OneCServerSessionSnapshot>>? ReadAsyncProfile { get; set; }
    public Task<OneCServerSessionSnapshot> ReadAsync(OneCServerConnectionProfile profile, string? password, CancellationToken cancellationToken = default)
    {
        ReadCalls++;
        ReadProfiles.Add(profile.Id);
        if (ReadAsyncProfile is not null) return ReadAsyncProfile(profile);
        return Task.FromResult(ReadProfile is null ? NextRead() : ReadProfile(profile));
    }
    public Task<IReadOnlyList<OneCSessionTerminationResult>> TerminateAsync(OneCServerConnectionProfile profile, string? password,
        IReadOnlyList<OneCSessionTarget> targets, string message, CancellationToken cancellationToken = default)
    {
        TerminateCalls++;
        throw new InvalidOperationException("Termination must not run in the stale-row scenario");
    }
}

sealed class FakeInventory : IOneCServiceInventory
{
    public IReadOnlyList<OneCServiceInfo> Services { get; set; } = [];
    public int ReadCalls { get; private set; }
    public Task<IReadOnlyList<OneCServiceInfo>> GetServicesAsync(CancellationToken cancellationToken = default)
    {
        ReadCalls++;
        return Task.FromResult(Services);
    }
}

sealed class FakeStore(IReadOnlyList<OneCServerConnectionProfile> profiles) : IOneCServerConnectionStore
{
    public int LoadCalls { get; private set; }
    public int SaveCalls { get; private set; }
    public Task<IReadOnlyList<OneCServerConnectionProfile>>? PendingLoad { get; set; }
    public Task? PendingSave { get; set; }
    public Task<IReadOnlyList<OneCServerConnectionProfile>> LoadAsync(CancellationToken cancellationToken = default)
    {
        LoadCalls++;
        return PendingLoad ?? Task.FromResult(profiles);
    }
    public Task SaveAsync(IReadOnlyList<OneCServerConnectionProfile> value, CancellationToken cancellationToken = default)
    {
        SaveCalls++;
        return PendingSave ?? Task.CompletedTask;
    }
}
