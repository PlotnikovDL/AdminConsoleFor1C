using System.Globalization;
using AdminConsoleFor1C.Core.Administration;
using AdminConsoleFor1C.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace AdminConsoleFor1C.App;

public sealed class OneCServerAgentSetupViewModel
{
    public OneCServerAgentSetupViewModel(
        OneCAdministrationToolDiagnosticsViewModel administrationTools,
        IReadOnlyList<OneCServiceInfo> services,
        IReadOnlyList<OneCProcessInfo> processes)
    {
        var candidates = GetCandidates(
            administrationTools.RagentTools,
            services,
            processes);
        var versionColumnWidth = CalculateVersionColumnWidth(candidates);
        foreach (var candidate in candidates)
        {
            candidate.SetVersionColumnWidth(versionColumnWidth);
        }

        Candidates = candidates;
    }

    public IReadOnlyList<OneCServerAgentSetupCandidateViewModel> Candidates { get; }

    public bool IsVisible => Candidates.Count > 0;

    public Visibility Visibility => IsVisible ? Visibility.Visible : Visibility.Collapsed;

    public string TitleText => "Агенты сервера 1С без службы Windows";

    private static IReadOnlyList<OneCServerAgentSetupCandidateViewModel> GetCandidates(
        IReadOnlyList<OneCAdministrationToolInfo> ragentTools,
        IReadOnlyList<OneCServiceInfo> services,
        IReadOnlyList<OneCProcessInfo> processes)
    {
        var candidates = new List<OneCServerAgentSetupCandidateViewModel>();
        var usedPorts = GetUsedPorts(services, processes);

        foreach (var ragentTool in GetDistinctRagentTools(ragentTools))
        {
            if (IsRegisteredAsService(ragentTool, services))
            {
                continue;
            }

            var runningProcess = GetRunningServerAgentProcess(ragentTool, processes);
            var portPlan = runningProcess is null
                ? GetPortPlan(usedPorts)
                : OneCServerAgentPortPlan.FromProcess(runningProcess);
            candidates.Add(new OneCServerAgentSetupCandidateViewModel(
                ragentTool,
                portPlan,
                runningProcess,
                GetSuggestedServiceDataDirectory(services, portPlan.AgentPort),
                processes));
        }

        return candidates;
    }

    private static IEnumerable<OneCAdministrationToolInfo> GetDistinctRagentTools(
        IReadOnlyList<OneCAdministrationToolInfo> ragentTools)
    {
        return ragentTools
            .GroupBy(static tool => string.IsNullOrWhiteSpace(tool.Version)
                ? tool.FilePath
                : tool.Version,
                StringComparer.OrdinalIgnoreCase)
            .Select(static group => group
                .OrderByDescending(static tool => ParseVersion(tool.Version))
                .ThenBy(static tool => tool.FilePath, StringComparer.OrdinalIgnoreCase)
                .First())
            .OrderBy(static tool => string.IsNullOrWhiteSpace(tool.Version))
            .ThenBy(static tool => ParseVersion(tool.Version))
            .ThenBy(static tool => tool.FilePath, StringComparer.OrdinalIgnoreCase);
    }

    private static double CalculateVersionColumnWidth(
        IReadOnlyList<OneCServerAgentSetupCandidateViewModel> candidates)
    {
        var maxWidth = candidates
            .Select(static candidate => MeasureVersionTextWidth(candidate.VersionText))
            .DefaultIfEmpty(0)
            .Max();

        return Math.Ceiling(maxWidth + 8);
    }

    private static double MeasureVersionTextWidth(string text)
    {
        var textBlock = new TextBlock
        {
            Text = text,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            LineHeight = 20
        };

        textBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return textBlock.DesiredSize.Width;
    }

    private static bool IsRegisteredAsService(
        OneCAdministrationToolInfo ragentTool,
        IReadOnlyList<OneCServiceInfo> services)
    {
        return services.Any(service => service.Kind == OneCServiceKind.ServerAgent
            && (HasSameVersion(service.Version, ragentTool.Version)
                || HasSamePath(service.ExecutablePath, ragentTool.FilePath)));
    }

    private static OneCProcessInfo? GetRunningServerAgentProcess(
        OneCAdministrationToolInfo ragentTool,
        IReadOnlyList<OneCProcessInfo> processes)
    {
        return processes.FirstOrDefault(process => process.Kind == OneCProcessKind.ServerAgent
            && (HasSameVersion(process.Version, ragentTool.Version)
                || HasSamePath(process.ExecutablePath, ragentTool.FilePath)));
    }

