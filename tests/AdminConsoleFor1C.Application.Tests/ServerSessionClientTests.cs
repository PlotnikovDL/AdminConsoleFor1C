using AdminConsoleFor1C.Core.Administration;
using AdminConsoleFor1C.Infrastructure.Services;

namespace AdminConsoleFor1C.Application.Tests;

public sealed class ServerSessionClientTests
{
    private const string Cluster = "11111111-1111-1111-1111-111111111111";
    private const string Session = "22222222-2222-2222-2222-222222222222";
    private const string Infobase = "33333333-3333-3333-3333-333333333333";
    private static OneCServerConnectionProfile Profile(string version = "8.3.27.2170") => new()
    { Name = "Server", Host = "server.test", PlatformVersion = version, PlatformDirectory = @"C:\1C\" + version, ClusterUser = "Admin" };
    private static string SessionOutput(string user = "Иван") => $"session : {Session}\ninfobase : {Infobase}\nuser-name : \"{user}\"\nhost : PC\napp-id : 1CV8C\nstarted-at : 2026-01-01T12:00:00\n";
    private static OneCSessionTarget Target(OneCServerConnectionProfile profile) => new(profile.Id, Cluster,
        OneCSessionInfo.FromProperties(OneCRacOutputParser.ParseObjects(SessionOutput())[0]));

    [Fact]
    public async Task KeepsConcurrentConnectionsOnTheirOwnPlatformAndClosesEachBridge()
    {
        var factory = new FakeFactory(args => args[0] == "cluster" ? $"cluster : {Cluster}\nname : \"Cluster\"\n"
            : args[0] == "infobase" ? $"infobase : {Infobase}\nname : \"База\"\n" : SessionOutput());
        var client = new RacServerSessionClient(factory);
        var profiles = new[] { Profile(), Profile("8.5.1.1343") };
        var snapshots = await Task.WhenAll(profiles.Select(profile => client.ReadAsync(profile, "test-only-password")));
        Assert.All(snapshots, snapshot => Assert.Single(Assert.Single(snapshot.Clusters).Sessions));
        Assert.Equal(profiles.Select(p => p.PlatformDirectory), factory.Opened.Select(p => p.PlatformDirectory));
        Assert.All(factory.Sessions, session => Assert.True(session.Disposed));
        Assert.All(factory.Sessions, session => Assert.Equal(new[] { "agent", "version" }, session.Commands[0]));
        Assert.All(factory.Sessions, session => Assert.Contains(session.Commands, args => args.Contains("--cluster-user=Admin")));
    }

    [Fact]
    public async Task TerminatesOnlyExplicitConfirmedSessionOnce()
    {
        var factory = new FakeFactory(args => args.Contains("info") ? SessionOutput() : "");
        var client = new RacServerSessionClient(factory);
        var profile = Profile();
        var results = await client.TerminateAsync(profile, "test-only", [Target(profile), Target(profile)], "Причина с пробелами");
        Assert.True(Assert.Single(results).Success);
        var commands = Assert.Single(factory.Sessions).Commands;
        Assert.Equal(3, commands.Count);
        Assert.Equal(new[] { "agent", "version" }, commands[0]);
        Assert.Contains("info", commands[1]);
        Assert.Contains("terminate", commands[2]);
        Assert.Contains("--session=" + Session, commands[2]);
        Assert.Contains("--cluster=" + Cluster, commands[2]);
        Assert.Contains("--error-message=Причина с пробелами", commands[2]);
    }

    [Theory]
    [InlineData("version : 8.5.1.1343")]
    [InlineData("unrecognized")]
    [InlineData("")]
    public async Task RejectsDifferentOrUnknownAgentVersionBeforeReadingSessions(string version)
    {
        var factory = new FakeFactory(_ => throw new InvalidOperationException("Session data must not be requested."), _ => version);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new RacServerSessionClient(factory).ReadAsync(Profile(), null));
        var session = Assert.Single(factory.Sessions);
        Assert.Equal(new[] { "agent", "version" }, Assert.Single(session.Commands));
        Assert.True(session.Disposed);
    }

