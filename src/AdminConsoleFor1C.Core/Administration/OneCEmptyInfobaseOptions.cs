namespace AdminConsoleFor1C.Core.Administration;

// Credentials are passed separately and never become part of a saved model.
public sealed record OneCEmptyInfobaseOptions
{
    public required string Name { get; init; }
    public required string Dbms { get; init; }
    public required string DbServer { get; init; }
    public required string DbName { get; init; }
    public string DbUser { get; init; } = "";
    public string Description { get; init; } = "";
    public string Locale { get; init; } = "ru_RU";
    public int DateOffset { get; init; } = 2000;
    public int SecurityLevel { get; init; }
    public bool CreateDatabase { get; init; } = true;
    public bool ScheduledJobsDeny { get; init; }
    public bool LicenseDistribution { get; init; } = true;

    public void Validate()
    {
        Require(Name, "Имя информационной базы");
        if (Name.IndexOfAny([';', '"', '\\', '/']) >= 0)
            throw new ArgumentException("Имя информационной базы не должно содержать ;, кавычки или косую черту.");
        Require(DbServer, "Сервер баз данных");
        Require(DbName, "Имя базы данных");
        Require(Locale, "Язык (страна)");
        if (Dbms is not ("MSSQLServer" or "PostgreSQL" or "IBMDB2" or "OracleDatabase"))
            throw new ArgumentException("Выберите поддерживаемую СУБД.");
        if (DateOffset is not (0 or 2000)) throw new ArgumentException("Смещение дат: 0 или 2000.");
        if (SecurityLevel is < 0 or > 2) throw new ArgumentException("Выберите защищённое соединение.");
        foreach (var value in new[] { Name, DbServer, DbName, DbUser, Locale })
            if (value.Any(char.IsControl)) throw new ArgumentException("Поля подключения не должны содержать управляющие символы.");
    }

    private static void Require(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"Заполните поле «{label}».");
        if (value != value.Trim()) throw new ArgumentException($"Удалите пробелы по краям поля «{label}».");
    }
}

public sealed record OneCInfobaseCreationTarget(Guid ConnectionId, OneCClusterInfo Cluster);
