#ifndef PublishDirectory
  #error PublishDirectory is required
#endif
#ifndef ReleaseDirectory
  #error ReleaseDirectory is required
#endif
#ifndef ReleaseVersion
  #error ReleaseVersion is required
#endif
#ifndef InstallerId
  #define InstallerId "PlotnikovDL.AdminConsoleFor1C"
#endif
#ifndef InstallFolderName
  #define InstallFolderName "AdminConsoleFor1C"
#endif
#ifndef DisplayName
  #define DisplayName "Центр администрирования 1С"
#endif
#ifndef InstallerFileName
  #define InstallerFileName "AdminConsoleFor1C-" + ReleaseVersion + "-win-x64-setup"
#endif

[Setup]
AppId={#InstallerId}
AppName={#DisplayName}
AppVersion={#ReleaseVersion}
AppPublisher=PlotnikovDL
AppPublisherURL=https://github.com/PlotnikovDL/AdminConsoleFor1C
AppSupportURL=https://github.com/PlotnikovDL/AdminConsoleFor1C/issues
AppUpdatesURL=https://github.com/PlotnikovDL/AdminConsoleFor1C/releases
DefaultDirName={autopf}\{#InstallFolderName}
UsePreviousAppDir=yes
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir={#ReleaseDirectory}
OutputBaseFilename={#InstallerFileName}
SetupIconFile={#PublishDirectory}\Assets\AppIcon.ico
UninstallDisplayIcon={app}\AdminConsoleFor1C.App.exe
UninstallDisplayName={#DisplayName}
VersionInfoVersion={#ReleaseVersion}.0
VersionInfoDescription={#DisplayName}
VersionInfoCompany=PlotnikovDL
VersionInfoProductName=AdminConsoleFor1C
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
ChangesAssociations=no
ChangesEnvironment=no

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDirectory}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#DisplayName}"; Filename: "{app}\AdminConsoleFor1C.App.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\{#DisplayName}"; Filename: "{app}\AdminConsoleFor1C.App.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\AdminConsoleFor1C.App.exe"; Description: "{cm:LaunchProgram,{#DisplayName}}"; Flags: nowait postinstall skipifsilent runasoriginaluser

[CustomMessages]
russian.NewerVersion=Уже установлена более новая версия. Установка предыдущей версии отменена.
english.NewerVersion=A newer version is already installed. Downgrade was cancelled.

[Code]
function InitializeSetup(): Boolean;
var
  InstalledVersion: String;
  OldVersion, NewVersion: Int64;
begin
  Result := True;
  if RegQueryStringValue(HKLM64, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#InstallerId}_is1',
    'DisplayVersion', InstalledVersion) and StrToVersion(InstalledVersion, OldVersion) and
    StrToVersion('{#ReleaseVersion}', NewVersion) then
  begin
    if ComparePackedVersion(OldVersion, NewVersion) > 0 then
    begin
      SuppressibleMsgBox(CustomMessage('NewerVersion'), mbError, MB_OK, IDOK);
      Result := False;
    end;
  end;
end;
