using System.Text.Json;
using AdminConsoleFor1C.App;
using AdminConsoleFor1C.Application.Services;
using AdminConsoleFor1C.Core.Administration;
using AdminConsoleFor1C.Core.Services;
using Microsoft.UI.Xaml.Controls;

var results = new List<(string Name, bool Passed, string? Error)>();
var p1 = Profile("server-one");
var p2 = Profile("server-two");
var sharedCluster = "cccccccc-cccc-cccc-cccc-cccccccccccc";
var sharedBase = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";

await Case("All scope aggregates all connected servers", async () =>
{
    var fixture = await Fixture.Create(p1, p2);
    fixture.Client.Data[p1.Id] = [Cluster("One", Base("Accounting"))];
    fixture.Client.Data[p2.Id] = [Cluster("Two", Base("Trade"))];
    await fixture.Vm.ConnectAllCommand.ExecuteAsync(null);
    Require(fixture.Vm.ConnectionFilter?.Id is null && fixture.Vm.SelectedCluster!.IsAll && fixture.Vm.InfobaseFilter!.IsAll, "All defaults lost");
    Require(fixture.Vm.Infobases.Count == 2, "Aggregate omitted a server");
});

await Case("Duplicate names and UUIDs remain separate identities", async () =>
{
    var fixture = await TwoDuplicateServers();
    Require(fixture.Vm.Infobases.Count == 2 && fixture.Vm.InfobaseFilters.Count == 3, "Duplicate bases collapsed");
    Require(fixture.Vm.Clusters.Count == 3, "Duplicate cluster UUIDs collapsed across servers");
    fixture.Vm.InfobaseFilter = fixture.Vm.InfobaseFilters.Single(f => f.ServerId == p2.Id);
    Require(fixture.Vm.Infobases.Count == 1 && fixture.Vm.Infobases[0].Profile.Id == p2.Id, "Composite base filter selected another server");
});

await Case("Literal All base name is an ordinary concrete filter", async () =>
{
    var fixture = await Fixture.Create(p1);
    fixture.Client.Data[p1.Id] = [Cluster("One", Base("Все базы"), Base("Other", Guid.NewGuid().ToString()))];
    await fixture.Vm.ConnectAllCommand.ExecuteAsync(null);
    fixture.Vm.InfobaseFilter = fixture.Vm.InfobaseFilters.Single(f => f.Name == "Все базы" && !f.IsAll);
    Require(fixture.Vm.Infobases.Count == 1 && fixture.Vm.Infobases[0].NameText == "Все базы", "Literal All name removed filtering");
});

await Case("All creation is disabled while selecting a server chooses its sole confirmed cluster", async () =>
{
    var fixture = await Fixture.Create(p1);
    fixture.Client.Data[p1.Id] = [Cluster("One", Base("Accounting"))];
    await fixture.Vm.ConnectAllCommand.ExecuteAsync(null);
    Require(!fixture.Vm.CanCreate, "All server scope selected a creation target");
    fixture.Vm.SelectServer(p1.Id);
    Require(fixture.Vm.CanCreate && fixture.Vm.SelectedCluster?.ServerId == p1.Id, "Selected server did not choose its sole confirmed cluster");
    fixture.Vm.SelectedCluster = ServerClusterFilter.All;
    Require(!fixture.Vm.CanCreate, "Explicit All cluster scope retained a creation target");
});

await Case("Concrete cluster selects its owning server even with duplicate UUIDs", async () =>
{
    var fixture = await TwoDuplicateServers();
    var otherCluster = fixture.Vm.Clusters.Single(c => c.ServerId == p2.Id);
    fixture.Vm.SelectServer(p1.Id);
    fixture.Vm.SelectedCluster = otherCluster;
    Require(fixture.Vm.ConnectionFilter?.Id == p2.Id && fixture.Vm.SelectedServer?.Profile.Id == p2.Id, "Concrete cluster did not select its owning server");
    Require(fixture.Vm.SelectedCluster?.ServerId == p2.Id && fixture.Vm.CanCreate && fixture.Vm.CreationCluster?.Host == "two", "Duplicate UUID selected another server's creation cluster");
});

await Case("Partial server error preserves successful server data", async () =>
{
    var fixture = await Fixture.Create(p1, p2);
    fixture.Client.Data[p1.Id] = [Cluster("One", Base("Accounting"))];
    fixture.Client.Errors[p2.Id] = new UnauthorizedAccessException("Server two denied");
    await fixture.Vm.ConnectAllCommand.ExecuteAsync(null);
    Require(fixture.Vm.Infobases.Count == 1 && fixture.Vm.Infobases[0].Profile.Id == p1.Id, "Successful partial data was discarded");
    Require(fixture.Vm.HasConnectionError && fixture.Vm.ConnectionErrorSeverity == InfoBarSeverity.Warning, "Partial failure was not reported as warning");
    fixture.Vm.SelectServer(p2.Id);
    Require(fixture.Vm.Infobases.Count == 0 && fixture.Vm.EmptyStateText.Contains("Не удалось"), "Unavailable server rendered as empty database list");
});

await Case("Cluster partial error retains its data and disables creation", async () =>
{
    var fixture = await Fixture.Create(p1);
    fixture.Client.Data[p1.Id] = [Cluster("One", Base("Accounting")) with { DetailsMessage = "Some database data denied" }];
    await fixture.Vm.ConnectAllCommand.ExecuteAsync(null);
    fixture.Vm.SelectServer(p1.Id);
    fixture.Vm.SelectedCluster = fixture.Vm.Clusters.Single(c => !c.IsAll);
    Require(fixture.Vm.Infobases.Count == 1 && fixture.Vm.HasConnectionError && !fixture.Vm.CanCreate, "Cluster partial error was hidden");
});

