using AdminConsoleFor1C.Core.Services;

namespace AdminConsoleFor1C.Core.Tests.Services;

public sealed class OneCServiceRegistrationRequestTests
{
    private static OneCServiceRegistrationRequest ValidRequest() => new()
    {
        ServiceName = "1C:Enterprise 8.3 Server Agent 2540 8.3.27.1000",
        ExecutablePath = @"C:\Program Files\1cv8\8.3.27.1000\bin\ragent.exe",
        DataDirectory = @"C:\ProgramData\1C\Новый кластер\",
        AgentPort = 2540, ClusterPort = 2541, ProcessPortStart = 2560, ProcessPortEnd = 2591
    };

    [Fact]
    public void BuildCommandLine_QuotesPathsAndTrimsTrailingSlash()
    {
        Assert.Equal("\"C:\\Program Files\\1cv8\\8.3.27.1000\\bin\\ragent.exe\" /srvc /agent /regport 2541 /port 2540 /range 2560:2591 /d \"C:\\ProgramData\\1C\\Новый кластер\"",
            ValidRequest().BuildCommandLine());
    }

    [Fact]
    public void BuildCommandLine_EnablesHttpDebugOnlyWhenRequested()
    {
        Assert.DoesNotContain("/debug", ValidRequest().BuildCommandLine());
        Assert.EndsWith("/debug -http /debugServerPort 2550", (ValidRequest() with { DebugPort = 2550 }).BuildCommandLine());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    [InlineData(2541)]
    [InlineData(2560)]
    [InlineData(2591)]
    public void Validate_RejectsInvalidOrOverlappingAgentPort(int port)
    {
        Assert.NotEmpty((ValidRequest() with { AgentPort = port }).Validate());
    }

    [Fact]
    public void Validate_RejectsReversedRangeAndDebugOverlap()
    {
        Assert.NotEmpty((ValidRequest() with { ProcessPortStart = 2600 }).Validate());
        Assert.NotEmpty((ValidRequest() with { DebugPort = 2570 }).Validate());
        Assert.NotEmpty((ValidRequest() with { DebugPort = 2540 }).Validate());
    }

    [Theory]
    [InlineData("C:\\ragent.exe\" -injected")]
    [InlineData("ragent.exe")]
    [InlineData("C:\\other.exe")]
    [InlineData("\\\\server\\share\\ragent.exe")]
    public void BuildCommandLine_RejectsInvalidExecutablePath(string path)
    {
        Assert.Throws<ArgumentException>(() => (ValidRequest() with { ExecutablePath = path }).BuildCommandLine());
    }

    [Theory]
    [InlineData("")]
    [InlineData("bad/name")]
    [InlineData("bad\\name")]
    [InlineData("bad\nname")]
    public void Validate_RejectsInvalidServiceNames(string name)
    {
        Assert.NotEmpty((ValidRequest() with { ServiceName = name }).Validate());
    }

    [Theory]
    [InlineData("2500:2600")]
    [InlineData("2570:2571")]
    [InlineData("1000:2000,2591:2600")]
    [InlineData("2540")]
    public void Validate_DetectsExistingPortRanges(string range)
    {
        Assert.NotEmpty(ValidRequest().Validate([ExistingService() with { PortRange = range }]));
    }

    [Fact]
    public void Validate_DetectsStoppedServicesAndRasPorts()
    {
        Assert.NotEmpty(ValidRequest().Validate([ExistingService() with { AdministrationServerPort = 2540 }]));
        Assert.Empty(ValidRequest().Validate([ExistingService() with { AgentPort = 1540, PortRange = "1560:1591" }]));
    }

    [Fact]
    public void Validate_ReservesDefaultPortsWhenExistingServiceOmitsArguments()
    {
        Assert.NotEmpty((ValidRequest() with { AgentPort = 1540 }).Validate([ExistingService()]));
        Assert.NotEmpty((ValidRequest() with { ProcessPortStart = 1570, ProcessPortEnd = 1580 }).Validate([ExistingService()]));
    }

    [Fact]
    public void Validate_DetectsNamesAndNormalizedDataDirectory()
    {
        Assert.NotEmpty(ValidRequest().Validate([ExistingService() with { Name = ValidRequest().ServiceName.ToLowerInvariant() }]));
        Assert.NotEmpty(ValidRequest().Validate([ExistingService() with { DisplayName = ValidRequest().ServiceName.ToUpperInvariant() }]));
        Assert.NotEmpty(ValidRequest().Validate([ExistingService() with { DataDirectory = @"c:\programdata\1c\Новый кластер" }]));
    }

    private static OneCServiceInfo ExistingService() => new()
    {
        Name = "existing", DisplayName = "Существующая служба", Kind = OneCServiceKind.ServerAgent,
        State = "Stopped", Status = "OK"
    };

    [Theory]
    [InlineData("8.3.27.2170", 1540, "1C:Enterprise 8.3 Server Agent 1540 8.3.27.2170")]
    [InlineData("8.5.1.1343", 2540, "1C:Enterprise 8.5 Server Agent 2540 8.5.1.1343")]
    public void Naming_MatchesExistingServiceStandard(string version, int port, string expected)
    {
        var request = ValidRequest() with { ServiceName = OneCServiceNaming.BuildServerAgentName(version, port) };
        Assert.Equal(expected, request.ServiceName);
        Assert.Equal(request.ServiceName, request.DisplayName);
    }

    [Theory]
    [InlineData(@".\test-user")]
    [InlineData(@"SERVER\service-user")]
    [InlineData(@"DOMAIN\service-user")]
    [InlineData("service-user@example.test")]
    public void WindowsUser_IsSeparateFromServiceCommandLine(string userName)
    {
        var request = ValidRequest() with { Account = OneCServiceAccount.WindowsUser, UserName = userName };
        Assert.Empty(request.Validate());
        Assert.Equal(userName, request.ServiceStartName);
        Assert.Equal(ValidRequest().BuildCommandLine(), request.BuildCommandLine());
        Assert.DoesNotContain(userName, request.BuildCommandLine());
        Assert.DoesNotContain("/pwd", request.BuildCommandLine());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("user")]
    [InlineData(@".\")]
    [InlineData(@"DOMAIN\user\extra")]
    [InlineData("DOMAIN/user")]
    public void Validate_RejectsUnqualifiedOrMalformedWindowsUser(string? userName)
    {
        Assert.NotEmpty((ValidRequest() with { Account = OneCServiceAccount.WindowsUser, UserName = userName }).Validate());
    }

    [Fact]
    public void CommandLine_RoundTripsThroughServiceInventoryParser()
    {
        var request = ValidRequest() with { DebugPort = 2550 };
        var parsed = OneCServiceCommandLineParser.Parse(request.BuildCommandLine());
        Assert.Equal(request.ExecutablePath, parsed.ExecutablePath);
        Assert.Equal("2540", OneCServiceCommandLineParser.GetOptionValue(parsed.Arguments, "port"));
        Assert.Equal("2541", OneCServiceCommandLineParser.GetOptionValue(parsed.Arguments, "regport"));
        Assert.Equal("2560:2591", OneCServiceCommandLineParser.GetOptionValue(parsed.Arguments, "range"));
        Assert.Equal("2550", OneCServiceCommandLineParser.GetOptionValue(parsed.Arguments, "debugServerPort"));
        Assert.Equal(request.DataDirectory.TrimEnd('\\'), OneCServiceCommandLineParser.GetOptionValue(parsed.Arguments, "d"));
    }
}
