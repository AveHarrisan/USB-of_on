; Установщик USB-of_on. Версию передаёт сборка: ISCC /DAppVersion=1.2.3 USB-of_on.iss

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

#define AppName "USB-of_on"
#define AppExe "USB-of_on.exe"

[Setup]
AppId={{8B7F2E4C-3D1A-4B6E-9C2F-5A7D1E0B4C93}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=AveHarrisan
AppPublisherURL=https://t.me/aveharrisan
AppSupportURL=https://discord.com/invite/XYBvdvfv8t
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableWelcomePage=no
PrivilegesRequired=admin
; Программа 64-битная там, где это возможно: ставим в «Program Files», а не в «Program Files (x86)».
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\dist
OutputBaseFilename=USB-of_on-Setup
SetupIconFile=..\assets\icon.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
WizardStyle=modern
WizardImageFile=wizard.bmp,wizard@2x.bmp
WizardSmallImageFile=small.bmp,small@2x.bmp
Compression=lzma2
SolidCompression=yes
; Программа живёт в трее: установщик и деинсталлятор видят её по этому мьютексу.
AppMutex=Global\USB-of_on-running
CloseApplications=force
RestartApplications=no
VersionInfoVersion={#AppVersion}
VersionInfoProductName={#AppName}
VersionInfoDescription=Установщик {#AppName}

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Messages]
WelcomeLabel2=Программа установит [name/ver] на ваш компьютер.%n%nUSB-of_on показывает все USB-устройства, даёт им имена, скрывает лишние и включает или выключает нужные — например, токены ЭЦП разных организаций. Обновления программа получает сама.

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "..\src\USBofon\bin\Release\net48\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
; Обычная установка — галочка «Запустить» на последней странице.
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent runascurrentuser
; Обновление из программы (тихий режим с ключом /RELAUNCH) — открываем программу снова.
Filename: "{app}\{#AppExe}"; Flags: nowait runascurrentuser; Check: IsRelaunch

[UninstallRun]
; Автозапуск программа создаёт сама задачей Планировщика — убираем её вместе с программой.
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /TN ""{#AppName}"" /F"; Flags: runhidden; RunOnceId: "DeleteAutostartTask"

[Code]
const
  LinkCount = 7;

var
  LinkUrls: array[0..LinkCount - 1] of String;
  LinkTitles: array[0..LinkCount - 1] of String;

procedure InitLinks;
begin
  LinkTitles[0] := 'Boosty';
  LinkUrls[0] := 'https://boosty.to/aveharrisan';
  LinkTitles[1] := 'DonationAlerts';
  LinkUrls[1] := 'https://www.donationalerts.com/r/aveharrisan';
  LinkTitles[2] := 'Телеграм автора';
  LinkUrls[2] := 'https://t.me/aveharrisan';
  LinkTitles[3] := 'Котамарин — игры и раздачи';
  LinkUrls[3] := 'https://t.me/kotamarine';
  LinkTitles[4] := 'lvl.su — гайды и вики';
  LinkUrls[4] := 'https://lvl.su/';
  LinkTitles[5] := 'Discord — вопросы и ошибки';
  LinkUrls[5] := 'https://discord.com/invite/XYBvdvfv8t';
  LinkTitles[6] := 'GitHub — страница программы';
  LinkUrls[6] := 'https://github.com/AveHarrisan/USB-of_on';
end;

procedure OpenUrl(const Url: String);
var
  ErrorCode: Integer;
begin
  // Браузер открываем от имени пользователя, а не от администратора.
  ShellExecAsOriginalUser('open', Url, '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
end;

procedure LinkClick(Sender: TObject);
begin
  OpenUrl(LinkUrls[TComponent(Sender).Tag]);
end;

procedure SupportClick(Sender: TObject);
begin
  OpenUrl(LinkUrls[0]);
end;

procedure FindMeClick(Sender: TObject);
begin
  OpenUrl(LinkUrls[2]);
end;

{ Версии до 1.2.1 ставились в «Program Files (x86)». Такую копию удаляем тихо:
  сохранённые имена лежат в ProgramData и при тихом удалении не трогаются. }
procedure RemoveOld32BitCopy;
var
  Uninstaller: String;
  ResultCode: Integer;
begin
  if not Is64BitInstallMode then
    Exit;
  if RegQueryStringValue(HKLM32, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{8B7F2E4C-3D1A-4B6E-9C2F-5A7D1E0B4C93}_is1',
    'UninstallString', Uninstaller) then
    Exec(RemoveQuotes(Uninstaller), '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
    RemoveOld32BitCopy;
end;

function IsRelaunch: Boolean;
var
  I: Integer;
begin
  Result := False;
  for I := 1 to ParamCount do
    if CompareText(ParamStr(I), '/RELAUNCH') = 0 then
      Result := True;
end;

{ Ссылки в две колонки: слева «Поддержать», справа «Найти меня». }
procedure AddCaption(Parent: TWinControl; const Text: String; Left, Top: Integer);
var
  Caption: TNewStaticText;
begin
  Caption := TNewStaticText.Create(WizardForm);
  Caption.Parent := Parent;
  Caption.Caption := Text;
  Caption.Font.Style := [fsBold];
  Caption.Left := Left;
  Caption.Top := Top;
end;

procedure AddLinks(Parent: TWinControl; Left, Top: Integer);
var
  I, X, Y: Integer;
  Link: TNewStaticText;
begin
  AddCaption(Parent, 'Поддержать', Left, Top);
  AddCaption(Parent, 'Найти меня', Left + ScaleX(120), Top);
  for I := 0 to LinkCount - 1 do
  begin
    if I < 2 then
    begin
      X := Left;
      Y := Top + ScaleY(19) + I * ScaleY(17);
    end
    else
    begin
      X := Left + ScaleX(120);
      Y := Top + ScaleY(19) + (I - 2) * ScaleY(17);
    end;
    Link := TNewStaticText.Create(WizardForm);
    Link.Parent := Parent;
    Link.Caption := LinkTitles[I];
    Link.Tag := I;
    Link.Cursor := crHand;
    Link.Font.Color := clHotLight;
    Link.Font.Style := [fsUnderline];
    Link.OnClick := @LinkClick;
    Link.Left := X;
    Link.Top := Y;
  end;
end;

procedure InitializeWizard;
var
  Support, FindMe: TNewButton;
begin
  InitLinks;

  { Кнопки внизу мастера — видны на каждой странице. }
  Support := TNewButton.Create(WizardForm);
  Support.Parent := WizardForm;
  Support.Caption := '♥ Поддержать';
  Support.Left := ScaleX(10);
  Support.Top := WizardForm.CancelButton.Top;
  Support.Width := ScaleX(100);
  Support.Height := WizardForm.CancelButton.Height;
  Support.OnClick := @SupportClick;

  FindMe := TNewButton.Create(WizardForm);
  FindMe.Parent := WizardForm;
  FindMe.Caption := 'Найти меня';
  FindMe.Left := Support.Left + Support.Width + ScaleX(6);
  FindMe.Top := WizardForm.CancelButton.Top;
  FindMe.Width := ScaleX(90);
  FindMe.Height := WizardForm.CancelButton.Height;
  FindMe.OnClick := @FindMeClick;

  { Полный список ссылок — сразу под текстом первой страницы. Высоту текста
    считаем по нему самому: от масштаба экрана она меняется. }
  WizardForm.WelcomeLabel2.AdjustHeight;
  AddLinks(WizardForm.WelcomePage, WizardForm.WelcomeLabel2.Left,
    WizardForm.WelcomeLabel2.Top + WizardForm.WelcomeLabel2.Height + ScaleY(12));
end;

var
  FinishedLinksAdded: Boolean;

procedure CurPageChanged(CurPageID: Integer);
var
  Top: Integer;
begin
  { На последней странице мастер расставляет текст и галочку «Запустить» только при показе. }
  if (CurPageID = wpFinished) and not FinishedLinksAdded then
  begin
    FinishedLinksAdded := True;
    WizardForm.FinishedLabel.AdjustHeight;
    Top := WizardForm.FinishedLabel.Top + WizardForm.FinishedLabel.Height;
    if WizardForm.RunList.Visible then
      Top := WizardForm.RunList.Top + WizardForm.RunList.Items.Count * ScaleY(22);
    AddLinks(WizardForm.FinishedPage, WizardForm.FinishedLabel.Left, Top + ScaleY(12));
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and not UninstallSilent then
    if DirExists(ExpandConstant('{commonappdata}\{#AppName}')) then
      if MsgBox('Удалить и сохранённые имена устройств?' + #13#10#13#10 +
                'Если оставить, после повторной установки имена вернутся.',
                mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
        DelTree(ExpandConstant('{commonappdata}\{#AppName}'), True, True, True);
end;