    [Theory]
    [InlineData("version : 8.5.1.1343")]
    [InlineData("unrecognized")]
    [InlineData("")]
    public async Task RejectsDifferentOrUnknownAgentVersionBeforeTerminatingSessions(string version)
    {
        var factory = new FakeFactory(_ => throw new InvalidOperationException("Session commands must not be sent."), _ => version);
        var profile = Profile();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new RacServerSessionClient(factory)
            .TerminateAsync(profile, null, [Target(profile)], "Stop"));
        var session = Assert.Single(factory.Sessions);
        Assert.Equal(new[] { "agent", "version" }, Assert.Single(session.Commands));
        Assert.True(session.Disposed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AgentVersionFailureRedactsPasswordAndClosesBridge(bool terminate)
    {
        const string secret = "version-check-test-secret";
        var factory = new FakeFactory(_ => "", _ => throw new InvalidOperationException("Agent version failed: " + secret));
        var profile = Profile();
        var client = new RacServerSessionClient(factory);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            if (terminate) await client.TerminateAsync(profile, secret, [Target(profile)], "Stop");
            else await client.ReadAsync(profile, secret);
        });
        Assert.Contains("Agent version failed", error.Message);
        Assert.DoesNotContain(secret, error.ToString());
        Assert.Single(Assert.Single(factory.Sessions).Commands);
        Assert.True(factory.Sessions[0].Disposed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AgentVersionCancellationStopsBeforeSessionCommandsAndClosesBridge(bool terminate)
    {
        var factory = new FakeFactory(_ => "", _ => throw new OperationCanceledException());
        var profile = Profile();
        var client = new RacServerSessionClient(factory);
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            if (terminate) await client.TerminateAsync(profile, null, [Target(profile)], "Stop");
            else await client.ReadAsync(profile, null);
        });
        Assert.Single(Assert.Single(factory.Sessions).Commands);
        Assert.True(factory.Sessions[0].Disposed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Другой пользователь")]
    public async Task DoesNotTerminateMissingOrChangedSession(string currentUser)
    {
        var factory = new FakeFactory(_ => currentUser == "" ? "" : SessionOutput(currentUser));
        var profile = Profile();
        var result = await new RacServerSessionClient(factory).TerminateAsync(profile, null, [Target(profile)], "Stop");
        Assert.False(Assert.Single(result).Success);
        Assert.DoesNotContain(Assert.Single(factory.Sessions).Commands, args => args.Contains("terminate"));
    }

    [Fact]
    public async Task RejectsTargetsFromAnotherConnectionBeforeOpeningBridge()
    {
        var factory = new FakeFactory(_ => "");
        await Assert.ThrowsAsync<ArgumentException>(() => new RacServerSessionClient(factory)
            .TerminateAsync(Profile(), null, [Target(Profile())], "Stop"));
        Assert.Empty(factory.Opened);
    }

    [Fact]
    public async Task AuthenticationFailureIsVisibleAndRedactsPassword()
    {
        const string secret = "test-only-secret";
        var factory = new FakeFactory(args => args[0] == "cluster" ? $"cluster : {Cluster}\n"
            : throw new InvalidOperationException("Authentication failed: " + secret));
        var result = await new RacServerSessionClient(factory).ReadAsync(Profile(), secret);
        var cluster = Assert.Single(result.Clusters);
        Assert.Empty(cluster.Sessions);
        Assert.Contains("Authentication failed", cluster.DetailsMessage);
        Assert.DoesNotContain(secret, cluster.DetailsMessage);
        Assert.True(Assert.Single(factory.Sessions).Disposed);
    }

    [Fact]
    public async Task ConnectionStoreRoundTripsProfilesAndRejectsCorruptFile()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "connections-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new JsonOneCServerConnectionStore(path);
            var profile = Profile();
            await store.SaveAsync([profile]);
            Assert.Equal(profile, Assert.Single(await store.LoadAsync()));
            Assert.DoesNotContain("password", await File.ReadAllTextAsync(path), StringComparison.OrdinalIgnoreCase);
            await File.WriteAllTextAsync(path, "corrupt data");
            await Assert.ThrowsAsync<System.Text.Json.JsonException>(() => store.LoadAsync());
            Assert.Equal("corrupt data", await File.ReadAllTextAsync(path));
        }
        finally { File.Delete(path); }
    }

    private sealed class FakeFactory(Func<IReadOnlyList<string>, string> response,
        Func<OneCServerConnectionProfile, string>? versionResponse = null) : IOneCRasSessionFactory
    {
        public List<OneCServerConnectionProfile> Opened { get; } = [];
        public List<FakeSession> Sessions { get; } = [];
        public Task<IOneCRasSession> OpenAsync(OneCServerConnectionProfile profile, CancellationToken token)
        {
            Opened.Add(profile);
            var session = new FakeSession(args => args.Count == 2 && args[0] == "agent" && args[1] == "version"
                ? versionResponse?.Invoke(profile) ?? $"version : {profile.PlatformVersion}\n" : response(args));
            Sessions.Add(session);
            return Task.FromResult<IOneCRasSession>(session);
        }
    }
    private sealed class FakeSession(Func<IReadOnlyList<string>, string> response) : IOneCRasSession
    {
        public List<IReadOnlyList<string>> Commands { get; } = [];
        public bool Disposed { get; private set; }
        public Task<string> RunAsync(IReadOnlyList<string> arguments, CancellationToken token)
        { Commands.Add(arguments); return Task.FromResult(response(arguments)); }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
}
