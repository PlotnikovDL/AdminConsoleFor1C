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
            ["cluster"] = "55555555-5555-5555-5555-555555555555",
            ["name"] = "\"Управление торговлей\"",
            ["descr"] = "\"Рабочая база\"",
            ["dbms"] = "MSSQLServer",
            ["db-server"] = "sql01",
            ["db-name"] = "ut",
            ["db-user"] = "sa",
            ["security-level"] = "1",
            ["license-distribution"] = "allow",
            ["sessions-deny"] = "off",
            ["scheduled-jobs-deny"] = "on"
        };

        var infobase = OneCInfobaseSummaryInfo.FromProperties(properties);

        Assert.Equal("Управление торговлей", infobase.NameText);
        Assert.Equal("55555555-5555-5555-5555-555555555555", infobase.ClusterUuidText);
        Assert.Equal("Рабочая база", infobase.DescriptionText);
        Assert.Contains("СУБД: MS SQL Server", infobase.DatabaseText);
        Assert.Contains("Сервер: sql01", infobase.DatabaseText);
        Assert.Contains("База: ut", infobase.DatabaseText);
        Assert.Contains("Пользователь: sa", infobase.DatabaseText);
        Assert.Equal("Защищенное соединение: только соединение", infobase.SecurityText);
        Assert.Contains("Защищенное соединение: только соединение", infobase.SecurityAndLicensingText);
        Assert.Contains("Лицензии: выдаются", infobase.SecurityAndLicensingText);
        Assert.Contains("Лицензии: выдаются", infobase.RestrictionsText);
        Assert.Contains("Вход пользователей: разрешен", infobase.RestrictionsText);
        Assert.Contains("Регламентные задания: заблокированы", infobase.RestrictionsText);
        Assert.True(infobase.AreSessionsAllowed);
        Assert.False(infobase.AreScheduledJobsAllowed);
    }

    [Fact]
    public void FromProperties_ReturnsOccupiedProcessLicenseInfo()
    {
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["process"] = "66666666-6666-6666-6666-666666666666",
            ["pid"] = "1648",
            ["server"] = "SERVER-1C",
            ["port"] = "1560",
            ["license"] = "\"Сервер, SERVER-1C, 1560, 8000314159 1 1\"",
            ["license-file"] = "\"C:\\ProgramData\\1C\\licenses\\20100521112156.lic\""
        };

        var license = OneCOccupiedLicenseInfo.FromProperties(properties, OneCLicenseOwnerKind.Process);

        Assert.Equal("Сервер", license.OwnerKindText);
        Assert.Equal("PID 1648", license.OwnerText);
        Assert.Contains("сервер SERVER-1C", license.ContextText);
        Assert.Contains("порт 1560", license.ContextText);
        Assert.Contains("8000314159", license.LicenseText);
        Assert.Contains("20100521112156.lic", license.LicenseFileText);
    }

    [Fact]
    public void FromProperties_ReturnsOccupiedSessionLicenseInfo()
    {
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["session"] = "77777777-7777-7777-7777-777777777777",
            ["session-id"] = "5",
            ["user-name"] = "\"Ivan\"",
            ["host"] = "DESKTOP-ROY",
            ["app-id"] = "1CV8C",
            ["client-license"] = "\"Клиент, 4648, 8000453822 20 20\""
        };

        var license = OneCOccupiedLicenseInfo.FromProperties(properties, OneCLicenseOwnerKind.Session);

        Assert.Equal("Пользователь", license.OwnerKindText);
        Assert.Equal("Ivan, сеанс 5", license.OwnerText);
        Assert.Contains("DESKTOP-ROY", license.ContextText);
        Assert.Contains("1CV8C", license.ContextText);
        Assert.Contains("8000453822", license.LicenseText);
    }

    [Fact]
    public void Create_ReturnsSingleSeatUsageForLocalClientLicense()
    {
        var licenses = Enumerable.Range(1, 5)
            .Select(index => OneCOccupiedLicenseInfo.FromProperties(
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["session"] = $"session-{index}",
                    ["user-name"] = "\"DefUser\"",
                    ["host"] = "DESKTOP-ROY",
                    ["app-id"] = "1CV8C",
                    ["series"] = "\"ORGL8\"",
                    ["issued-by-server"] = "no",
                    ["license-type"] = "HASP",
                    ["net"] = "no",
                    ["max-users-all"] = "1",
                    ["max-users-cur"] = "1",
                    ["rmngr-pid"] = (30000 + index).ToString(),
                    ["short-presentation"] = "\"Клиент, ORGL8 Лок 1\"",
                    ["full-presentation"] = $"\"Клиент, {30000 + index}, ORGL8 Локальный 1\""
                },
                OneCLicenseOwnerKind.Session))
            .ToList();

        var usages = OneCLicenseUsageInfo.Create([], licenses);
        var usage = Assert.Single(usages);

        Assert.Equal("Клиентская", usage.OwnerKindText);
        Assert.Equal("ORGL8", usage.SourceText);
        Assert.Equal("HASP, локальная", usage.LicenseKindText);
        Assert.Equal("на компьютер", usage.ConsumptionModeText);
        Assert.Equal("1/1", usage.UsageText);
        Assert.Equal(1, usage.OccupiedSeats);
        Assert.Equal(5, usage.ConsumerCount);
        Assert.Equal("5 сеансов используют 1 место", usage.ExplanationText);
        Assert.Contains("Пользователи: DefUser", usage.ConsumersDetailText);
        Assert.Contains("Компьютеры: DESKTOP-ROY", usage.ConsumersDetailText);
    }

    [Fact]
    public void Create_ReturnsSessionUsageForNetworkClientLicense()
    {
        var licenses = Enumerable.Range(1, 5)
            .Select(index => OneCOccupiedLicenseInfo.FromProperties(
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["session"] = $"session-{index}",
                    ["user-name"] = "\"DefUser\"",
                    ["host"] = "DESKTOP-ROY",
                    ["app-id"] = "1CV8C",
                    ["series"] = "\"8101787554\"",
                    ["issued-by-server"] = "yes",
                    ["license-type"] = "soft",
                    ["net"] = "yes",
                    ["max-users-all"] = "100",
                    ["max-users-cur"] = "95",
                    ["rmngr-address"] = "\"SERVER-1C\"",
                    ["rmngr-port"] = "1564",
                    ["rmngr-pid"] = (40000 + index).ToString(),
                    ["short-presentation"] = "\"Клиент, 8101787554 100 95\""
                },
                OneCLicenseOwnerKind.Session))
            .ToList();

        var usages = OneCLicenseUsageInfo.Create([], licenses);
        var usage = Assert.Single(usages);

        Assert.Equal("Клиентская", usage.OwnerKindText);
        Assert.Equal("8101787554", usage.SourceText);
        Assert.Equal("программная, сетевая", usage.LicenseKindText);
        Assert.Equal("на сеанс", usage.ConsumptionModeText);
        Assert.Equal("5/100", usage.UsageText);
        Assert.Equal(5, usage.OccupiedSeats);
        Assert.Equal(5, usage.ConsumerCount);
        Assert.Equal("5 сеансов занимают 5 мест", usage.ExplanationText);
    }

    [Fact]
    public void OccupiedLicensesSummaryText_ReturnsSeatsAndSessions()
    {
        var licenses = Enumerable.Range(1, 5)
            .Select(index => OneCOccupiedLicenseInfo.FromProperties(
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["session"] = $"session-{index}",
                    ["user-name"] = "\"DefUser\"",
                    ["host"] = "DESKTOP-ROY",
                    ["series"] = "\"ORGL8\"",
                    ["issued-by-server"] = "no",
                    ["license-type"] = "HASP",
                    ["net"] = "no",
                    ["max-users-all"] = "1"
                },
                OneCLicenseOwnerKind.Session))
            .ToList();
        var cluster = new OneCClusterInfo
        {
            Uuid = "cluster-1",
            SessionLicenses = licenses
        };

        Assert.Equal("Клиентские места: 1/1, сеансов: 5", cluster.OccupiedLicensesSummaryText);
    }
}
