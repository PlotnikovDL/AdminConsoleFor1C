using System.Collections.Concurrent;
using System.Text.Json;
using AdminConsoleFor1C.Application.Services;
using AdminConsoleFor1C.Core.Administration;

namespace AdminConsoleFor1C.Application.Tests;

public sealed class OneCInfobaseInventoryReaderTests
{
    private const string ClusterUuid = "11111111-1111-1111-1111-111111111111";
    private const string InfobaseUuid = "22222222-2222-2222-2222-222222222222";
    private static OneCServerConnectionProfile Profile(int server) => new()
    {
        Name = "Сервер", Host = $"server{server}.test", PlatformVersion = "8.3.27.2170",
        PlatformDirectory = @"C:\1C\8.3.27.2170"
    };
    private static OneCClusterInfo Cluster() => new()
    {
        Uuid = ClusterUuid, Name = "Кластер", Host = "cluster.test", Port = 1541,
        Infobases = [new() { Uuid = InfobaseUuid, Name = "База", ClusterUuid = ClusterUuid }]
    };

    [Fact]
    public async Task RetainsSeparateServerIdentityForIdenticalClusterAndInfobaseIdsAndNames()
    {
        var profiles = new[] { Profile(1), Profile(2) };
        var client = new FakeClient((_, _, _) => Task.FromResult<IReadOnlyList<OneCClusterInfo>>([Cluster()]));
        var before = DateTimeOffset.Now;
        var result = await new OneCInfobaseInventoryReader(client).ReadAsync(profiles, _ => null);
        var after = DateTimeOffset.Now;
        Assert.Equal(profiles.Select(p => p.Id), result.Select(r => r.Profile.Id));
        Assert.Same(profiles[0], result[0].Profile);
        Assert.Same(profiles[1], result[1].Profile);
        Assert.Equal(2, result.SelectMany(r => r.Clusters.SelectMany(c => c.Infobases)).Count());
        Assert.All(result, server =>
        {
            Assert.Null(server.Error);
            var cluster = Assert.Single(server.Clusters);
            Assert.Equal(ClusterUuid, cluster.Uuid);
            Assert.Equal(InfobaseUuid, Assert.Single(cluster.Infobases).Uuid);
            Assert.InRange(server.UpdatedAt, before, after);
        });
    }

    [Fact]
    public async Task RetainsSuccessfulServersAndClusterAccessErrorsWhenOneServerFails()
    {
        var profiles = new[] { Profile(1), Profile(2), Profile(3) };
        var partialCluster = Cluster() with { DetailsMessage = "Недоступна часть данных кластера." };
        var client = new FakeClient((profile, _, _) => profile.Id == profiles[1].Id
            ? throw new InvalidOperationException("Сервер недоступен.")
            : Task.FromResult<IReadOnlyList<OneCClusterInfo>>([partialCluster]));
        var result = await new OneCInfobaseInventoryReader(client).ReadAsync(profiles, _ => null);
        Assert.Equal(profiles.Select(p => p.Id), result.Select(r => r.Profile.Id));
        Assert.Null(result[0].Error);
        Assert.Same(partialCluster, Assert.Single(result[0].Clusters));
        Assert.Equal("Сервер недоступен.", result[1].Error);
        Assert.Empty(result[1].Clusters);
        Assert.Null(result[2].Error);
        Assert.Same(partialCluster, Assert.Single(result[2].Clusters));
    }

    [Fact]
    public async Task RoutesEachPasswordToItsOwnProfileAndDoesNotIncludeCredentialsInResults()
    {
        var profiles = new[] { Profile(1), Profile(2), Profile(3) };
        var passwords = new Dictionary<Guid, string?>
        {
            [profiles[0].Id] = "server-one-test-password", [profiles[1].Id] = "server-two-test-password",
            [profiles[2].Id] = null
        };
        var requested = new ConcurrentBag<Guid>();
        var client = new FakeClient((profile, password, _) =>
        {
            Assert.Equal(passwords[profile.Id], password);
            requested.Add(profile.Id);
            return Task.FromResult<IReadOnlyList<OneCClusterInfo>>([Cluster()]);
        });
        var result = await new OneCInfobaseInventoryReader(client).ReadAsync(profiles, id => passwords[id]);
        Assert.Equal(profiles.Select(p => p.Id).Order(), requested.Order());
        Assert.All(result, server => Assert.Null(server.Error));
        var serialized = JsonSerializer.Serialize(result);
        Assert.DoesNotContain(passwords[profiles[0].Id]!, serialized);
        Assert.DoesNotContain(passwords[profiles[1].Id]!, serialized);
    }