await Case("Refresh preserves composite cluster and base choice", async () =>
{
    var fixture = await TwoDuplicateServers();
    fixture.Vm.SelectedCluster = fixture.Vm.Clusters.Single(c => c.ServerId == p2.Id);
    fixture.Vm.InfobaseFilter = fixture.Vm.InfobaseFilters.Single(f => !f.IsAll);
    await fixture.Vm.RefreshAsync();
    Require(fixture.Vm.SelectedCluster!.ServerId == p2.Id && fixture.Vm.InfobaseFilter!.ServerId == p2.Id, "Refresh chose duplicate UUID on another server");
    Require(fixture.Vm.Infobases.Count == 1 && fixture.Vm.Infobases[0].Profile.Id == p2.Id, "Refresh expanded the selected composite scope");
});

await Case("Refresh treats UUID letter case as the same identity", async () =>
{
    var fixture = await TwoDuplicateServers();
    fixture.Vm.SelectedCluster = fixture.Vm.Clusters.Single(c => c.ServerId == p2.Id);
    fixture.Vm.InfobaseFilter = fixture.Vm.InfobaseFilters.Single(f => !f.IsAll);
    fixture.Client.Data[p2.Id] = [Cluster("Two", Base("Accounting", sharedBase.ToUpperInvariant())) with { Uuid = sharedCluster.ToUpperInvariant() }];
    await fixture.Vm.RefreshAsync();
    Require(fixture.Vm.SelectedCluster!.ServerId == p2.Id && fixture.Vm.InfobaseFilter!.ServerId == p2.Id, "UUID case change reset selected scope to All");
});

await Case("Unified refresh reads the selected scope without prior monitoring intent", async () =>
{
    var fixture = await Fixture.Create(p1, p2);
    fixture.Client.Data[p1.Id] = [Cluster("One", Base("Accounting"))];
    fixture.Client.Data[p2.Id] = [Cluster("Two", Base("Trade"))];
    fixture.Vm.SelectServer(p1.Id);
    await fixture.Vm.RefreshAsync();
    await fixture.Vm.RefreshServersAsync();
    Require(fixture.Client.Reads[p1.Id] == 1 && !fixture.Client.Reads.ContainsKey(p2.Id), "Refresh did not use the selected server scope");
    Require(fixture.Vm.Infobases.Count == 1 && fixture.Vm.Infobases[0].Profile.Id == p1.Id, "Initial refresh did not load the selected server");
});

await Case("Stopped local service is not queried", async () =>
{
    var local = Profile("local") with { Host = "localhost" };
    var fixture = await Fixture.Create([local], [Service("Stopped")]);
    fixture.Client.Data[local.Id] = [Cluster("One", Base("Accounting"))];
    await fixture.Vm.ConnectAllCommand.ExecuteAsync(null);
    Require(!fixture.Client.Reads.ContainsKey(local.Id), "Stopped local service was queried");
    Require(fixture.Vm.HasConnectionError && fixture.Vm.Infobases.Count == 0, "Stopped local service was rendered as success");
});

await Case("Removing selected saved server clears stale data without auto-first selection", async () =>
{
    var fixture = await TwoDuplicateServers();
    fixture.Vm.SelectServer(p1.Id);
    fixture.Vm.SelectedCluster = fixture.Vm.Clusters.Single(c => !c.IsAll);
    await fixture.Vm.RemoveSelectedServerAsync();
    Require(fixture.Vm.SelectedServer is null && fixture.Vm.ConnectionFilter?.Id is null, "Removed server selected another concrete target");
    Require(fixture.Vm.Infobases.All(r => r.Profile.Id != p1.Id) && !fixture.Vm.CanCreate, "Removed server retained creation/data");
});

await Case("Changed profile clears cached old endpoint data", async () =>
{
    var fixture = await TwoDuplicateServers();
    fixture.Vm.SelectServer(p1.Id);
    fixture.Vm.SelectedCluster = fixture.Vm.Clusters.Single(c => !c.IsAll);
    await fixture.Owner.SaveConnectionAsync(p1 with { Host = "server-changed", AgentPort = 2540 }, null);
    await fixture.Vm.RefreshServersAsync();
    Require(fixture.Vm.SelectedServer?.Profile.Host == "server-changed", "Profile choice did not adopt changed endpoint");
    Require(fixture.Vm.Infobases.Count == 0 && !fixture.Vm.CanCreate && fixture.Vm.SelectedServer is { IsConnected: false }, "Old endpoint data survived profile edit");
});

await Case("Removed discovered service clears stale rows and preserves All fallback", async () =>
{
    var fixture = await Fixture.Create([p1], [Service("Running")]);
    var discovered = fixture.Vm.Connections.Single(c => c.IsDiscovered);
    fixture.Client.Data[p1.Id] = [Cluster("Saved", Base("Saved"))];
    fixture.Client.Data[discovered.Profile.Id] = [Cluster("Local", Base("Local"))];
    await fixture.Vm.ConnectAllCommand.ExecuteAsync(null);
    fixture.Vm.SelectServer(discovered.Profile.Id);
    fixture.Inventory.Services = [];
    await fixture.Vm.RefreshServersAsync();
    Require(fixture.Vm.SelectedServer is null && fixture.Vm.ConnectionFilter?.Id is null, "Removed discovery auto-selected saved target");
    Require(fixture.Vm.Infobases.Count == 1 && fixture.Vm.Infobases[0].Profile.Id == p1.Id, "Removed discovery left stale data");
});

await Case("Discovery refresh preserves item identity, scope and loaded rows", async () =>
{
    var fixture = await TwoDuplicateServers();
    fixture.Vm.SelectServer(p2.Id);
    fixture.Vm.SelectedCluster = fixture.Vm.Clusters.Single(c => !c.IsAll);
    var selected = fixture.Vm.SelectedServer;
    var readCount = fixture.Client.Reads[p2.Id];
    await fixture.Vm.RefreshServersAsync();
    Require(ReferenceEquals(selected, fixture.Vm.SelectedServer), "Discovery replaced unchanged item identity");
    Require(fixture.Vm.SelectedCluster!.ServerId == p2.Id && fixture.Vm.Infobases.Count == 1, "Discovery reset loaded scope");
    Require(fixture.Client.Reads[p2.Id] == readCount, "Discovery performed an unexpected server read");
});

