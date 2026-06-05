using AdminConsoleFor1C.Core.Administration;

namespace AdminConsoleFor1C.Core.Tests.Services;

public sealed class OneCAdministrationToolInfoTests
{
    [Fact]
    public void FileName_ReturnsRacExecutableName()
    {
        var tool = new OneCAdministrationToolInfo
        {
            Kind = OneCAdministrationToolKind.Rac,
            FilePath = @"C:\Program Files\1cv8\8.3.27.2170\bin\rac.exe",
            Version = "8.3.27.2170"
        };

        Assert.Equal("rac.exe", tool.FileName);
        Assert.Equal("Утилита командной строки", tool.KindDisplayName);
        Assert.Equal("8.3.27.2170", tool.VersionText);
    }

    [Fact]
    public void FileName_ReturnsRasExecutableName()
    {
        var tool = new OneCAdministrationToolInfo
        {
            Kind = OneCAdministrationToolKind.Ras,
            FilePath = @"C:\Program Files\1cv8\8.3.27.2170\bin\ras.exe"
        };

        Assert.Equal("ras.exe", tool.FileName);
        Assert.Equal("Сервер администрирования", tool.KindDisplayName);
        Assert.Equal("—", tool.VersionText);
    }

    [Fact]
    public void FileName_ReturnsRagentExecutableName()
    {
        var tool = new OneCAdministrationToolInfo
        {
            Kind = OneCAdministrationToolKind.Ragent,
            FilePath = @"C:\Program Files\1cv8\8.3.27.2170\bin\ragent.exe",
            Version = "8.3.27.2170"
        };

        Assert.Equal("ragent.exe", tool.FileName);
        Assert.Equal("Агент сервера", tool.KindDisplayName);
        Assert.Equal("8.3.27.2170", tool.VersionText);
    }
}