    [Fact]
    public async Task RedactsPasswordFromIndividualServerFailure()
    {
        const string password = "failed-server-test-password";
        var profile = Profile(1);
        var client = new FakeClient((_, _, _) => throw new InvalidOperationException("Access denied: " + password));
        var result = Assert.Single(await new OneCInfobaseInventoryReader(client).ReadAsync([profile], _ => password));
        Assert.Same(profile, result.Profile);
        Assert.Equal("Access denied: ***", result.Error);
        Assert.DoesNotContain(password, result.ToString());
    }

    [Fact]
    public async Task ReadsAtMostThreeServersConcurrentlyAndPreservesInputOrder()
    {
        var profiles = Enumerable.Range(1, 7).Select(Profile).ToArray();
        var firstThreeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sync = new object();
        var active = 0;
        var maximum = 0;
        var started = 0;
        var client = new FakeClient(async (_, _, token) =>
        {
            lock (sync)
            {
                active++;
                maximum = Math.Max(maximum, active);
                if (++started == 3) firstThreeStarted.SetResult();
            }
            try { await release.Task.WaitAsync(token); return [Cluster()]; }
            finally { lock (sync) active--; }
        });
        var read = new OneCInfobaseInventoryReader(client).ReadAsync(profiles, _ => null);
        try
        {
            await firstThreeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            lock (sync) Assert.Equal(3, started);
            Assert.False(read.IsCompleted);
        }
        finally { release.TrySetResult(); }
        var result = await read.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(3, maximum);
        Assert.Equal(0, active);
        Assert.Equal(profiles.Select(p => p.Id), result.Select(r => r.Profile.Id));
        Assert.All(result, server => Assert.Null(server.Error));
    }

    [Fact]
    public async Task CancellationStopsActiveReadsAndDoesNotStartQueuedServers()
    {
        var profiles = Enumerable.Range(1, 8).Select(Profile).ToArray();
        using var cancellation = new CancellationTokenSource();
        var firstThreeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var neverCompletes = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = 0;
        var client = new FakeClient(async (_, _, token) =>
        {
            Assert.Equal(cancellation.Token, token);
            if (Interlocked.Increment(ref started) == 3) firstThreeStarted.SetResult();
            await neverCompletes.Task.WaitAsync(token);
            return [Cluster()];
        });
        var read = new OneCInfobaseInventoryReader(client).ReadAsync(profiles, _ => null, cancellation.Token);
        try { await firstThreeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
        finally { cancellation.Cancel(); }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(3, started);
    }

    [Fact]
    public async Task AlreadyCanceledReadDoesNotRequestCredentialsOrContactServers()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var client = new FakeClient((_, _, _) => throw new InvalidOperationException("No server should be contacted."));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new OneCInfobaseInventoryReader(client)
            .ReadAsync([Profile(1)], _ => throw new InvalidOperationException("No credentials should be requested."), cancellation.Token));
    }

    [Fact]
    public async Task UnrequestedClientCancellationIsIsolatedAndRedactsPassword()
    {
        const string password = "client-cancellation-test-password";
        var profiles = new[] { Profile(1), Profile(2) };
        var client = new FakeClient((profile, secret, _) => profile.Id == profiles[1].Id
            ? throw new OperationCanceledException("Request timed out: " + secret)
            : Task.FromResult<IReadOnlyList<OneCClusterInfo>>([Cluster()]));
        var result = await new OneCInfobaseInventoryReader(client).ReadAsync(profiles,
            id => id == profiles[1].Id ? password : null);
        Assert.Null(result[0].Error);
        Assert.Single(Assert.Single(result[0].Clusters).Infobases);
        Assert.Same(profiles[1], result[1].Profile);
        Assert.Equal("Request timed out: ***", result[1].Error);
        Assert.Empty(result[1].Clusters);
        Assert.DoesNotContain(password, JsonSerializer.Serialize(result));
    }

    private sealed class FakeClient(Func<OneCServerConnectionProfile, string?, CancellationToken,
        Task<IReadOnlyList<OneCClusterInfo>>> read) : IOneCInfobaseClient
    {
        public Task<IReadOnlyList<OneCClusterInfo>> ReadAsync(OneCServerConnectionProfile profile, string? password,
            CancellationToken cancellationToken = default) => read(profile, password, cancellationToken);

        public Task<string> CreateAsync(OneCServerConnectionProfile profile, OneCInfobaseCreationTarget target,
            OneCEmptyInfobaseOptions options, string? clusterPassword, string? databasePassword,
            CancellationToken cancellationToken = default) => throw new NotSupportedException("Inventory never creates infobases.");
    }
}