await Case("Saving a discovered profile updates Infobases removal capability", async () =>
{
    var fixture = await Fixture.Create([p1], [Service("Running")]);
    var discovered = fixture.Vm.Connections.Single(c => c.IsDiscovered);
    fixture.Vm.SelectServer(discovered.Profile.Id);
    await fixture.Owner.SaveConnectionAsync(discovered.Profile, null);
    await fixture.Vm.RefreshServersAsync();
    Require(fixture.Vm.SelectedServer is { IsDiscovered: false } && fixture.Vm.CanRemoveSelected, "Saved profile still treated as discovered and cannot be removed");
});

await Case("Shared connection owner busy transitions notify Infobases bindings", async () =>
{
    var fixture = await Fixture.Create(p1);
    var notifications = new List<string?>();
    fixture.Vm.PropertyChanged += (_, e) => notifications.Add(e.PropertyName);
    fixture.Owner.IsBusy = true;
    Require(!fixture.Vm.CanEdit, "Computed busy state did not propagate");
    fixture.Owner.IsBusy = false;
    Require(fixture.Vm.CanEdit, "Computed edit state did not recover");
    Require(notifications.Contains(nameof(InfobasesPageViewModel.CanEdit)), "CanEdit changed without PropertyChanged; UI can remain disabled after page navigation during owner load");
});

await Case("Returning to the page does not unlock an active inventory read", async () =>
{
    var fixture = await Fixture.Create(p1);
    var pending = new TaskCompletionSource<IReadOnlyList<OneCClusterInfo>>(TaskCreationOptions.RunContinuationsAsynchronously);
    fixture.Client.ReadOverride = (_, _, _) => pending.Task;
    var read = fixture.Vm.ConnectAllCommand.ExecuteAsync(null);
    try
    {
        Require(fixture.Vm.IsBusy, "Read did not enter busy state");
        await fixture.Vm.LoadAsync();
        Require(fixture.Vm.IsBusy && !fixture.Vm.CanEdit, "Page reload unlocked the active read");
    }
    finally { pending.TrySetResult([Cluster("One", Base("Accounting"))]); await read; }
    Require(!fixture.Vm.IsBusy && fixture.Vm.Infobases.Count == 1, "Read did not finish normally after page reload");
});

await Case("Profile edit during pending inventory read discards old endpoint data", async () =>
{
    var fixture = await Fixture.Create(p1);
    var pending = new TaskCompletionSource<IReadOnlyList<OneCClusterInfo>>(TaskCreationOptions.RunContinuationsAsynchronously);
    fixture.Client.ReadOverride = (_, _, _) => pending.Task;
    var read = fixture.Vm.ConnectAllCommand.ExecuteAsync(null);
    try
    {
        await fixture.Owner.SaveConnectionAsync(p1 with { Host = "server-edited-during-read", AgentPort = 2540 }, null);
    }
    finally { pending.TrySetResult([Cluster("Old endpoint", Base("Old endpoint data"))]); await read; }
    fixture.Vm.SelectServer(p1.Id);
    Require(fixture.Vm.SelectedServer?.Profile.Host == "server-edited-during-read", "Completed read retained the old profile");
    Require(fixture.Vm.Infobases.Count == 0 && !fixture.Vm.CanCreate && fixture.Vm.Clusters.Count == 1,
        "Completed stale read restored old endpoint data or a creation target");
});

await Case("Client cancellation without requested scope cancellation preserves other server data", async () =>
{
    var fixture = await Fixture.Create(p1, p2);
    fixture.Client.Data[p1.Id] = [Cluster("One", Base("Accounting"))];
    fixture.Client.Errors[p2.Id] = new OperationCanceledException("Server two unexpectedly canceled");
    await fixture.Vm.ConnectAllCommand.ExecuteAsync(null);
    Require(fixture.Vm.Infobases.Count == 1 && fixture.Vm.Infobases[0].Profile.Id == p1.Id, "Unexpected cancellation on one client discarded successful server data");
    Require(fixture.Vm.HasConnectionError && !fixture.Vm.Connections.Single(c => c.Profile.Id == p2.Id).IsConnected, "Unexpected cancellation did not become per-server failure");
});

await Case("Unexpected client cancellation on refresh invalidates stale creation target", async () =>
{
    var fixture = await Fixture.Create(p1);
    fixture.Client.Data[p1.Id] = [Cluster("One", Base("Accounting"))];
    await fixture.Vm.ConnectAllCommand.ExecuteAsync(null);
    fixture.Vm.SelectServer(p1.Id);
    fixture.Vm.SelectedCluster = fixture.Vm.Clusters.Single(c => !c.IsAll);
    Require(fixture.Vm.CanCreate, "Initial explicit target not available");
    fixture.Client.Errors[p1.Id] = new OperationCanceledException("Unexpected client cancellation");
    await fixture.Vm.RefreshAsync();
    Require(!fixture.Vm.CanCreate && fixture.Vm.SelectedServer is { IsConnected: false } && fixture.Vm.Infobases.Count == 0, "Canceled refresh left stale data and creation target enabled");
});

await Case("First activation automatically reads All servers without querying Sessions", async () =>
{
    var fixture = await Fixture.Create(p1, p2);
    fixture.Client.Data[p1.Id] = [Cluster("One", Base("Accounting"))];
    fixture.Client.Data[p2.Id] = [Cluster("Two", Base("Trade"))];
    await fixture.Vm.ActivateAsync();
    Require(fixture.Vm.ConnectionFilter?.Id is null && fixture.Vm.SelectedCluster!.IsAll && fixture.Vm.InfobaseFilter!.IsAll, "First activation changed All defaults");
    Require(fixture.Vm.Infobases.Count == 2 && fixture.Client.Reads[p1.Id] == 1 && fixture.Client.Reads[p2.Id] == 1, "First activation did not read both servers once");
    Require(fixture.SessionClient.ReadCount == 0, "Infobases activation also queried Sessions");
});

