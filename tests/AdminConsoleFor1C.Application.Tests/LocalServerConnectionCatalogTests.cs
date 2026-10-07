using AdminConsoleFor1C.Application.Services;
using AdminConsoleFor1C.Core.Administration;
using AdminConsoleFor1C.Core.Services;

namespace AdminConsoleFor1C.Application.Tests;

public sealed class LocalServerConnectionCatalogTests
{
    private const string Computer = "WORKSTATION";
    private static OneCServiceInfo Agent(string version = "8.3.27.2214", int? port = 3540) => new()
    {
        Name = $"agent-{version}-{port}", DisplayName = "1C Server Agent", Kind = OneCServiceKind.ServerAgent,
        State = "Running", Status = "OK", Version = version, AgentPort = port,
        ExecutablePath = $@"C:\Program Files\1cv8\{version}\bin\ragent.exe"
    };
    private static OneCServerConnectionProfile Saved(string host = Computer, string version = "8.3.27.2214") => new()
    {
        Name = "Моё подключение", Host = host, AgentPort = 3540, PlatformVersion = version,
        PlatformDirectory = $@"C:\Tools\{version}\bin", ClusterUser = "cluster-admin"
    };

    [Fact]
    public void NewRegisteredServiceIsAvailableWithoutSavingConnection()
    {
        var entry = Assert.Single(LocalServerConnectionCatalog.Merge([], [Agent()], Computer));
        Assert.True(entry.IsDiscovered);
        Assert.Equal("WORKSTATION:3540", entry.Profile.Name);
        Assert.Equal("localhost:3540", entry.Profile.AgentAddress);
        Assert.Equal("8.3.27.2214", entry.Profile.PlatformVersion);
        Assert.Equal(@"C:\Program Files\1cv8\8.3.27.2214\bin", entry.Profile.PlatformDirectory);
        entry.Profile.Validate();
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData("LOCALHOST")]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    [InlineData("workstation")]
    public void LocalAliasesDoNotDuplicateSavedConnectionOrReplaceCredentials(string host)
    {
        var saved = Saved(host);
        var entry = Assert.Single(LocalServerConnectionCatalog.Merge([saved], [Agent()], Computer));
        Assert.Same(saved, entry.Profile);
        Assert.False(entry.IsDiscovered);
        Assert.Equal("cluster-admin", entry.Profile.ClusterUser);
        Assert.NotNull(entry.LocalService);
    }

    [Fact]
    public void RemoteConnectionWithSamePortAndVersionRemainsSeparate()
    {
        var saved = Saved("remote-server");
        var entries = LocalServerConnectionCatalog.Merge([saved], [Agent()], Computer);
        Assert.Equal(2, entries.Count);
        Assert.Same(saved, entries[0].Profile);
        Assert.Null(entries[0].LocalService);
        Assert.True(entries[1].IsDiscovered);
    }

    [Fact]
    public void SavedOldPlatformDoesNotHideNewServiceOnSamePort()
    {
        var saved = Saved(version: "8.3.27.2170");
        var entries = LocalServerConnectionCatalog.Merge([saved], [Agent()], Computer);
        Assert.Equal(2, entries.Count);
        Assert.Null(entries[0].LocalService);
        Assert.Equal("8.3.27.2214", entries[1].Profile.PlatformVersion);
    }

    [Fact]
    public void ServiceStateIsRetainedAndIdentityStableAcrossRefresh()
    {
        var running = Assert.Single(LocalServerConnectionCatalog.Merge([], [Agent()], Computer));
        var stopped = Assert.Single(LocalServerConnectionCatalog.Merge([], [Agent() with { State = "Stopped" }], Computer));
        Assert.Equal(running.Profile, stopped.Profile);
        Assert.Equal("Stopped", stopped.LocalService!.State);
    }

    [Fact]
    public void RemovedServiceDisappearsButSavedRemoteConnectionRemains()
    {
        var saved = Saved("remote-server");
        Assert.Equal(2, LocalServerConnectionCatalog.Merge([saved], [Agent()], Computer).Count);
        Assert.Same(saved, Assert.Single(LocalServerConnectionCatalog.Merge([saved], [], Computer)).Profile);
    }

    [Fact]
    public void AdministrationAndDebugServicesAreNotAgentTargets()
    {
        Assert.Empty(LocalServerConnectionCatalog.Merge([], [
            Agent() with { Kind = OneCServiceKind.AdministrationServer },
            Agent() with { Kind = OneCServiceKind.DebugServer }], Computer));
    }

    [Fact]
    public void ServicesOfSameVersionOnDifferentPortsRemainSeparate()
    {
        var entries = LocalServerConnectionCatalog.Merge([], [Agent(port: 1540), Agent(port: 3540)], Computer);
        Assert.Equal(2, entries.Count);
        Assert.NotEqual(entries[0].Profile.Id, entries[1].Profile.Id);
    }

    [Fact]
    public void OmittedAgentPortUsesPlatformDefault()
        => Assert.Equal(1540, Assert.Single(LocalServerConnectionCatalog.Merge([], [Agent(port: null)], Computer)).Profile.AgentPort);
}