    private static string GetSuggestedServiceDataDirectory(
        IReadOnlyList<OneCServiceInfo> services,
        int agentPort)
    {
        foreach (var service in services.Where(static service => service.Kind == OneCServiceKind.ServerAgent))
        {
            if (string.IsNullOrWhiteSpace(service.DataDirectory) || service.AgentPort is null)
            {
                continue;
            }

            var directory = service.DataDirectory.Trim();
            var currentDirectoryName = Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (!string.Equals(
                    currentDirectoryName,
                    service.AgentPort.Value.ToString(CultureInfo.InvariantCulture),
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parent = Path.GetDirectoryName(directory);
            if (!string.IsNullOrWhiteSpace(parent))
            {
                return Path.Combine(parent, agentPort.ToString(CultureInfo.InvariantCulture));
            }
        }

        foreach (var directory in GetKnownServiceDataDirectories(agentPort))
        {
            if (Directory.Exists(directory))
            {
                return directory;
            }
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "1C",
            "1cv8",
            "srvinfo",
            agentPort.ToString(CultureInfo.InvariantCulture));
    }

    private static IEnumerable<string> GetKnownServiceDataDirectories(int agentPort)
    {
        var directoryName = agentPort.ToString(CultureInfo.InvariantCulture);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            yield return Path.Combine(programFiles, "1cv8", "srvinfo", directoryName);
        }

        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrWhiteSpace(programFilesX86)
            && !string.Equals(programFilesX86, programFiles, StringComparison.OrdinalIgnoreCase))
        {
            yield return Path.Combine(programFilesX86, "1cv8", "srvinfo", directoryName);
        }
    }

    private static OneCServerAgentPortPlan GetPortPlan(HashSet<int> usedPorts)
    {
        for (var instanceIndex = 1; instanceIndex < 100; instanceIndex++)
        {
            var candidate = OneCServerAgentPortPlan.Create(instanceIndex);
            if (candidate.GetServerAgentServicePorts().All(port => !usedPorts.Contains(port)))
            {
                return candidate;
            }
        }

        return OneCServerAgentPortPlan.Create(1);
    }

    private static HashSet<int> GetUsedPorts(
        IReadOnlyList<OneCServiceInfo> services,
        IReadOnlyList<OneCProcessInfo> processes)
    {
        var usedPorts = new HashSet<int>();
        foreach (var service in services)
        {
            AddPort(usedPorts, service.AgentPort);
            AddPort(usedPorts, service.RegPort);
            AddPort(usedPorts, service.AdministrationServerPort);
            AddPort(usedPorts, service.DebugServerPort);
            AddPortRange(usedPorts, service.PortRange);
        }

        foreach (var process in processes)
        {
            AddPort(usedPorts, process.AgentPort);
            AddPort(usedPorts, process.ClusterPort);
            AddPort(usedPorts, process.WorkerPort);
            AddPort(usedPorts, process.AdministrationServerPort);
            AddPort(usedPorts, process.DebugServerPort);
            AddPortRange(usedPorts, process.PortRange);
        }

        return usedPorts;
    }

    private static void AddPort(HashSet<int> usedPorts, int? port)
    {
        if (port is > 0)
        {
            usedPorts.Add(port.Value);
        }
    }

    private static void AddPortRange(HashSet<int> usedPorts, string? portRange)
    {
        if (string.IsNullOrWhiteSpace(portRange))
        {
            return;
        }

        var parts = portRange.Split([':', '-'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2
            || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var start)
            || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var end))
        {
            return;
        }

        if (start > end)
        {
            (start, end) = (end, start);
        }

