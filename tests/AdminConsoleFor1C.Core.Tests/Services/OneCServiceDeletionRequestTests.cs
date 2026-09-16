using AdminConsoleFor1C.Core.Services;

namespace AdminConsoleFor1C.Core.Tests.Services;

public sealed class OneCServiceDeletionRequestTests
{
    private const string DataPath = @"C:\Program Files\1cv8\srvinfo\2540";
    private static OneCServiceInfo Service(string path = DataPath) => new()
    {
        Name = "agent-2540", DisplayName = "Agent 2540", Kind = OneCServiceKind.ServerAgent,
        State = "Running", Status = "OK", DataDirectory = path,
        ExecutablePath = @"C:\Program Files\1cv8\8.5.1.100\bin\ragent.exe",
        RawCommandLine = "unchanged command"
    };
    private static OneCServiceDeletionRequest Request(string? path = DataPath) => new()
    {
        ServiceName = "agent-2540", ExpectedCommandLine = "unchanged command", DataDirectory = path
    };

    [Fact]
    public void AcceptsConfirmedDirectoryAndDistinctSibling()
    {
        Assert.Equal(DataPath, Request().ValidateAndGetDataDirectory([
            Service(), Service(DataPath + "0") with { Name = "other" }]));
    }

    [Fact]
    public void ServiceOnlyDoesNotRequireDirectory()
    {
        Assert.Null(Request(null).ValidateAndGetDataDirectory([Service() with { DataDirectory = null }]));
    }

    [Fact]
    public void RejectsChangedServiceOrDirectory()
    {
        Assert.Throws<InvalidOperationException>(() => Request().ValidateAndGetDataDirectory([Service() with { RawCommandLine = "changed" }]));
        Assert.Throws<InvalidOperationException>(() => Request().ValidateAndGetDataDirectory([Service(DataPath + "0")]));
        Assert.Throws<InvalidOperationException>(() => Request().ValidateAndGetDataDirectory([]));
        Assert.Throws<InvalidOperationException>(() => Request().ValidateAndGetDataDirectory([Service() with { DataDirectory = null }]));
    }

    [Theory]
    [InlineData(DataPath)]
    [InlineData(@"C:\Program Files\1cv8\srvinfo")]
    [InlineData(DataPath + @"\reg_2541")]
    public void RejectsSharedParentOrChildDirectory(string otherPath)
    {
        Assert.Throws<InvalidOperationException>(() => Request().ValidateAndGetDataDirectory([
            Service(), Service(otherPath) with { Name = "other" }]));
    }

    [Theory]
    [InlineData(@"C:\")]
    [InlineData(@"C:\Program Files")]
    [InlineData(@"C:\Program Files\1cv8")]
    [InlineData(@"C:\Program Files\1cv8\srvinfo")]
    [InlineData(@"C:\Program Files\1cv8\8.5.1.100")]
    [InlineData(@"C:\Windows\System32")]
    [InlineData(@"..\2540")]
    [InlineData(@"C:\data\..\Windows")]
    [InlineData(@"\\server\data\2540")]
    [InlineData(@"C:\PROGRA~1\1cv8\srvinfo\2540")]
    public void RejectsBroadOrAmbiguousPaths(string path)
    {
        Assert.Throws<InvalidOperationException>(() => Request(path).ValidateAndGetDataDirectory([Service(path)]));
    }
}
