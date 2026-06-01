using AdminConsoleFor1C.Core.Services;

namespace AdminConsoleFor1C.Core.Tests.Services;

public sealed class OneCServiceInfoTests
{
    [Fact]
    public void PortsText_ReturnsNamedPorts_ForServerAgent()
    {
        var service = CreateServiceInfo() with
        {
            AgentPort = 1540,
            RegPort = 1541,
            DebugServerPort = 1550,
            PortRange = "1560:1591"
        };

        var ports = service.PortsText.Split(Environment.NewLine);

        Assert.Equal(
            ["Агент: 1540", "Кластер: 1541", "Отладка HTTP: 1550", "Процессы: 1560:1591"],
            ports);
    }

    [Fact]
    public void PortsText_ReturnsRasPort_ForAdministrationServer()
    {
        var service = CreateServiceInfo() with
        {
            Kind = OneCServiceKind.AdministrationServer,
            AdministrationServerPort = 1545
        };

        Assert.Equal("RAS: 1545", service.PortsText);
    }

    private static OneCServiceInfo CreateServiceInfo()
    {
        return new OneCServiceInfo
        {
            Name = "1C:Enterprise 8.3 Server Agent 1540",
            DisplayName = "Агент сервера 1С:Предприятия 8.3",
            Kind = OneCServiceKind.ServerAgent,
            State = "Running",
            Status = "OK"
        };
    }
}
