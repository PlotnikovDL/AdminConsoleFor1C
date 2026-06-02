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
}