        for (var port = start; port <= end; port++)
        {
            usedPorts.Add(port);
        }
    }

    private static bool HasSameVersion(string? left, string? right)
    {
        return !string.IsNullOrWhiteSpace(left)
            && !string.IsNullOrWhiteSpace(right)
            && string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasSamePath(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        try
        {
            return string.Equals(
                Path.GetFullPath(left),
                Path.GetFullPath(right),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static Version ParseVersion(string? version)
    {
        return Version.TryParse(version, out var parsed)
            ? parsed
            : new Version();
    }
}

public sealed class OneCServerAgentSetupCandidateViewModel
{
    private readonly OneCAdministrationToolInfo _ragentTool;

    public OneCServerAgentSetupCandidateViewModel(
        OneCAdministrationToolInfo ragentTool,
        OneCServerAgentPortPlan portPlan,
        OneCProcessInfo? runningProcess,
        string suggestedServiceDataDirectory,
        IReadOnlyList<OneCProcessInfo> processes)
    {
        _ragentTool = ragentTool;
        PortPlan = portPlan;
        SuggestedServiceDataDirectory = suggestedServiceDataDirectory;
        DataDirectory = string.IsNullOrWhiteSpace(runningProcess?.DataDirectory)
            ? SuggestedServiceDataDirectory
            : runningProcess.DataDirectory;
        IsRunningWithoutService = processes.Any(process => process.Kind == OneCProcessKind.ServerAgent
            && (HasSameVersion(process.Version, ragentTool.Version)
                || HasSamePath(process.ExecutablePath, ragentTool.FilePath)));
    }

    public OneCAdministrationToolInfo RagentTool => _ragentTool;

    public OneCServerAgentPortPlan PortPlan { get; }

    public string DataDirectory { get; }

    public string SuggestedServiceDataDirectory { get; }

    public bool IsRunningWithoutService { get; }

    public string VersionText => _ragentTool.VersionText;

    public double VersionColumnWidth { get; private set; }

    public string RagentPathText => _ragentTool.FilePathText;

    public string ExpectedServiceNameText => $"1C:Enterprise {VersionFamilyText} Server Agent {AgentPortText} {VersionText}";

    public string AgentPortText => PortPlan.AgentPort.ToString(CultureInfo.InvariantCulture);

    public string ClusterPortText => PortPlan.ClusterPort.ToString(CultureInfo.InvariantCulture);

    public string ProcessRangeText => PortPlan.ProcessRange;

    public string DebugServerPortText => PortPlan.DebugServerPort.ToString(CultureInfo.InvariantCulture);

    private string VersionFamilyText
    {
        get
        {
            var parts = VersionText.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return parts.Length >= 2
                ? string.Create(CultureInfo.InvariantCulture, $"{parts[0]}.{parts[1]}")
                : "8";
        }
    }

    internal void SetVersionColumnWidth(double width)
    {
        VersionColumnWidth = width;
    }

    private static bool HasSameVersion(string? left, string? right)
    {
        return !string.IsNullOrWhiteSpace(left)
            && !string.IsNullOrWhiteSpace(right)
            && string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasSamePath(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        try
        {
            return string.Equals(
                Path.GetFullPath(left),
                Path.GetFullPath(right),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }

}

public sealed record OneCServerAgentPortPlan(
    int InstanceIndex,
    int AgentPort,
    int ClusterPort,
    int RasPort,
    int DebugServerPort,
    int ProcessRangeStart,
    int ProcessRangeEnd)
{
    public string ProcessRange => string.Create(
        CultureInfo.InvariantCulture,
        $"{ProcessRangeStart}:{ProcessRangeEnd}");

    public static OneCServerAgentPortPlan Create(int instanceIndex)
    {
        var prefix = instanceIndex * 1000;
        return new OneCServerAgentPortPlan(
            instanceIndex,
            prefix + 540,
            prefix + 541,
            prefix + 545,
            prefix + 550,
            prefix + 560,
            prefix + 591);
    }

    public static OneCServerAgentPortPlan FromProcess(OneCProcessInfo process)
    {
        var agentPort = process.AgentPort ?? 1540;
        var fallback = CreateFromAgentPort(agentPort);
        var (processRangeStart, processRangeEnd) = TryParseRange(process.PortRange, out var start, out var end)
            ? (start, end)
            : (fallback.ProcessRangeStart, fallback.ProcessRangeEnd);

        return new OneCServerAgentPortPlan(
            fallback.InstanceIndex,
            agentPort,
            process.ClusterPort ?? fallback.ClusterPort,
            fallback.RasPort,
            process.DebugServerPort ?? fallback.DebugServerPort,
            processRangeStart,
            processRangeEnd);
    }

    private static OneCServerAgentPortPlan CreateFromAgentPort(int agentPort)
    {
        return agentPort >= 1540 && agentPort % 1000 == 540
            ? Create(agentPort / 1000)
            : Create(1);
    }

    private static bool TryParseRange(string? value, out int start, out int end)
    {
        start = 0;
        end = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Split([':', '-'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 2
            && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out start)
            && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out end);
    }

    public IEnumerable<int> GetServerAgentServicePorts()
    {
        yield return AgentPort;
        yield return ClusterPort;
        yield return DebugServerPort;

        for (var port = ProcessRangeStart; port <= ProcessRangeEnd; port++)
        {
            yield return port;
        }
    }
}