await Case("Return activation refreshes selected server and preserves cluster base search and auto refresh", async () =>
{
    var fixture = await TwoDuplicateServers();
    fixture.Vm.SelectServer(p2.Id);
    fixture.Vm.SelectedCluster = fixture.Vm.Clusters.Single(c => !c.IsAll);
    fixture.Vm.InfobaseFilter = fixture.Vm.InfobaseFilters.Single(f => !f.IsAll);
    fixture.Vm.SearchText = "Accounting";
    fixture.Vm.IsAutoRefreshEnabled = true;
    var firstServerReads = fixture.Client.Reads[p1.Id];
    var secondServerReads = fixture.Client.Reads[p2.Id];
    await fixture.Vm.ActivateAsync();
    Require(fixture.Vm.ConnectionFilter?.Id == p2.Id && fixture.Vm.SelectedCluster?.ServerId == p2.Id && fixture.Vm.InfobaseFilter?.ServerId == p2.Id, "Return reset the selected composite scope");
    Require(fixture.Vm.SearchText == "Accounting" && fixture.Vm.IsAutoRefreshEnabled, "Return reset search or auto refresh preference");
    Require(fixture.Client.Reads[p1.Id] == firstServerReads && fixture.Client.Reads[p2.Id] == secondServerReads + 1, "Return queried servers outside selected scope");
    Require(fixture.SessionClient.ReadCount == 0, "Return activation queried Sessions");
});

await Case("Concrete cluster return remembers and refreshes its owning server", async () =>
{
    var fixture = await TwoDuplicateServers();
    fixture.Vm.SelectedCluster = fixture.Vm.Clusters.Single(c => c.ServerId == p2.Id);
    var firstServerReads = fixture.Client.Reads[p1.Id];
    var secondServerReads = fixture.Client.Reads[p2.Id];
    await fixture.Vm.ActivateAsync();
    Require(fixture.Vm.ConnectionFilter?.Id == p2.Id && fixture.Vm.SelectedCluster?.ServerId == p2.Id, "Return lost concrete cluster owner selection");
    Require(fixture.Client.Reads[p1.Id] == firstServerReads && fixture.Client.Reads[p2.Id] == secondServerReads + 1, "Concrete cluster return queried another server");
});

await Case("All server and cluster return with concrete base refreshes only that base server", async () =>
{
    var fixture = await TwoDuplicateServers();
    fixture.Vm.InfobaseFilter = fixture.Vm.InfobaseFilters.Single(f => f.ServerId == p2.Id);
    var firstServerReads = fixture.Client.Reads[p1.Id];
    var secondServerReads = fixture.Client.Reads[p2.Id];
    await fixture.Vm.ActivateAsync();
    Require(fixture.Vm.ConnectionFilter?.Id is null && fixture.Vm.SelectedCluster!.IsAll && fixture.Vm.InfobaseFilter?.ServerId == p2.Id, "Return widened concrete base filter");
    Require(fixture.Client.Reads[p1.Id] == firstServerReads && fixture.Client.Reads[p2.Id] == secondServerReads + 1, "Concrete base return queried another server");
});

await Case("Failed filtered activation preserves selection and restores it on recovery", async () =>
{
    var fixture = await TwoDuplicateServers();
    fixture.Vm.SelectServer(p2.Id);
    fixture.Vm.SelectedCluster = fixture.Vm.Clusters.Single(c => !c.IsAll);
    fixture.Vm.InfobaseFilter = fixture.Vm.InfobaseFilters.Single(f => !f.IsAll);
    fixture.Vm.SearchText = "Accounting";
    fixture.Client.Errors[p2.Id] = new TimeoutException("Selected server unavailable");
    await fixture.Vm.ActivateAsync();
    Require(fixture.Vm.SelectedCluster?.ServerId == p2.Id && fixture.Vm.InfobaseFilter?.ServerId == p2.Id, "Failure reset desired cluster/base selection");
    Require(fixture.Vm.Infobases.Count == 0 && !fixture.Vm.CanCreate && fixture.Vm.HasConnectionError, "Failed filtered scope exposed stale data or creation");
    fixture.Client.Errors.Remove(p2.Id);
    await fixture.Vm.ActivateAsync();
    Require(fixture.Vm.SelectedCluster?.ServerId == p2.Id && fixture.Vm.InfobaseFilter?.ServerId == p2.Id, "Recovery lost the desired cluster/base selection");
    Require(fixture.Vm.Infobases.Count == 1 && fixture.Vm.Infobases[0].Profile.Id == p2.Id && fixture.Vm.SearchText == "Accounting" && !fixture.Vm.HasConnectionError, "Recovered scope did not restore data and search");
});

await Case("Unreadable selected cluster keeps base filter and restores it on recovery", async () =>
{
    var fixture = await TwoDuplicateServers();
    fixture.Vm.SelectServer(p2.Id);
    fixture.Vm.SelectedCluster = fixture.Vm.Clusters.Single(c => !c.IsAll);
    fixture.Vm.InfobaseFilter = fixture.Vm.InfobaseFilters.Single(f => !f.IsAll);
    fixture.Client.Data[p2.Id] = [Cluster("Two") with { DetailsMessage = "Database summary inaccessible" }];
    await fixture.Vm.ActivateAsync();
    Require(fixture.Vm.InfobaseFilter?.ServerId == p2.Id && fixture.Vm.HasConnectionError && !fixture.Vm.CanCreate, "Partial cluster failure reset selected base or allowed creation");
    fixture.Client.Data[p2.Id] = [Cluster("Two", Base("Accounting"))];
    await fixture.Vm.ActivateAsync();
    Require(fixture.Vm.InfobaseFilter?.ServerId == p2.Id && fixture.Vm.Infobases.Count == 1 && !fixture.Vm.HasConnectionError, "Cluster recovery lost desired base filter");
});

