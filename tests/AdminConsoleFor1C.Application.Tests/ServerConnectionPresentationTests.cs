using AdminConsoleFor1C.Application.Services;
using AdminConsoleFor1C.Core.Administration;
using AdminConsoleFor1C.Core.Services;

namespace AdminConsoleFor1C.Application.Tests;

public sealed class ServerConnectionPresentationTests
{
    private static OneCServerConnectionProfile Profile(string name, string host = "localhost") => new()
    {
        Name = name, Host = host, AgentPort = 3540,
        PlatformDirectory = @"C:\Tools\bin", PlatformVersion = "8.3.27.2214", ClusterUser = "admin"
    };

    [Theory]
    [InlineData("Этот компьютер · 8.3.27.2214")]
    [InlineData("localhost:3540")]
    [InlineData("127.0.0.1:3540")]
    [InlineData("[::1]:3540")]
    [InlineData("workstation:3540")]
    public void LegacyAutomaticLocalNamesHaveOneDisplayWithoutChangingProfile(string name)
    {
        var profile = Profile(name);
        Assert.Equal("WORKSTATION:3540", ServerConnectionPresentation.DisplayName(profile, "WORKSTATION"));
        Assert.Equal(name, profile.Name);
        Assert.Equal("localhost", profile.Host);
        Assert.Equal("admin", profile.ClusterUser);
    }

    [Theory]
    [InlineData("Бухгалтерия — рабочий сервер", "localhost")]
    [InlineData("Этот компьютер · 8.3.27.2214", "remote-server")]
    [InlineData("localhost:1540", "localhost")]
    public void CustomOrDifferentTargetNamesArePreserved(string name, string host)
        => Assert.Equal(name, ServerConnectionPresentation.DisplayName(Profile(name, host), "WORKSTATION"));

    [Theory]
    [InlineData("LOCALHOST", "WORKSTATION:3540")]
    [InlineData("::1", "WORKSTATION:3540")]
    [InlineData("remote-server", "remote-server:3540")]
    [InlineData("2001:db8::1", "[2001:db8::1]:3540")]
    public void SuggestedNameDistinguishesLocalRemoteAndIpv6Targets(string host, string expected)
        => Assert.Equal(expected, ServerConnectionPresentation.SuggestedName(host, 3540, "WORKSTATION"));

    [Fact]
    public void LocalPortsStayTogetherBeforeRemoteRegardlessOfInputNamesAndVersions()
    {
        var remote = Entry("AAAA remote", "server", 1540);
        var local3540 = Entry("WORKSTATION:3540", "localhost", 3540, "8.3.9.100", discovered: true);
        var local1540 = Entry("Z production", "localhost", 1540, "8.5.1.10", discovered: true);
        var local2540 = Entry("A development", "WORKSTATION", 2540, "8.3.27.2214", service: true);

        var ordered = Order(remote, local3540, local2540, local1540);

        Assert.Equal([1540, 2540, 3540, 1540], ordered.Select(entry => entry.Profile.AgentPort));
        Assert.Same(local1540, ordered[0]);
        Assert.Same(local2540, ordered[1]);
        Assert.Same(local3540, ordered[2]);
        Assert.Same(remote, ordered[3]);
        Assert.Equal("Z production", local1540.Profile.Name);
    }

    [Fact]
    public void SavedLocalAliasesShareOneComputerEvenWithoutServiceInventory()
    {
        var local3540 = Entry("A local", "LOCALHOST", 3540);
        var local900 = Entry("Z local", "127.0.0.1", 900);
        var local1000 = Entry("B local", "::1", 1000);
        var local4540 = Entry("C local", "workstation", 4540);
        var remote = Entry("AAAA remote", "remote", 1540);

        var ordered = Order(remote, local3540, local4540, local1000, local900);

        Assert.Equal([local900, local1000, local3540, local4540, remote], ordered);
        Assert.All(ordered.Take(4), entry => Assert.False(entry.IsDiscovered));
        Assert.All(ordered.Take(4), entry => Assert.Null(entry.LocalService));
    }

