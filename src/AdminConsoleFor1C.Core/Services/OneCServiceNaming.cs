namespace AdminConsoleFor1C.Core.Services;

public static class OneCServiceNaming
{
    public static string BuildServerAgentName(string version, int agentPort)
    {
        if (!Version.TryParse(version, out var platformVersion) || platformVersion.Revision < 0)
            throw new ArgumentException("Не удалось определить полную версию сервера 1С.", nameof(version));
        if (agentPort is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(agentPort), "Порт агента должен быть от 1 до 65535.");

        return $"1C:Enterprise {platformVersion.Major}.{platformVersion.Minor} Server Agent {agentPort} {platformVersion}";
    }
}
