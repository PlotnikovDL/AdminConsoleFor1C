using System.Net;

namespace AdminConsoleFor1C.Core.Administration;

// Passwords are deliberately absent from the persisted connection profile.
public sealed record OneCServerConnectionProfile
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Name { get; init; }
    public required string Host { get; init; }
    public int AgentPort { get; init; } = 1540;
    public required string PlatformDirectory { get; init; }
    public required string PlatformVersion { get; init; }
    public string ClusterUser { get; init; } = string.Empty;

    public string AgentAddress => Host.Contains(':') ? $"[{Host}]:{AgentPort}" : $"{Host}:{AgentPort}";

    public void Validate()
    {
        if (Id == Guid.Empty || string.IsNullOrWhiteSpace(Name) || Name.Length > 200)
            throw new ArgumentException("Укажите название подключения (до 200 символов).");
        if (string.IsNullOrWhiteSpace(Host) || Host != Host.Trim() || Host.StartsWith('-')
            || (Uri.CheckHostName(Host) == UriHostNameType.Unknown && !IPAddress.TryParse(Host, out _)))
            throw new ArgumentException("Укажите имя сервера или IP-адрес без порта и протокола.");
        if (AgentPort is < 1 or > 65535)
            throw new ArgumentException("Порт агента должен быть от 1 до 65535.");
        if (!Version.TryParse(PlatformVersion, out var version) || version.Revision < 0
            || string.IsNullOrWhiteSpace(PlatformDirectory))
            throw new ArgumentException("Выберите полную версию платформы сервера и каталог её средств администрирования.");
    }
}