    [Fact]
    public void LocalSourceGroupsUseNaturalComputerOrderBeforePorts()
    {
        var remote = Entry("AAAA remote", "remote", 1540);
        var local10 = Entry("A agent", "computer10", 1540, service: true);
        var local2At2540 = Entry("Z agent", "COMPUTER2", 2540, discovered: true);
        var local2At1540 = Entry("B agent", "computer2", 1540, service: true);

        Assert.Equal([local2At1540, local2At2540, local10, remote],
            Order(remote, local10, local2At2540, local2At1540));
    }

    [Fact]
    public void RemoteAutomaticNamesHaveNaturalHostAndNumericPortOrder()
    {
        var server10 = Entry("server10:1540", "server10", 1540);
        var server2At3540 = Entry("server2:3540", "server2", 3540);
        var server2At1540 = Entry("SERVER2:1540", "SERVER2", 1540);
        var server2At2540 = Entry("Server2:2540", "Server2", 2540);

        Assert.Equal([server2At1540, server2At2540, server2At3540, server10],
            Order(server10, server2At3540, server2At2540, server2At1540));
    }

    [Fact]
    public void RemoteAliasesSortByDisplayedNameThenAddressAndNumericPort()
    {
        var alias10 = Entry("Площадка10", "server1", 1540);
        var alias2AtHost10 = Entry("ПЛОЩАДКА2", "server10", 1540);
        var alias2AtHost2Port2540 = Entry("площадка2", "server2", 2540);
        var alias2AtHost2Port1540 = Entry("Площадка2", "SERVER2", 1540);

        Assert.Equal([alias2AtHost2Port1540, alias2AtHost2Port2540, alias2AtHost10, alias10],
            Order(alias10, alias2AtHost10, alias2AtHost2Port2540, alias2AtHost2Port1540));
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData("server")]
    public void EqualTargetsSortVersionsNumerically(string host)
    {
        var version10Build100 = Entry("Target", host, 1540, "8.3.10.100");
        var version9 = Entry("Target", host, 1540, "8.3.9.100");
        var version10Build20 = Entry("Target", host, 1540, "8.3.10.20");

        Assert.Equal([version9, version10Build20, version10Build100],
            Order(version10Build100, version10Build20, version9));
    }

    [Fact]
    public void EqualDisplayKeysHaveDeterministicIdentityOrderAcrossInputPermutations()
    {
        var first = Entry("Target", "SERVER2", 1540, id: Guid.Parse("00000000-0000-0000-0000-000000000001"));
        var second = Entry("target", "server2", 1540, id: Guid.Parse("00000000-0000-0000-0000-000000000002"));
        var third = Entry("TARGET", "Server2", 1540, id: Guid.Parse("00000000-0000-0000-0000-000000000003"));
        ServerConnectionEntry[] expected = [first, second, third];

        Assert.Equal(expected, Order(third, first, second));
        Assert.Equal(expected, Order(second, third, first));
        Assert.Equal(expected, Order(first, second, third));
    }

    [Fact]
    public void NaturalNamesHandleLeadingZerosAndNumericPartsBeyondIntegerRange()
    {
        var short2 = Entry("server2:1540", "server2", 1540);
        var padded2 = Entry("server002:1540", "server002", 1540);
        var long9 = Entry("server99999999999999999999:1540", "server99999999999999999999", 1540);
        var long10 = Entry("server100000000000000000000:1540", "server100000000000000000000", 1540);

        Assert.Equal([short2, padded2, long9, long10], Order(long10, padded2, long9, short2));
    }

    private static ServerConnectionEntry Entry(string name, string host, int port,
        string version = "8.3.27.2214", bool discovered = false, bool service = false, Guid? id = null)
    {
        var profile = Profile(name, host) with
        {
            Id = id ?? Guid.NewGuid(), AgentPort = port, PlatformVersion = version
        };
        return new(profile, discovered, service ? new OneCServiceInfo
        {
            Name = "agent", DisplayName = "Agent", Kind = OneCServiceKind.ServerAgent,
            State = "Running", Status = "OK", AgentPort = port, Version = version
        } : null);
    }

    private static ServerConnectionEntry[] Order(params ServerConnectionEntry[] entries)
        => ServerConnectionPresentation.OrderForDisplay(entries, entry => entry.Profile,
            entry => entry.IsDiscovered || entry.LocalService is not null, "WORKSTATION").ToArray();
}
