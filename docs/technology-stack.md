# Стек и архитектурные ориентиры

Этот документ фиксирует согласованный стек `Admin Console for 1C` и правила, на которые опираемся при дальнейшем развитии приложения.

## Базовая платформа

- Тип приложения: нативное Windows desktop-приложение для Windows 11.
- UI: WinUI 3.
- Runtime UI-платформы: Windows App SDK.
- Язык: C#.
- Разметка: XAML.
- Целевая платформа приложения: `net10.0-windows10.0.26100.0`.
- Минимальная версия Windows: `10.0.19041.0`.
- Архитектура сборки: `x64`, `win-x64`.
- Nullability и implicit usings включены.

Текущие версии пакетов:

- `Microsoft.WindowsAppSDK`: `1.8.260317003`.
- `Microsoft.Windows.SDK.BuildTools`: `10.0.26100.7705`.
- `Microsoft.Windows.SDK.BuildTools.WinApp`: `0.3.1`.

## UI и Windows 11

Интерфейс должен ощущаться продолжением Windows 11 Settings: спокойные списки, понятные карточки, стандартные WinUI-контролы, минимум декоративного шума.

Основные контролы и паттерны:

- `NavigationView` - основная левая навигация по разделам приложения.
- `BreadcrumbBar` - вложенность внутри разделов: например `Информационные базы > tradeCRM > Перенос`.
- `InfoBar` - ошибки, предупреждения и результат выполнения действий.
- `CommandBar`, `AppBarButton`, обычные `Button` с иконками - действия страницы и карточек.
- `ToggleSwitch` - двоичные состояния: вход пользователей, регламентные задания.
- `ContentDialog` - подтверждения и короткие мастера.
- `Expander` - редкие технические подробности, которые не нужны постоянно.
- `ListView`, `ItemsRepeater` или `TreeView` - списки и иерархии служб, кластеров, баз и сеансов.

Цвета статусов должны быть сдержанными:

- `Работает` - зеленый акцент без яркой заливки.
- `Остановлен` - нейтральный серый.
- `Без службы` - нейтральный информационный статус, без ощущения ошибки.
- Ошибки показываются через `InfoBar` и красный системный стиль, а не постоянной агрессивной окраской карточек.

## Разделы приложения

Целевая навигация:

- `Компоненты сервера` - службы Windows, агенты, процессы, `rac.exe`, `ras.exe`, RAS.
- `Информационные базы` - список ИБ, подробная карточка ИБ, перенос регистрации ИБ между агентами 1С.
- `Сеансы` - активные пользователи и сеансы, завершение сеансов.
- `Лицензии` - аппаратные, программные и занятые лицензии 1С.
- `Настройки` - параметры приложения, пути, диагностика и будущие профили.

Важное правило: карточки слева остаются навигационным списком и показывают только минимум для выбора. Подробности, команды и опасные действия живут в правой области или на отдельной странице.

## Архитектура проекта

Слои проекта:

- `AdminConsoleFor1C.Core` - доменные модели 1С, разбор командных строк, результат парсинга `rac`.
- `AdminConsoleFor1C.Application` - интерфейсы сценариев и портов приложения.
- `AdminConsoleFor1C.Infrastructure` - Windows Services, процессы, запуск `rac/ras`, интеграции с ОС и 1С.
- `AdminConsoleFor1C.App` - WinUI 3 приложение, XAML, view models, пользовательские сценарии.
- `AdminConsoleFor1C.ElevatedWorker` - отдельный процесс для действий, требующих прав администратора.
- `tests` - автоматические тесты, сейчас используется xUnit.

Правило зависимостей: UI вызывает сценарии через application/infrastructure-слой, а доменные модели не должны зависеть от WinUI, Windows Services или файловой системы.

## Администрирование 1С

Основные источники данных:

- Windows Services API - список и управление службами агента сервера 1С.
- Windows process inventory - процессы `ragent.exe`, `rmngr.exe`, `rphost.exe`, `dbgs.exe`, `ras.exe`.
- `rac.exe` - кластеры, рабочие серверы, информационные базы, сеансы, лицензии.
- `ras.exe` - сервер администрирования, который временно запускается, когда нужен доступ через `rac`.

Операции с ИБ:

- создание регистрации ИБ через `rac infobase create`;
- изменение ограничений ИБ;
- завершение сеансов;
- перенос регистрации ИБ на другой агент 1С без переноса данных СУБД;
- удаление старой регистрации ИБ без `--drop-database` и без `--clear-database`, если пользователь явно не согласовал опасный сценарий.

## Повышенные действия

Все операции, требующие прав администратора, должны выполняться через отдельный elevated worker:

- регистрация службы Windows;
- запуск, остановка и перезапуск службы;
- удаление службы;
- будущие действия, меняющие системные настройки.

Обычный UI-процесс не должен незаметно требовать постоянного запуска от администратора.

## Хранение данных

Именование без пробелов:

- EXE: `AdminConsoleFor1C.exe`.
- Техническая папка: `AdminConsoleFor1C`.

Пользовательские настройки:

- `%AppData%\AdminConsoleFor1C\settings.json`
- `%AppData%\AdminConsoleFor1C\profiles.json`
- `%AppData%\AdminConsoleFor1C\repositories.json`

Локальные данные:

- `%LocalAppData%\AdminConsoleFor1C\Logs`
- `%LocalAppData%\AdminConsoleFor1C\Cache`
- `%LocalAppData%\AdminConsoleFor1C\Temp`
- `%LocalAppData%\AdminConsoleFor1C\Packages`

Общие данные компьютера:

- `%ProgramData%\AdminConsoleFor1C`

## Установка и распространение

Основной формат установки: MSIX.

Целевой канал распространения:

- GitHub Releases - публикация MSIX/пакета релиза.
- winget - установка через Windows Package Manager.
- UniGetUI - установка через доступный источник winget.

Идея: пользователь должен находить приложение как обычный Windows-инструмент, устанавливать одной командой или через GUI, а не запускать набор скриптов.

## Рабочий процесс разработки

Основная команда запуска в разработке:

```powershell
dotnet run --project C:\projects\1c-tools\AdminConsoleFor1C\src\AdminConsoleFor1C.App\AdminConsoleFor1C.App.csproj --launch-profile "Admin Console for 1C (Package)"
```

Проверка перед UI-коммитом:

```powershell
dotnet build C:\projects\1c-tools\AdminConsoleFor1C\AdminConsoleFor1C.slnx
```

После сборки нужно запустить приложение и визуально проверить основной экран, диалоги и состояния ошибок.

Коммиты пишутся на русском языке, в прошедшем времени.

## Официальные ориентиры

- Windows app design: https://learn.microsoft.com/en-us/windows/apps/design/
- Design basics for Windows apps: https://learn.microsoft.com/en-us/windows/apps/design/basics/
- Windows App SDK: https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/
- WinUI 3: https://learn.microsoft.com/windows/apps/winui/winui3/
- WinUI controls and patterns: https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/
- NavigationView: https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/navigationview
- App settings guidelines: https://learn.microsoft.com/en-us/windows/apps/design/app-settings/guidelines-for-app-settings
- MSIX app packages: https://learn.microsoft.com/en-us/windows/msix/overview
- Windows Package Manager manifests: https://learn.microsoft.com/en-us/windows/package-manager/package/manifest
- Fluent 2 design system: https://fluent2.microsoft.design/
