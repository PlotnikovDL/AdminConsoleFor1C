using AdminConsoleFor1C.Application.Services;
using AdminConsoleFor1C.Core.Administration;
using AdminConsoleFor1C.Infrastructure.Services;

namespace AdminConsoleFor1C.Application.Tests;

public sealed class InfobaseClientTests
{
    private const string ClusterId = "11111111-1111-1111-1111-111111111111";
    private const string DatabaseId = "22222222-2222-2222-2222-222222222222";
    private static OneCServerConnectionProfile Profile(string version = "8.3.27.2170", int port = 1540) => new()
    { Name = "Сервер", Host = "remote.test", AgentPort = port, PlatformVersion = version, PlatformDirectory = @"C:\1C\" + version, ClusterUser = "Admin" };
    private static OneCClusterInfo Cluster => new() { Uuid = ClusterId, Host = "remote.test", Port = 1541 };
    private static OneCEmptyInfobaseOptions Options => new()
    { Name = "База тест", Dbms = "PostgreSQL", DbServer = "sql.test:5432", DbName = "test_db", DbUser = "db_admin" };
    private static string Response(IReadOnlyList<string> args) => args[0] switch
    {
        "agent" => "version : 8.3.27.2170\n",
        "cluster" => $"cluster : {ClusterId}\nhost : remote.test\nport : 1541\n",
        _ => args.Contains("create") ? $"infobase : {DatabaseId}\n" : ""
    };

    [Fact]
    public async Task CreatesOnExplicitRemoteProfileAndClusterWithExactArguments()
    {
        var factory = new FakeFactory(Response);
        var profile = Profile();
        var options = Options with { Description = "Текст с пробелами и \"кавычками\"", DateOffset = 2000, SecurityLevel = 2, ScheduledJobsDeny = true, LicenseDistribution = false };
        var id = await new RacInfobaseClient(factory).CreateAsync(profile, new(profile.Id, Cluster), options, "cluster-test", "db-test");
        Assert.Equal(DatabaseId, id);
        Assert.Same(profile, Assert.Single(factory.Opened));
        var session = Assert.Single(factory.Sessions);
        Assert.True(session.Disposed);
        Assert.Equal(4, session.Commands.Count);
        var command = session.Commands.Last();
        Assert.Contains("--cluster=" + ClusterId, command);
        Assert.Contains("--db-server=sql.test:5432", command);
        Assert.Contains("--name=База тест", command);
        Assert.Contains("--descr=" + options.Description, command);
        Assert.Contains("--cluster-user=Admin", command);
        Assert.Contains("--cluster-pwd=cluster-test", command);
        Assert.Contains("--db-pwd=db-test", command);
        Assert.Contains("--create-database", command);
        Assert.Contains("--date-offset=2000", command);
        Assert.Contains("--security-level=2", command);
        Assert.Contains("--scheduled-jobs-deny=on", command);
        Assert.Contains("--license-distribution=deny", command);
    }

    [Fact]
    public async Task UsesSelectedVersionAndNondefaultAgentPortWithoutCreatingDatabaseWhenUnchecked()
    {
        var profile = Profile("8.5.1.1343", 2540);
        var factory = new FakeFactory(args => args[0] == "agent" ? "8.5.1.1343\n" : Response(args));
        await new RacInfobaseClient(factory).CreateAsync(profile, new(profile.Id, Cluster), Options with { CreateDatabase = false }, null, null);
        Assert.Equal("remote.test:2540", Assert.Single(factory.Opened).AgentAddress);
        Assert.EndsWith("8.5.1.1343", factory.Opened[0].PlatformDirectory);
        Assert.DoesNotContain("--create-database", Assert.Single(factory.Sessions).Commands.Last());
    }

    [Theory]
    [InlineData("version : 8.5.1.1343")]
    [InlineData("unrecognized")]
    public async Task RejectsDifferentOrUnknownServerVersionBeforeMutation(string version)
    {
        var factory = new FakeFactory(args => args[0] == "agent" ? version : Response(args));
        var profile = Profile();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new RacInfobaseClient(factory)
            .CreateAsync(profile, new(profile.Id, Cluster), Options, null, null));
        Assert.Single(Assert.Single(factory.Sessions).Commands);
    }

