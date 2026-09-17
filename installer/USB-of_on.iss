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

[Code]
const
  LinkCount = 6;

var
  LinkUrls: array[0..LinkCount - 1] of String;
  LinkTitles: array[0..LinkCount - 1] of String;

procedure InitLinks;
begin
  LinkTitles[0] := 'Boosty — поддержать разово или подпиской';
  LinkUrls[0] := 'https://boosty.to/aveharrisan';
  LinkTitles[1] := 'DonationAlerts — разовый донат';
  LinkUrls[1] := 'https://www.donationalerts.com/r/aveharrisan';
  LinkTitles[2] := 'AveHarrisan — телеграм автора';
  LinkUrls[2] := 'https://t.me/aveharrisan';
  LinkTitles[3] := 'Котамарин — канал про игры и раздачи';
  LinkUrls[3] := 'https://t.me/kotamarine';
  LinkTitles[4] := 'lvl.su — гайды и вики';
  LinkUrls[4] := 'https://lvl.su/';
  LinkTitles[5] := 'Discord — вопросы и ошибки';
  LinkUrls[5] := 'https://discord.com/invite/XYBvdvfv8t';
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

function IsRelaunch: Boolean;
var
  I: Integer;
begin
  Result := False;
  for I := 1 to ParamCount do
    if CompareText(ParamStr(I), '/RELAUNCH') = 0 then
      Result := True;
end;

{ Блок «Поддержать» и «Найти меня» со ссылками на странице мастера. }
procedure AddLinks(Parent: TWinControl; Left: Integer);
var
  I, Y: Integer;
  Caption, Link: TNewStaticText;
begin
  Y := Parent.ClientHeight - ScaleY(148);
  for I := 0 to LinkCount - 1 do
  begin
    if (I = 0) or (I = 2) then
    begin
      Caption := TNewStaticText.Create(WizardForm);
      Caption.Parent := Parent;
      if I = 0 then
        Caption.Caption := 'Поддержать'
      else
        Caption.Caption := 'Найти меня';
      Caption.Font.Style := [fsBold];
      Caption.Left := Left;
      Caption.Top := Y;
      Y := Y + ScaleY(18);
    end;
    Link := TNewStaticText.Create(WizardForm);
    Link.Parent := Parent;
    Link.Caption := LinkTitles[I];
    Link.Tag := I;
    Link.Cursor := crHand;
    Link.Font.Color := clHotLight;
    Link.Font.Style := [fsUnderline];
    Link.OnClick := @LinkClick;
    Link.Left := Left + ScaleX(12);
    Link.Top := Y;
    Y := Y + ScaleY(17);
    if I = 1 then
      Y := Y + ScaleY(8);
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

  { Полный список ссылок — внизу первой и последней страницы. }
  AddLinks(WizardForm.WelcomePage, WizardForm.WelcomeLabel2.Left);
  AddLinks(WizardForm.FinishedPage, WizardForm.FinishedLabel.Left);
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
