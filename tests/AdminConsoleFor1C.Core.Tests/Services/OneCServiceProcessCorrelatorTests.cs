using AdminConsoleFor1C.Core.Services;

namespace AdminConsoleFor1C.Core.Tests.Services;

public sealed class OneCServiceProcessCorrelatorTests
{
    [Fact]
    public void MatchesServiceProcessAndItsDescendantsAndKeepsBothServiceNames()
    {
        var service = Service("service-1540", 10, 1540) with { DisplayName = "Агент сервера 1С" };
        var processes = new[]
        {
            Process(10),
            Process(20, 10, OneCProcessKind.ClusterManager),
            Process(30, 20, OneCProcessKind.WorkerProcess)
        };

        var result = OneCServiceProcessCorrelator.Correlate([service], processes);

        Assert.All(result, process =>
        {
            Assert.Equal("service-1540", process.RelatedServiceName);
            Assert.Equal("Агент сервера 1С", process.RelatedServiceDisplayName);
        });
    }

    [Fact]
    public void ProcessIdTakesPrecedenceOverAnotherServicesMatchingPort()
    {
        var ownService = Service("actual-service", 10, 2540);
        var portMatch = Service("matching-port", 40, 1540);
        var process = Process(10) with { AgentPort = 1540 };

        var result = OneCServiceProcessCorrelator.Correlate([portMatch, ownService], [process]);

        Assert.Equal("actual-service", Assert.Single(result).RelatedServiceName);
    }

    [Fact]
    public void UsesDefaultAgentPortForUniqueRunningServiceWhenProcessIdIsUnavailable()
    {
        var service = Service("default-agent", null, null);

        var result = OneCServiceProcessCorrelator.Correlate([service], [Process(10)]);

        Assert.Equal("default-agent", Assert.Single(result).RelatedServiceName);
    }

    [Fact]
    public void DoesNotGuessBetweenServicesWithSamePort()
    {
        var result = OneCServiceProcessCorrelator.Correlate(
            [Service("first", null, 1540), Service("second", null, 1540)], [Process(10)]);

        Assert.Null(Assert.Single(result).RelatedServiceName);
    }

    [Fact]
    public void DoesNotAssociateByPortWithStoppedServiceOrDifferentVersion()
    {
        var service = Service("stopped", null, 1540) with { State = "Stopped" };
        var otherVersion = Service("other-version", null, 1540) with { Version = "8.5.1.1343" };

        var result = OneCServiceProcessCorrelator.Correlate([service, otherVersion], [Process(10)]);

        Assert.Null(Assert.Single(result).RelatedServiceName);
    }

    [Fact]
    public void StopsWalkingWhenParentProcessIdsContainCycle()
    {
        var result = OneCServiceProcessCorrelator.Correlate([], [Process(10, 20), Process(20, 10)]);

        Assert.All(result, process => Assert.Null(process.RelatedServiceName));
    }

    private static OneCServiceInfo Service(string name, uint? processId, int? agentPort) => new()
    {
        Name = name, DisplayName = name, Kind = OneCServiceKind.ServerAgent,
        ProcessId = processId, State = "Running", Status = "OK", AgentPort = agentPort,
        Version = "8.3.27.2170"
    };

    private static OneCProcessInfo Process(uint processId, uint? parent = null, OneCProcessKind kind = OneCProcessKind.ServerAgent) => new()
    {
        Name = kind == OneCProcessKind.ServerAgent ? "ragent.exe" : kind == OneCProcessKind.ClusterManager ? "rmngr.exe" : "rphost.exe",
        ProcessId = processId, ParentProcessId = parent, Kind = kind, Version = "8.3.27.2170"
    };
}