    [Theory]
    [InlineData("version : 8.5.1.1343")]
    [InlineData("unrecognized")]
    public async Task RejectsDifferentOrUnknownServerVersionBeforeReadingClusters(string version)
    {
        var factory = new FakeFactory(args => args[0] == "agent" ? version : Response(args));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new RacInfobaseClient(factory).ReadAsync(Profile(), null));
        var session = Assert.Single(factory.Sessions);
        Assert.Equal(new[] { "agent", "version" }, Assert.Single(session.Commands));
        Assert.True(session.Disposed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("cluster : 11111111-1111-1111-1111-111111111111\nhost : other.test\nport : 1541")]
    public async Task RejectsMissingOrMovedCluster(string output)
    {
        var factory = new FakeFactory(args => args[0] == "cluster" ? output : Response(args));
        var profile = Profile();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new RacInfobaseClient(factory)
            .CreateAsync(profile, new(profile.Id, Cluster), Options, null, null));
        Assert.DoesNotContain(Assert.Single(factory.Sessions).Commands, args => args.Contains("create"));
    }

    [Fact]
    public async Task RejectsDuplicateNameIgnoringCase()
    {
        var factory = new FakeFactory(args => args.Contains("summary") ? $"infobase : {DatabaseId}\nname : \"база ТЕСТ\"\n" : Response(args));
        var profile = Profile();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new RacInfobaseClient(factory)
            .CreateAsync(profile, new(profile.Id, Cluster), Options, null, null));
        Assert.Contains("уже есть", error.Message);
        Assert.DoesNotContain(Assert.Single(factory.Sessions).Commands, args => args.Contains("create"));
    }

    [Fact]
    public async Task RejectsTargetFromAnotherConnectionWithoutOpeningBridge()
    {
        var factory = new FakeFactory(Response);
        await Assert.ThrowsAsync<ArgumentException>(() => new RacInfobaseClient(factory)
            .CreateAsync(Profile(), new(Guid.NewGuid(), Cluster), Options, null, null));
        Assert.Empty(factory.Opened);
    }

    [Theory]
    [InlineData("Name")]
    [InlineData("DbServer")]
    [InlineData("DbName")]
    public async Task RejectsMissingRequiredFieldsBeforeOpeningBridge(string field)
    {
        var options = field switch { "Name" => Options with { Name = "" }, "DbServer" => Options with { DbServer = "" }, _ => Options with { DbName = "" } };
        var factory = new FakeFactory(Response);
        var profile = Profile();
        await Assert.ThrowsAsync<ArgumentException>(() => new RacInfobaseClient(factory)
            .CreateAsync(profile, new(profile.Id, Cluster), options, null, null));
        Assert.Empty(factory.Opened);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MarksLostCreateResponseUncertainRedactsBothSecretsAndDoesNotRetry(bool cancel)
    {
        var factory = new FakeFactory(args => args.Contains("create")
            ? throw (cancel ? new OperationCanceledException("cluster-secret db-secret") : new TimeoutException("cluster-secret db-secret")) : Response(args));
        var profile = Profile();
        var error = await Assert.ThrowsAsync<OneCInfobaseCreationUncertainException>(() => new RacInfobaseClient(factory)
            .CreateAsync(profile, new(profile.Id, Cluster), Options, "cluster-secret", "db-secret"));
        Assert.DoesNotContain("cluster-secret", error.ToString());
        Assert.DoesNotContain("db-secret", error.ToString());
        Assert.Single(Assert.Single(factory.Sessions).Commands, c => c.Contains("create"));
        Assert.True(factory.Sessions[0].Disposed);
    }

    [Fact]
    public async Task MissingCreatedUuidIsNotReportedAsSuccess()
    {
        var factory = new FakeFactory(args => args.Contains("create") ? "" : Response(args));
        var profile = Profile();
        await Assert.ThrowsAsync<OneCInfobaseCreationUncertainException>(() => new RacInfobaseClient(factory)
            .CreateAsync(profile, new(profile.Id, Cluster), Options, null, null));
    }

    [Fact]
    public async Task ReportsClusterReadFailureInsteadOfPretendingItIsEmpty()
    {
        var factory = new FakeFactory(args => args.Contains("summary") ? throw new InvalidOperationException("Access denied: secret") : Response(args));
        var result = await new RacInfobaseClient(factory).ReadAsync(Profile(), "secret");
        Assert.Contains("Access denied", Assert.Single(result).DetailsMessage);
        Assert.DoesNotContain("secret", result[0].DetailsMessage);
    }

    private sealed class FakeFactory(Func<IReadOnlyList<string>, string> response) : IOneCRasSessionFactory
    {
        public List<OneCServerConnectionProfile> Opened { get; } = [];
        public List<FakeSession> Sessions { get; } = [];
        public Task<IOneCRasSession> OpenAsync(OneCServerConnectionProfile profile, CancellationToken token)
        {
            Opened.Add(profile);
            var session = new FakeSession(response);
            Sessions.Add(session);
            return Task.FromResult<IOneCRasSession>(session);
        }
    }
    private sealed class FakeSession(Func<IReadOnlyList<string>, string> response) : IOneCRasSession
    {
        public List<IReadOnlyList<string>> Commands { get; } = [];
        public bool Disposed { get; private set; }
        public Task<string> RunAsync(IReadOnlyList<string> args, CancellationToken token)
        { Commands.Add(args); return Task.FromResult(response(args)); }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
}