await Case("Successful missing base resets to All without retaining a fake failed option", async () =>
{
    var fixture = await TwoDuplicateServers();
    fixture.Vm.SelectServer(p2.Id);
    fixture.Vm.SelectedCluster = fixture.Vm.Clusters.Single(c => !c.IsAll);
    fixture.Vm.InfobaseFilter = fixture.Vm.InfobaseFilters.Single(f => !f.IsAll);
    fixture.Client.Data[p2.Id] = [Cluster("Two")];
    await fixture.Vm.ActivateAsync();
    Require(fixture.Vm.SelectedCluster?.ServerId == p2.Id && fixture.Vm.InfobaseFilter!.IsAll && fixture.Vm.InfobaseFilters.Count == 1, "Successful absence kept a stale base option");
    Require(!fixture.Vm.HasConnectionError && fixture.Vm.CanCreate, "Successful empty cluster was reported as a failure");
});

await Case("Successful missing cluster resets to All and disables creation", async () =>
{
    var fixture = await TwoDuplicateServers();
    fixture.Vm.SelectServer(p2.Id);
    fixture.Vm.SelectedCluster = fixture.Vm.Clusters.Single(c => !c.IsAll);
    fixture.Vm.InfobaseFilter = fixture.Vm.InfobaseFilters.Single(f => !f.IsAll);
    fixture.Client.Data[p2.Id] = [];
    await fixture.Vm.ActivateAsync();
    Require(fixture.Vm.SelectedCluster!.IsAll && fixture.Vm.InfobaseFilter!.IsAll && fixture.Vm.Clusters.Count == 1, "Successful missing cluster kept a stale concrete selection");
    Require(!fixture.Vm.HasConnectionError && !fixture.Vm.CanCreate, "Missing cluster retained a creation target");
});

await Case("Repeated activation during pending read coalesces into at most one queued read", async () =>
{
    var fixture = await Fixture.Create(p1);
    var firstPending = new TaskCompletionSource<IReadOnlyList<OneCClusterInfo>>(TaskCreationOptions.RunContinuationsAsynchronously);
    var secondPending = new TaskCompletionSource<IReadOnlyList<OneCClusterInfo>>(TaskCreationOptions.RunContinuationsAsynchronously);
    var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var calls = 0;
    fixture.Client.ReadOverride = (_, _, _) =>
    {
        var call = Interlocked.Increment(ref calls);
        if (call == 1) return firstPending.Task;
        if (call == 2) { secondStarted.TrySetResult(); return secondPending.Task; }
        return Task.FromResult<IReadOnlyList<OneCClusterInfo>>([Cluster("One", Base("Accounting"))]);
    };
    var activation = fixture.Vm.ActivateAsync();
    var secondActivation = fixture.Vm.ActivateAsync();
    var thirdActivation = fixture.Vm.ActivateAsync();
    try
    {
        Require(ReferenceEquals(activation, secondActivation) && ReferenceEquals(activation, thirdActivation), "Repeated activation did not share its pending task");
        Require(fixture.Vm.IsBusy && calls == 1, "Repeated activation started simultaneous reads or unlocked busy state");
        firstPending.TrySetResult([Cluster("One", Base("Accounting"))]);
        await secondStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Require(fixture.Vm.IsBusy && calls == 2 && !activation.IsCompleted, "Queued activation did not retain one busy read");
    }
    finally
    {
        firstPending.TrySetResult([Cluster("One", Base("Accounting"))]);
        secondPending.TrySetResult([Cluster("One", Base("Accounting"))]);
        await activation.WaitAsync(TimeSpan.FromSeconds(5));
    }
    Require(calls == 2 && !fixture.Vm.IsBusy && fixture.Vm.Infobases.Count == 1, "Repeated activation produced more than one queued read");
});

await Case("Infobases activation waits for pending Sessions owner work without extra Sessions read", async () =>
{
    var fixture = await Fixture.Create(p1);
    fixture.Client.Data[p1.Id] = [Cluster("One", Base("Accounting"))];
    var pending = new TaskCompletionSource<OneCServerSessionSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
    fixture.SessionClient.ReadOverride = (_, _, _) => pending.Task;
    var sessionsActivation = fixture.Owner.ActivateAsync();
    var infobasesActivation = fixture.Vm.ActivateAsync();
    try
    {
        Require(fixture.Owner.IsBusy && !infobasesActivation.IsCompleted && fixture.Client.Reads.Count == 0, "Infobases did not wait for pending owner work");
        Require(fixture.SessionClient.ReadCount == 1, "Unexpected additional session reads during owner wait");
    }
    finally
    {
        pending.TrySetResult(new([], DateTimeOffset.Now));
        await sessionsActivation.WaitAsync(TimeSpan.FromSeconds(5));
        await infobasesActivation.WaitAsync(TimeSpan.FromSeconds(5));
    }
    Require(fixture.Client.Reads[p1.Id] == 1 && fixture.Vm.Infobases.Count == 1 && fixture.SessionClient.ReadCount == 1, "Activation after owner work was lost or queried Sessions again");
});

await Case("Queued return activation reads the latest selected server scope", async () =>
{
    var fixture = await Fixture.Create(p1, p2);
    var pending = new TaskCompletionSource<IReadOnlyList<OneCClusterInfo>>(TaskCreationOptions.RunContinuationsAsynchronously);
    fixture.Client.ReadOverride = (profile, _, _) => fixture.Client.Reads[profile.Id] == 1
        ? pending.Task : Task.FromResult<IReadOnlyList<OneCClusterInfo>>([Cluster("Two", Base("Accounting"))]);
    var activation = fixture.Vm.ActivateAsync();
    fixture.Vm.SelectServer(p2.Id);
    var returnActivation = fixture.Vm.ActivateAsync();
    pending.TrySetResult([Cluster("First", Base("Accounting"))]);
    await Task.WhenAll(activation, returnActivation).WaitAsync(TimeSpan.FromSeconds(5));
    Require(fixture.Client.Reads[p1.Id] == 1 && fixture.Client.Reads[p2.Id] == 2, "Queued return reread the previous All scope instead of the latest server");
    Require(fixture.Vm.ConnectionFilter?.Id == p2.Id && fixture.Vm.Infobases.All(row => row.Profile.Id == p2.Id), "Queued return reset latest selected server");
});

