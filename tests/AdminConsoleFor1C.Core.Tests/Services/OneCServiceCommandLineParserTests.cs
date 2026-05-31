using AdminConsoleFor1C.Core.Services;

namespace AdminConsoleFor1C.Core.Tests.Services;

public sealed class OneCServiceCommandLineParserTests
{
    [Fact]
    public void Parse_ReturnsExecutablePathAndArguments_WhenPathIsQuoted()
    {
        var commandLine = OneCServiceCommandLineParser.Parse(
            "\"C:\\Program Files\\1cv8\\8.5.1.100\\bin\\ragent.exe\" -srvc -agent -port 1540");

        Assert.Equal("C:\\Program Files\\1cv8\\8.5.1.100\\bin\\ragent.exe", commandLine.ExecutablePath);
        Assert.Equal("-srvc -agent -port 1540", commandLine.Arguments);
    }

    [Fact]
    public void GetOptionValue_ReturnsValues_ForDashArguments()
    {
        const string arguments = "-srvc -agent -regport 1541 -port 1540 -range 1560:1591 -d \"C:\\Program Files\\1cv8\\srvinfo\\1540\"";

        Assert.Equal("1540", OneCServiceCommandLineParser.GetOptionValue(arguments, "port"));
        Assert.Equal("1541", OneCServiceCommandLineParser.GetOptionValue(arguments, "regport"));
        Assert.Equal("1560:1591", OneCServiceCommandLineParser.GetOptionValue(arguments, "range"));
        Assert.Equal("C:\\Program Files\\1cv8\\srvinfo\\1540", OneCServiceCommandLineParser.GetOptionValue(arguments, "d"));
    }

    [Fact]
    public void GetOptionValue_ReturnsValues_ForSlashArguments()
    {
        const string arguments = "/srvc /agent /regport 2541 /port 2540 /range 2560:2591 /d \"c:\\cluster data\"";

        Assert.Equal("2540", OneCServiceCommandLineParser.GetOptionValue(arguments, "port"));
        Assert.Equal("2541", OneCServiceCommandLineParser.GetOptionValue(arguments, "regport"));
        Assert.Equal("2560:2591", OneCServiceCommandLineParser.GetOptionValue(arguments, "range"));
        Assert.Equal("c:\\cluster data", OneCServiceCommandLineParser.GetOptionValue(arguments, "d"));
    }

    [Fact]
    public void GetOptionValue_ReturnsValue_ForLongArgumentWithEquals()
    {
        const string arguments = "cluster --service --port=1545 localhost:1540";

        Assert.Equal("1545", OneCServiceCommandLineParser.GetOptionValue(arguments, "port"));
    }

    [Fact]
    public void GetVersionFromExecutablePath_ReturnsVersion_From1CPath()
    {
        var version = OneCServiceCommandLineParser.GetVersionFromExecutablePath(
            "C:\\Program Files\\1cv8\\8.5.1.100\\bin\\ras.exe");

        Assert.Equal("8.5.1.100", version);
    }
}
