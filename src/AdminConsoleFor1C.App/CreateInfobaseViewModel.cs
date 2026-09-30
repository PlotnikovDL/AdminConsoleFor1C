using AdminConsoleFor1C.Core.Administration;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AdminConsoleFor1C.App;

public sealed partial class CreateInfobaseViewModel(OneCServerConnectionProfile profile, OneCClusterInfo cluster) : ObservableObject
{
    public string TargetText => $"Сервер 1С: {profile.Host} · Порт агента: {profile.AgentPort}\nПлатформа: {profile.PlatformVersion}\nКластер: {cluster.NameText} · {cluster.AddressText}";
    public IReadOnlyList<InfobaseOption> DbmsChoices { get; } = [new("MSSQLServer", "Microsoft SQL Server"), new("PostgreSQL", "PostgreSQL"), new("IBMDB2", "IBM DB2"), new("OracleDatabase", "Oracle Database")];
    public IReadOnlyList<InfobaseOption> SecurityChoices { get; } = [new("0", "Выключено"), new("1", "Только при установке соединения"), new("2", "Постоянно")];
    public IReadOnlyList<InfobaseOption> LocaleChoices { get; } = [new("ru_RU", "Русский (Россия)"), new("en_US", "Английский (США)"), new("be_BY", "Белорусский (Беларусь)"), new("kk_KZ", "Казахский (Казахстан)"), new("uk_UA", "Украинский (Украина)")];
    public IReadOnlyList<string> DateOffsets { get; } = ["0", "2000"];
    [ObservableProperty] public partial string Name { get; set; } = "";
    [ObservableProperty] public partial string DbName { get; set; } = "";
    [ObservableProperty] public partial string DbServer { get; set; } = "";
    [ObservableProperty] public partial string DbUser { get; set; } = "";
    [ObservableProperty] public partial string Description { get; set; } = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(UsesDateOffset))] public partial InfobaseOption? Dbms { get; set; }
    [ObservableProperty] public partial InfobaseOption? Security { get; set; }
    [ObservableProperty] public partial InfobaseOption? Locale { get; set; }
    [ObservableProperty] public partial string DateOffset { get; set; } = "2000";
    [ObservableProperty] public partial bool CreateDatabase { get; set; } = true;
    [ObservableProperty] public partial bool ScheduledJobsDeny { get; set; }
    [ObservableProperty] public partial bool LicenseDistribution { get; set; } = true;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasError))] public partial string? Error { get; set; }
    [ObservableProperty] public partial string ReviewText { get; set; } = "";
    public bool HasError => !string.IsNullOrWhiteSpace(Error);
    public bool UsesDateOffset => Dbms?.Value == "MSSQLServer";

    partial void OnDbmsChanged(InfobaseOption? value) => DateOffset = UsesDateOffset ? "2000" : "0";

    partial void OnNameChanged(string oldValue, string newValue)
    {
        if (DbName.Length == 0 || DbName == oldValue) DbName = newValue;
    }

    public void Initialize()
    {
        Dbms = DbmsChoices[0];
        Security = SecurityChoices[0];
        Locale = LocaleChoices[0];
    }

    public OneCEmptyInfobaseOptions? Prepare()
    {
        try
        {
            var options = new OneCEmptyInfobaseOptions
            {
                Name = Name.Trim(), DbName = DbName.Trim(), DbServer = DbServer.Trim(), DbUser = DbUser.Trim(),
                Dbms = Dbms?.Value ?? "", Locale = Locale?.Value ?? "", Description = Description,
                DateOffset = int.Parse(DateOffset), SecurityLevel = int.Parse(Security?.Value ?? "0"),
                CreateDatabase = CreateDatabase, ScheduledJobsDeny = ScheduledJobsDeny, LicenseDistribution = LicenseDistribution
            };
            options.Validate();
            ReviewText = $"Информационная база: {options.Name}\nКонфигурация: пустая база\n\n"
                + $"СУБД: {Dbms}\nСервер баз данных: {options.DbServer}\nБаза данных: {options.DbName}\n"
                + $"Пользователь СУБД: {(options.DbUser.Length == 0 ? "не указан" : options.DbUser)}\n"
                + $"Создать базу данных, если отсутствует: {(CreateDatabase ? "да" : "нет")}\n\n"
                + $"Язык (страна): {Locale}\nСмещение дат: {DateOffset}\nЗащищённое соединение: {Security}\n"
                + $"Регламентные задания: {(ScheduledJobsDeny ? "заблокированы" : "разрешены")}\n"
                + $"Выдача лицензий сервером: {(LicenseDistribution ? "разрешена" : "запрещена")}"
                + (Description.Length == 0 ? "" : $"\n\nОписание: {Description}");
            Error = null;
            return options;
        }
        catch (Exception ex) { Error = ex.Message; return null; }
    }
}

public sealed record InfobaseOption(string Value, string Label)
{
    public override string ToString() => Label;
}