await Case("Infobases activation is not lost between coalesced owner session reads", async () =>
{
    var fixture = await Fixture.Create(p1);
    fixture.Client.Data[p1.Id] = [Cluster("One", Base("Accounting"))];
    var firstPending = new TaskCompletionSource<OneCServerSessionSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
    var secondPending = new TaskCompletionSource<OneCServerSessionSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
    var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    fixture.SessionClient.ReadOverride = (_, _, _) =>
    {
        if (fixture.SessionClient.ReadCount == 1) return firstPending.Task;
        secondStarted.TrySetResult();
        return secondPending.Task;
    };
    var ownerActivation = fixture.Owner.ActivateAsync();
    var ownerReturnActivation = fixture.Owner.ActivateAsync();
    var infobasesActivation = fixture.Vm.ActivateAsync();
    try
    {
        firstPending.TrySetResult(new([], DateTimeOffset.Now));
        await secondStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }
    finally
    {
        firstPending.TrySetResult(new([], DateTimeOffset.Now));
        secondPending.TrySetResult(new([], DateTimeOffset.Now));
        await Task.WhenAll(ownerActivation, ownerReturnActivation, infobasesActivation).WaitAsync(TimeSpan.FromSeconds(5));
    }
    Require(fixture.Client.Reads.GetValueOrDefault(p1.Id) == 1 && fixture.Vm.Infobases.Count == 1, "Owner reactivation caused Infobases automatic load to be skipped");
    Require(fixture.SessionClient.ReadCount == 2, "Infobases caused extra session reads while waiting for owner reactivation");
});

await Case("Activation during manual Infobases refresh waits and then reads the latest scope", async () =>
{
    var fixture = await Fixture.Create(p1, p2);
    fixture.Vm.SelectServer(p1.Id);
    var pending = new TaskCompletionSource<IReadOnlyList<OneCClusterInfo>>(TaskCreationOptions.RunContinuationsAsynchronously);
    fixture.Client.ReadOverride = (profile, _, _) => profile.Id == p1.Id ? pending.Task
        : Task.FromResult<IReadOnlyList<OneCClusterInfo>>([Cluster("Two", Base("Accounting"))]);
    var refresh = fixture.Vm.RefreshAsync();
    fixture.Vm.SelectServer(p2.Id);
    var activation = fixture.Vm.ActivateAsync();
    try { Require(!activation.IsCompleted && fixture.Vm.IsBusy && !fixture.Client.Reads.ContainsKey(p2.Id), "Activation did not wait for manual pending refresh"); }
    finally
    {
        pending.TrySetResult([Cluster("One", Base("Accounting"))]);
        await Task.WhenAll(refresh, activation).WaitAsync(TimeSpan.FromSeconds(5));
    }
    Require(fixture.Client.Reads[p1.Id] == 1 && fixture.Client.Reads[p2.Id] == 1 && fixture.Vm.ConnectionFilter?.Id == p2.Id,
        "Activation during manual refresh was lost or used the previous scope");
    Require(fixture.Vm.Infobases.Count == 1 && fixture.Vm.Infobases[0].Profile.Id == p2.Id, "Latest scope data did not appear after pending manual refresh");
});

await Case("First metadata load shared by Sessions and Infobases does not lose either activation", async () =>
{
    var fixture = Fixture.CreateUnloaded(p1);
    fixture.Client.Data[p1.Id] = [Cluster("One", Base("Accounting"))];
    var pending = new TaskCompletionSource<IReadOnlyList<OneCServerConnectionProfile>>(TaskCreationOptions.RunContinuationsAsynchronously);
    fixture.Store.LoadOverride = _ => pending.Task;
    var sessionActivation = fixture.Owner.ActivateAsync();
    var infobaseActivation = fixture.Vm.ActivateAsync();
    try
    {
        Require(fixture.Owner.IsBusy && !fixture.Owner.IsLoaded && !infobaseActivation.IsCompleted, "Infobases did not wait for the first pending profile load");
        Require(fixture.Client.Reads.Count == 0 && fixture.SessionClient.ReadCount == 0, "Server queries ran before profile initialization completed");
    }
    finally
    {
        pending.TrySetResult([p1]);
        await Task.WhenAll(sessionActivation, infobaseActivation).WaitAsync(TimeSpan.FromSeconds(5));
    }
    Require(fixture.Store.LoadCount == 1, "Page activations duplicated metadata load");
    Require(fixture.SessionClient.ReadCount == 1 && fixture.Client.Reads.GetValueOrDefault(p1.Id) == 1 && fixture.Vm.Infobases.Count == 1,
        "An activation was lost during shared profile initialization");
});

await Case("Server with multiple confirmed clusters keeps All until manual selection", async () =>
{
    var fixture = await Fixture.Create(p1, p2);
    var secondUuid = Guid.NewGuid().ToString();
    fixture.Client.Data[p1.Id] = [Cluster("One", Base("Accounting"))];
    fixture.Client.Data[p2.Id] = [Cluster("Two", Base("Accounting")), Cluster("Other", Base("Trade")) with { Uuid = secondUuid }];
    await fixture.Vm.ActivateAsync();
    fixture.Vm.SelectServer(p2.Id);
    Require(fixture.Vm.SelectedCluster!.IsAll && !fixture.Vm.CanCreate, "Multiple clusters automatically selected a creation target");
    fixture.Vm.SelectedCluster = fixture.Vm.Clusters.Single(c => c.ClusterUuid == secondUuid);
    Require(fixture.Vm.SelectedServer?.Profile.Id == p2.Id && fixture.Vm.CreationCluster?.Uuid == secondUuid && fixture.Vm.CanCreate, "Manual cluster selection did not resolve its exact target");
});

