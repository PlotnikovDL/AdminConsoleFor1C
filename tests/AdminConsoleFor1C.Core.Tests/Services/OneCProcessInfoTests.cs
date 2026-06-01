using AdminConsoleFor1C.Core.Services;

namespace AdminConsoleFor1C.Core.Tests.Services;

public sealed class OneCProcessInfoTests
{
    [Fact]
    public void PortsText_ReturnsNamedPorts_ForServerAgentProcess()
    {
        var process = CreateProcessInfo() with
        {
            AgentPort = 1540,
            ClusterPort = 1541,
            DebugServerPort = 1550,
            PortRange = "1560:1591"
        };

        var ports = process.PortsText.Split(Environment.NewLine);

        Assert.Equal(
            ["Агент: 1540", "Кластер: 1541", "Отладка HTTP: 1550", "Диапазон: 1560:1591"],
            ports);
    }

    [Fact]
    public void RelatedServiceText_ReturnsServiceName_WhenProcessIsMatched()
    {
        var process = CreateProcessInfo() with
        {
            RelatedServiceDisplayName = "Агент сервера"
        };

        Assert.Equal("Служба: Агент сервера", process.RelatedServiceText);
    }

    private static OneCProcessInfo CreateProcessInfo()
    {
        return new OneCProcessInfo
        {
            Name = "ragent.exe",
            Kind = OneCProcessKind.ServerAgent,
            ProcessId = 1540
        };
    }
}
