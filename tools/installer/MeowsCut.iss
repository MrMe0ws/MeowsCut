; Установщик Meows Cut.
;
; Собирается скриптом tools/build-installer.ps1: он сначала делает portable-папку,
; потом отдаёт её сюда. Отдельно этот файл запускать не нужно — без параметров
; SourceDir и AppVersion он не соберётся.
;
; Установка идёт в профиль пользователя (PrivilegesRequired=lowest): прав
; администратора у пользователя может не быть, а редактору видео они не нужны.

#ifndef SourceDir
  #error Задайте SourceDir: путь к готовой portable-папке
#endif

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

#define AppName "Meows Cut"
#define AppPublisher "Meows Cut"
#define AppExeName "MeowsCut.exe"

[Setup]
AppId={{8F3C2A41-7D65-4B18-9C0E-2E7A5D1F3B44}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputBaseFilename=MeowsCut-{#AppVersion}-setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#AppExeName}
SetupIconFile={#SourceDir}\..\..\src\MeowsCut.App\Assets\app.ico

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "Ярлык на рабочем столе"; GroupDescription: "Дополнительно:"
Name: "associate"; Description: "Открывать видеофайлы через Meows Cut (пункт «Открыть с помощью»)"; GroupDescription: "Дополнительно:"; Flags: unchecked

[Files]
; Вся portable-папка целиком, включая FFmpeg рядом с exe: локатор находит его
; вторым шагом, и после установки приложение ничего не докачивает.
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Registry]
; Пункт «Открыть с помощью» для частых видеоформатов. Ассоциации по умолчанию
; не перехватываем: подменять системный проигрыватель редактор не должен.
Root: HKCU; Subkey: "Software\Classes\Applications\{#AppExeName}\shell\open\command"; \
    ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExeName}"" ""%1"""; \
    Flags: uninsdeletekey; Tasks: associate
Root: HKCU; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; \
    ValueType: string; ValueName: ".mp4"; ValueData: ""; Flags: uninsdeletekey; Tasks: associate
Root: HKCU; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; \
    ValueType: string; ValueName: ".mov"; ValueData: ""; Tasks: associate
Root: HKCU; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; \
    ValueType: string; ValueName: ".mkv"; ValueData: ""; Tasks: associate
Root: HKCU; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; \
    ValueType: string; ValueName: ".webm"; ValueData: ""; Tasks: associate
Root: HKCU; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; \
    ValueType: string; ValueName: ".avi"; ValueData: ""; Tasks: associate

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Запустить Meows Cut"; \
    Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Логи и кэш кадров создаются приложением уже после установки, и без этого
; после удаления в профиле остаётся мусор. Пресеты пользователя не трогаем:
; их писали руками, и вернуть их будет неоткуда.
Type: filesandordirs; Name: "{localappdata}\MeowsCut\logs"
Type: filesandordirs; Name: "{localappdata}\MeowsCut\thumbnails"
Type: filesandordirs; Name: "{localappdata}\MeowsCut\temp"