await Case("Explicit All cluster on a selected server survives refresh and activation", async () =>
{
    var fixture = await TwoDuplicateServers();
    fixture.Vm.SelectServer(p2.Id);
    Require(!fixture.Vm.SelectedCluster!.IsAll, "Sole cluster was not selected initially");
    fixture.Vm.SelectedCluster = ServerClusterFilter.All;
    await fixture.Vm.RefreshAsync();
    await fixture.Vm.ActivateAsync();
    Require(fixture.Vm.SelectedServer?.Profile.Id == p2.Id && fixture.Vm.SelectedCluster!.IsAll && !fixture.Vm.CanCreate, "Refresh overwrote explicitly selected All cluster");
});

await Case("Deferred unique cluster selection requires a successful current snapshot", async () =>
{
    var fixture = await Fixture.Create(p1, p2);
    fixture.Vm.SelectServer(p2.Id);
    fixture.Client.Errors[p2.Id] = new TimeoutException("No confirmed cluster list");
    await fixture.Vm.RefreshAsync();
    Require(fixture.Vm.SelectedCluster!.IsAll && !fixture.Vm.CanCreate, "Full read failure created an unconfirmed cluster target");
    fixture.Client.Errors.Remove(p2.Id);
    fixture.Client.Data[p2.Id] = [Cluster("Two", Base("Accounting"))];
    await fixture.Vm.RefreshAsync();
    Require(fixture.Vm.SelectedCluster?.ServerId == p2.Id && fixture.Vm.CanCreate, "Deferred intent did not select the sole cluster after confirmed recovery");
});

await Case("Pending auto selection follows current server and ignores an older server result", async () =>
{
    var fixture = await Fixture.Create(p1, p2);
    var pending = new TaskCompletionSource<IReadOnlyList<OneCClusterInfo>>(TaskCreationOptions.RunContinuationsAsynchronously);
    fixture.Client.ReadOverride = (profile, _, _) => profile.Id == p1.Id ? pending.Task
        : Task.FromResult<IReadOnlyList<OneCClusterInfo>>([Cluster("Two", Base("Accounting"))]);
    fixture.Vm.SelectServer(p1.Id);
    var read = fixture.Vm.RefreshAsync();
    fixture.Vm.SelectServer(p2.Id);
    Require(fixture.Vm.SelectedCluster!.IsAll && !fixture.Vm.CanCreate, "Pending read exposed an unconfirmed target");
    pending.TrySetResult([Cluster("One", Base("Accounting"))]);
    await read.WaitAsync(TimeSpan.FromSeconds(5));
    Require(fixture.Vm.SelectedServer?.Profile.Id == p2.Id && fixture.Vm.SelectedCluster!.IsAll && !fixture.Vm.CanCreate, "Old server result fulfilled another server's auto selection intent");
    await fixture.Vm.RefreshAsync();
    Require(fixture.Vm.SelectedCluster?.ServerId == p2.Id && fixture.Vm.CreationCluster?.Host == "two", "Current server intent was lost after older read completed");
});

await Case("Confirmed sole cluster with unreadable details is selected but cannot create", async () =>
{
    var fixture = await Fixture.Create(p1);
    fixture.Client.Data[p1.Id] = [Cluster("One") with { DetailsMessage = "Base summary unavailable" }];
    await fixture.Vm.ActivateAsync();
    fixture.Vm.SelectServer(p1.Id);
    Require(fixture.Vm.SelectedCluster?.ServerId == p1.Id && !fixture.Vm.CanCreate && fixture.Vm.HasConnectionError,
        "Confirmed cluster metadata or its access failure was treated incorrectly");
});

await Case("Server cluster synchronization completes without selector reentry loops", async () =>
{
    var fixture = await TwoDuplicateServers();
    var cluster1 = fixture.Vm.Clusters.Single(c => c.ServerId == p1.Id);
    var cluster2 = fixture.Vm.Clusters.Single(c => c.ServerId == p2.Id);
    var notifications = 0;
    fixture.Vm.PropertyChanged += (_, e) =>
    {
        if (e.PropertyName is nameof(InfobasesPageViewModel.ConnectionFilter) or nameof(InfobasesPageViewModel.SelectedServer) or nameof(InfobasesPageViewModel.SelectedCluster))
            Require(++notifications < 30, "Selectors recursively changed each other");
    };
    fixture.Vm.SelectedCluster = cluster2;
    Require(fixture.Vm.SelectedServer?.Profile.Id == p2.Id, "First cluster did not select parent");
    fixture.Vm.SelectedCluster = cluster1;
    Require(fixture.Vm.SelectedServer?.Profile.Id == p1.Id && fixture.Vm.SelectedCluster?.ServerId == p1.Id, "Second cluster retained previous parent");
    fixture.Vm.SelectServer(p2.Id);
    Require(fixture.Vm.SelectedCluster?.ServerId == p2.Id && fixture.Vm.CanCreate, "Parent change failed to select its unique child");
});

foreach (var result in results) Console.WriteLine($"{(result.Passed ? "PASS" : "FAIL")} {result.Name}{(result.Error is null ? "" : ": " + result.Error)}");
Console.WriteLine($"Total: {results.Count}; passed: {results.Count(r => r.Passed)}; failed: {results.Count(r => !r.Passed)}");
File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "state-check-results.json"), JsonSerializer.Serialize(results.Select(r => new { r.Name, r.Passed, r.Error }), new JsonSerializerOptions { WriteIndented = true }));
return results.All(r => r.Passed) ? 0 : 1;

async Task Case(string name, Func<Task> test)
{
    try { await test(); results.Add((name, true, null)); }
    catch (Exception exception) { results.Add((name, false, exception.Message)); }
}

