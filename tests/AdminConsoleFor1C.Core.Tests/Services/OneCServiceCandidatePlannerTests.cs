using AdminConsoleFor1C.Core.Administration;
using AdminConsoleFor1C.Core.Services;

namespace AdminConsoleFor1C.Core.Tests.Services;

public sealed class OneCServiceCandidatePlannerTests
{
    [Fact]
    public void BuildServerAgentCandidates_ReturnsOnlyRagentTools()
    {
        var candidates = OneCServiceCandidatePlanner.BuildServerAgentCandidates(
            [
                CreateTool(OneCAdministrationToolKind.Rac, @"C:\Program Files\1cv8\8.3.28.1000\bin\rac.exe"),
                CreateTool(OneCAdministrationToolKind.Ras, @"C:\Program Files\1cv8\8.3.28.1000\bin\ras.exe"),
                CreateTool(OneCAdministrationToolKind.Ragent, @"C:\Program Files\1cv8\8.3.28.1000\bin\ragent.exe", "8.3.28.1000")
            ],
            []);

        var candidate = Assert.Single(candidates);
        Assert.Equal(@"C:\Program Files\1cv8\8.3.28.1000\bin\ragent.exe", candidate.ExecutablePath);
        Assert.Equal("8.3.28.1000", candidate.Version);
    }

    [Fact]
    public void BuildServerAgentCandidates_ExcludesAlreadyRegisteredServerAgentPath()
    {
        var candidates = OneCServiceCandidatePlanner.BuildServerAgentCandidates(
            [
                CreateTool(OneCAdministrationToolKind.Ragent, @"C:\Program Files\1cv8\8.3.27.2170\bin\ragent.exe", "8.3.27.2170"),
                CreateTool(OneCAdministrationToolKind.Ragent, @"C:\Program Files\1cv8\8.3.28.1000\bin\ragent.exe", "8.3.28.1000")
            ],
            [
                CreateService(@"c:\program files\1cv8\8.3.27.2170\bin\ragent.exe")
            ]);

        var candidate = Assert.Single(candidates);
        Assert.Equal(@"C:\Program Files\1cv8\8.3.28.1000\bin\ragent.exe", candidate.ExecutablePath);
    }

    [Fact]
    public void BuildServerAgentCandidates_OrdersByVersionDescendingAndDeduplicatesPaths()
    {
        var candidates = OneCServiceCandidatePlanner.BuildServerAgentCandidates(
            [
                CreateTool(OneCAdministrationToolKind.Ragent, @"C:\Program Files\1cv8\8.3.27.2170\bin\ragent.exe", "8.3.27.2170"),
                CreateTool(OneCAdministrationToolKind.Ragent, @"C:\Program Files\1cv8\8.3.28.1000\bin\ragent.exe", "8.3.28.1000"),
                CreateTool(OneCAdministrationToolKind.Ragent, @"C:\Program Files\1cv8\8.3.28.1000\bin\ragent.exe", "8.3.28.1000")
            ],
            []);

        Assert.Equal(2, candidates.Count);
        Assert.Equal("8.3.28.1000", candidates[0].Version);
        Assert.Equal("8.3.27.2170", candidates[1].Version);
    }

    private static OneCAdministrationToolInfo CreateTool(
        OneCAdministrationToolKind kind,
        string filePath,
        string? version = null)
    {
        return new OneCAdministrationToolInfo
        {
            Kind = kind,
            FilePath = filePath,
            Version = version
        };
    }

    private static OneCServiceInfo CreateService(string executablePath)
    {
        return new OneCServiceInfo
        {
            Name = "1C:Enterprise 8.3 Server Agent 1540",
            DisplayName = "1C:Enterprise 8.3 Server Agent",
            Kind = OneCServiceKind.ServerAgent,
            State = "Running",
            Status = "OK",
            ExecutablePath = executablePath
        };
    }
}
