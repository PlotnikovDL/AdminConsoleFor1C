using AdminConsoleFor1C.Core.Administration;

namespace AdminConsoleFor1C.Core.Tests.Services;

public sealed class OneCRacOutputParserTests
{
    [Fact]
    public void ParseObjects_ReturnsKeyValueBlocks()
    {
        const string output = """
            cluster                       : 11111111-1111-1111-1111-111111111111
            name                          : "Основной кластер"
            host                          : "server01"
            port                          : 1541

            cluster                       : 22222222-2222-2222-2222-222222222222
            name                          : "Резервный кластер"
            host                          : "server02"
            port                          : 2541
            """;

        var objects = OneCRacOutputParser.ParseObjects(output);

        Assert.Equal(2, objects.Count);
        Assert.Equal("11111111-1111-1111-1111-111111111111", objects[0]["cluster"]);
        Assert.Equal("\"Резервный кластер\"", objects[1]["name"]);
    }

    [Fact]
    public void FromProperties_ReturnsClusterInfo()
    {
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["cluster"] = "11111111-1111-1111-1111-111111111111",
            ["name"] = "\"Основной кластер\"",
            ["host"] = "\"localhost\"",
            ["port"] = "1541",
            ["security-level"] = "0",
            ["load-balancing-mode"] = "performance"
        };

        var cluster = OneCClusterInfo.FromProperties(properties);

        Assert.Equal("Основной кластер", cluster.NameText);
        Assert.Equal("localhost:1541", cluster.AddressText);
        Assert.Equal("0", cluster.SecurityLevelText);
        Assert.Equal("performance", cluster.LoadBalancingModeText);
    }

    [Fact]
    public void FromProperties_ReturnsClusterServerInfo()
    {
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["server"] = "33333333-3333-3333-3333-333333333333",
            ["agent-host"] = "DESKTOP-ROY",
            ["agent-port"] = "1540",
            ["port-range"] = "1560:1591",
            ["name"] = "\"Центральный сервер\"",
            ["using"] = "main",
            ["infobases-limit"] = "8",
            ["connections-limit"] = "256",
            ["cluster-port"] = "1541"
        };

        var server = OneCClusterServerInfo.FromProperties(properties);

        Assert.Equal("Центральный сервер", server.NameText);
        Assert.Equal("DESKTOP-ROY:1540", server.AgentAddressText);
        Assert.Contains("Кластер: 1541", server.PortsText);
        Assert.Contains("Процессы: 1560:1591", server.PortsText);
        Assert.Equal("Центральный сервер", server.UsageText);
        Assert.Contains("Соединений: 256", server.LimitsText);
    }

    [Fact]
    public void FromProperties_ReturnsInfobaseSummaryInfo()
    {
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["infobase"] = "44444444-4444-4444-4444-444444444444",
            ["name"] = "\"Управление торговлей\"",
            ["descr"] = "\"Рабочая база\"",
            ["dbms"] = "MSSQLServer",
            ["db-server"] = "sql01",
            ["db-name"] = "ut",
            ["sessions-deny"] = "off",
            ["scheduled-jobs-deny"] = "on"
        };

        var infobase = OneCInfobaseSummaryInfo.FromProperties(properties);

        Assert.Equal("Управление торговлей", infobase.NameText);
        Assert.Equal("Рабочая база", infobase.DescriptionText);
        Assert.Equal("MSSQLServer, sql01, ut", infobase.DatabaseText);
        Assert.Contains("Сеансы: разрешены", infobase.RestrictionsText);
        Assert.Contains("Задания: запрещены", infobase.RestrictionsText);
    }
}