async Task<Fixture> TwoDuplicateServers()
{
    var fixture = await Fixture.Create(p1, p2);
    fixture.Client.Data[p1.Id] = [Cluster("One", Base("Accounting"))];
    fixture.Client.Data[p2.Id] = [Cluster("Two", Base("Accounting"))];
    await fixture.Vm.ConnectAllCommand.ExecuteAsync(null);
    return fixture;
}

OneCClusterInfo Cluster(string name, params OneCInfobaseSummaryInfo[] bases) => new()
{
    Uuid = sharedCluster, Name = name, Host = name.ToLowerInvariant(), Port = 1541, Infobases = bases
};
OneCInfobaseSummaryInfo Base(string name, string? uuid = null) => new() { Uuid = uuid ?? sharedBase, Name = name };
static OneCServerConnectionProfile Profile(string host) => new()
{
    Name = host, Host = host, AgentPort = 1540, PlatformVersion = "8.3.27.2170", PlatformDirectory = @"C:\platform\bin"
};
static OneCServiceInfo Service(string state) => new()
{
    Name = "local-agent", DisplayName = "Local agent", Kind = OneCServiceKind.ServerAgent, State = state, Status = "OK",
    Version = "8.3.27.2170", AgentPort = 1540, ExecutablePath = @"C:\platform\8.3.27.2170\bin\ragent.exe"
};
static void Require(bool condition, string error) { if (!condition) throw new InvalidOperationException(error); }

sealed class Fixture
{
    public FakeInfobaseClient Client { get; } = new();
    public FakeSessionClient SessionClient { get; } = new();
    public FakeInventory Inventory { get; }
    public FakeStore Store { get; }
    public SessionsPageViewModel Owner { get; }
    public InfobasesPageViewModel Vm { get; }
    private Fixture(OneCServerConnectionProfile[] profiles, OneCServiceInfo[] services)
    {
        Inventory = new() { Services = services };
        Store = new(profiles);
        Owner = new(SessionClient, Store, new(Inventory, "TEST-MACHINE"));
        Vm = new(Client, Owner);
    }
    public static Task<Fixture> Create(params OneCServerConnectionProfile[] profiles) => Create(profiles, []);
    public static Fixture CreateUnloaded(params OneCServerConnectionProfile[] profiles) => new(profiles, []);
    public static async Task<Fixture> Create(OneCServerConnectionProfile[] profiles, OneCServiceInfo[] services)
    {
        var fixture = new Fixture(profiles, services);
        await fixture.Vm.LoadAsync();
        return fixture;
    }
}

sealed class FakeInventory : IOneCServiceInventory
{
    public IReadOnlyList<OneCServiceInfo> Services { get; set; } = [];
    public Task<IReadOnlyList<OneCServiceInfo>> GetServicesAsync(CancellationToken cancellationToken = default) => Task.FromResult(Services);
}
sealed class FakeStore(OneCServerConnectionProfile[] profiles) : IOneCServerConnectionStore
{
    private IReadOnlyList<OneCServerConnectionProfile> saved = profiles;
    private int loadCount;
    public int LoadCount => Volatile.Read(ref loadCount);
    public Func<CancellationToken, Task<IReadOnlyList<OneCServerConnectionProfile>>>? LoadOverride { get; set; }
    public Task<IReadOnlyList<OneCServerConnectionProfile>> LoadAsync(CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref loadCount);
        return LoadOverride?.Invoke(cancellationToken) ?? Task.FromResult(saved);
    }
    public Task SaveAsync(IReadOnlyList<OneCServerConnectionProfile> value, CancellationToken cancellationToken = default) { saved = value; return Task.CompletedTask; }
}
sealed class FakeSessionClient : IOneCServerSessionClient
{
    private int readCount;
    public int ReadCount => Volatile.Read(ref readCount);
    public Func<OneCServerConnectionProfile, string?, CancellationToken, Task<OneCServerSessionSnapshot>>? ReadOverride { get; set; }
    public Task<OneCServerSessionSnapshot> ReadAsync(OneCServerConnectionProfile profile, string? password, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref readCount);
        return ReadOverride?.Invoke(profile, password, cancellationToken) ?? Task.FromResult(new OneCServerSessionSnapshot([], DateTimeOffset.Now));
    }
    public Task<IReadOnlyList<OneCSessionTerminationResult>> TerminateAsync(OneCServerConnectionProfile profile, string? password,
        IReadOnlyList<OneCSessionTarget> targets, string message, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Unexpected mutating call in state audit");
}
sealed class FakeInfobaseClient : IOneCInfobaseClient
{
    public Dictionary<Guid, IReadOnlyList<OneCClusterInfo>> Data { get; } = [];
    public Dictionary<Guid, Exception> Errors { get; } = [];
    public Dictionary<Guid, int> Reads { get; } = [];
    public Func<OneCServerConnectionProfile, string?, CancellationToken, Task<IReadOnlyList<OneCClusterInfo>>>? ReadOverride { get; set; }
    public Task<IReadOnlyList<OneCClusterInfo>> ReadAsync(OneCServerConnectionProfile profile, string? password, CancellationToken cancellationToken = default)
    {
        Reads[profile.Id] = Reads.GetValueOrDefault(profile.Id) + 1;
        if (ReadOverride is { } read) return read(profile, password, cancellationToken);
        return Errors.TryGetValue(profile.Id, out var error) ? Task.FromException<IReadOnlyList<OneCClusterInfo>>(error)
            : Task.FromResult(Data.GetValueOrDefault(profile.Id) ?? (IReadOnlyList<OneCClusterInfo>)[]);
    }
    public Task<string> CreateAsync(OneCServerConnectionProfile profile, OneCInfobaseCreationTarget target,
        OneCEmptyInfobaseOptions options, string? clusterPassword, string? databasePassword, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Unexpected mutating call in state audit");
}
