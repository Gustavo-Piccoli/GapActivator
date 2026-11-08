; GapActivator per-machine installer (administrator rights). Compile with ISCC and /D defines:
;   AppVersion, NumericVersion, Arch (x64|arm64), SourceDir, OutputDir
; SourceDir holds GapActivator.exe, its native *.dll files and LICENSE.txt at the root, and the
; service publish in a Service subfolder.
#ifndef AppVersion
  #error AppVersion must be defined (/DAppVersion=1.2.3)
#endif
#ifndef NumericVersion
  #error NumericVersion must be defined (/DNumericVersion=1.2.3)
#endif
#ifndef Arch
  #error Arch must be defined (/DArch=x64 or /DArch=arm64)
#endif
#ifndef SourceDir
  #error SourceDir must be defined (/DSourceDir=<publish dir>)
#endif
#ifndef OutputDir
  #error OutputDir must be defined (/DOutputDir=<output dir>)
#endif

[Setup]
AppId={{7B865882-D696-4BD1-ACFF-34BDE62B9FDD}
AppName=GapActivator
AppVersion={#AppVersion}
AppPublisher=Gustavo Auad Piccoli
AppCopyright=Copyright (c) 2026 Gustavo Auad Piccoli
AppPublisherURL=https://github.com/Gustavo-Piccoli/GapActivator
AppSupportURL=https://github.com/Gustavo-Piccoli/GapActivator
AppUpdatesURL=https://github.com/Gustavo-Piccoli/GapActivator
DefaultDirName={autopf}\GapActivator
DisableProgramGroupPage=yes
PrivilegesRequired=admin
MinVersion=10.0
OutputDir={#OutputDir}
OutputBaseFilename=GapActivator-Setup-{#AppVersion}-{#Arch}
SetupIconFile=..\src\GapActivator.App\Assets\Icons\gapactivator.ico
UninstallDisplayIcon={app}\GapActivator.exe
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
AppMutex=Local\GapActivator.App.SingleInstance
VersionInfoVersion={#NumericVersion}
#if Arch == "x64"
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#elif Arch == "arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#else
  #error Arch must be x64 or arm64
#endif

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[CustomMessages]
english.AccountPageCaption=Service account
brazilianportuguese.AccountPageCaption=Conta do serviço
english.AccountPageDescription=Choose the account that the GapActivator service runs under.
brazilianportuguese.AccountPageDescription=Escolha a conta sob a qual o serviço do GapActivator será executado.
english.AccountVirtual=Virtual service account NT SERVICE\GapActivator (recommended)
brazilianportuguese.AccountVirtual=Conta de serviço virtual NT SERVICE\GapActivator (recomendada)
english.AccountSpecific=A specific account
brazilianportuguese.AccountSpecific=Uma conta específica
english.AccountNameLabel=Account name (DOMAIN\user):
brazilianportuguese.AccountNameLabel=Nome da conta (DOMÍNIO\usuário):
english.AccountPasswordLabel=Password:
brazilianportuguese.AccountPasswordLabel=Senha:
english.OperatorsOn=Operators mode is on: Administrators and members of the local group GapActivator Operators can change processes and settings. Everyone else can view.
brazilianportuguese.OperatorsOn=Modo Operators ativado: Administradores e membros do grupo local GapActivator Operators podem alterar processos e configurações. Os demais usuários podem apenas visualizar.
english.OperatorsOff=Operators mode is off: only Administrators can change processes and settings. This mode is used when the service runs under a specific account, because whoever changes what it runs gets that account's rights. Everyone else can view.
brazilianportuguese.OperatorsOff=Modo Operators desativado: somente Administradores podem alterar processos e configurações. Esse modo é usado quando o serviço é executado com uma conta específica, porque quem altera o que ele executa obtém os direitos dessa conta. Os demais usuários podem apenas visualizar.
english.AccountNameRequired=Enter the name of the account.
brazilianportuguese.AccountNameRequired=Informe o nome da conta.
english.AccountLogonFailed=Windows did not accept this account name and password (error %1: %2). Check them and try again.
brazilianportuguese.AccountLogonFailed=O Windows não aceitou este nome de conta e esta senha (erro %1: %2). Verifique-os e tente novamente.
english.StopTimeout=The GapActivator service did not stop within 11 minutes. It may be waiting for running processes to finish. Wait for them to finish or stop the service manually, then try again.
brazilianportuguese.StopTimeout=O serviço do GapActivator não parou em 11 minutos. Ele pode estar aguardando o término de processos em execução. Aguarde o término deles ou pare o serviço manualmente e tente novamente.
english.StepFailed=Setup could not complete this step: %1.%n%nExit code: %2%n%3
brazilianportuguese.StepFailed=O instalador não conseguiu concluir esta etapa: %1.%n%nCódigo de saída: %2%n%3
english.StepOriginalUser=finding the user who is installing
brazilianportuguese.StepOriginalUser=identificar o usuário que está instalando
english.StepCreateService=creating the service
brazilianportuguese.StepCreateService=criar o serviço
english.StepServiceAccount=setting the service account
brazilianportuguese.StepServiceAccount=definir a conta do serviço
english.StepUpdateService=updating the service
brazilianportuguese.StepUpdateService=atualizar o serviço
english.StepConfigureService=configuring the service
brazilianportuguese.StepConfigureService=configurar o serviço
english.StepLogonRight=granting the Log on as a service right
brazilianportuguese.StepLogonRight=conceder o direito Fazer logon como um serviço
english.StepDataRoot=preparing the data folder
brazilianportuguese.StepDataRoot=preparar a pasta de dados
english.StepEventSource=registering the event source
brazilianportuguese.StepEventSource=registrar a origem de eventos
english.StepOperators=creating the GapActivator Operators group
brazilianportuguese.StepOperators=criar o grupo GapActivator Operators
english.StepRemoveOperators=removing the GapActivator Operators group
brazilianportuguese.StepRemoveOperators=remover o grupo GapActivator Operators
english.StepStartService=starting the service
brazilianportuguese.StepStartService=iniciar o serviço
english.StepUnregisterEventSource=removing the event source
brazilianportuguese.StepUnregisterEventSource=remover a origem de eventos
english.StepDeleteService=removing the service
brazilianportuguese.StepDeleteService=remover o serviço
english.DeleteDataPrompt=Also delete GapActivator's data (database, logs and run output)?
brazilianportuguese.DeleteDataPrompt=Excluir também os dados do GapActivator (banco de dados, logs e saída das execuções)?
english.CreateStartMenuIcon=Create a Start Menu shortcut
brazilianportuguese.CreateStartMenuIcon=Criar um atalho no Menu Iniciar
english.LaunchNow=Launch GapActivator now
brazilianportuguese.LaunchNow=Iniciar o GapActivator agora

[Tasks]
; CreateDesktopIcon and AdditionalIcons are built-in messages of both language files.
Name: desktopicon; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: startmenuicon; Description: "{cm:CreateStartMenuIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[InstallDelete]
; An upgrade replaces the service and app files wholesale, so a file dropped by a newer build never lingers.
Type: filesandordirs; Name: "{app}\Service\*"
Type: files; Name: "{app}\*.dll"

[Files]
Source: "{#SourceDir}\GapActivator.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\*.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\LICENSE.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\Service\*"; DestDir: "{app}\Service"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\GapActivator"; Filename: "{app}\GapActivator.exe"; Tasks: startmenuicon
Name: "{autodesktop}\GapActivator"; Filename: "{app}\GapActivator.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\GapActivator.exe"; Description: "{cm:LaunchNow}"; Flags: nowait postinstall skipifsilent runasoriginaluser

[Code]
// The service and its recovery settings are created in ssPostInstall (after the files are in place) and removed
// in usUninstall (while the service executable still exists). The specific-account password lives only in this
// script's memory: it is validated with LogonUserW and set with ChangeServiceConfigW, and it is never put on a
// command line, in a file or in the log.

const
  ServiceName = 'GapActivator';
  VirtualAccount = 'NT SERVICE\GapActivator';
  ServiceRegKey = 'SYSTEM\CurrentControlSet\Services\GapActivator';
  // The service may wait for running processes to stop: 1320 polls of 500 ms are 11 minutes.
  StopPollCount = 1320;
  StopPollMs = 500;
  // Exit code of Setup (and of the uninstaller) when a service step fails. Inno uses 1 to 8 itself.
  ExitPostInstallFailed = 10;
  SC_MANAGER_CONNECT = 1;
  SERVICE_CHANGE_CONFIG = 2;
  SERVICE_QUERY_STATUS = 4;
  SERVICE_STOPPED = 1;
  LOGON32_LOGON_NETWORK = 3;
  LOGON32_PROVIDER_DEFAULT = 0;

type
  TServiceStatus = record
    dwServiceType: Cardinal;
    dwCurrentState: Cardinal;
    dwControlsAccepted: Cardinal;
    dwWin32ExitCode: Cardinal;
    dwServiceSpecificExitCode: Cardinal;
    dwCheckPoint: Cardinal;
    dwWaitHint: Cardinal;
  end;

// Unicode Windows API imports. The Cardinal parameters named "Null" are always passed 0, so the
// API receives NULL where it expects an optional pointer.
function OpenSCManagerW(lpMachineNameNull, lpDatabaseNameNull, dwDesiredAccess: Cardinal): Cardinal;
external 'OpenSCManagerW@advapi32.dll stdcall';

function OpenServiceW(hSCManager: Cardinal; lpServiceName: String; dwDesiredAccess: Cardinal): Cardinal;
external 'OpenServiceW@advapi32.dll stdcall';

function QueryServiceStatus(hService: Cardinal; var lpServiceStatus: TServiceStatus): Longint;
external 'QueryServiceStatus@advapi32.dll stdcall';

function ChangeServiceConfigW(hService: Cardinal; dwServiceType, dwStartType, dwErrorControl: Longint;
  lpBinaryPathNameNull, lpLoadOrderGroupNull, lpdwTagIdNull, lpDependenciesNull: Cardinal;
  lpServiceStartName, lpPassword: String; lpDisplayNameNull: Cardinal): Longint;
external 'ChangeServiceConfigW@advapi32.dll stdcall';

// The same function, changing only the binary path (every other argument is NO_CHANGE or NULL).
function ChangeServiceBinaryPathW(hService: Cardinal; dwServiceType, dwStartType, dwErrorControl: Longint;
  lpBinaryPathName: String; lpLoadOrderGroupNull, lpdwTagIdNull, lpDependenciesNull, lpServiceStartNameNull,
  lpPasswordNull, lpDisplayNameNull: Cardinal): Longint;
external 'ChangeServiceConfigW@advapi32.dll stdcall';

function CloseServiceHandle(hSCObject: Cardinal): Longint;
external 'CloseServiceHandle@advapi32.dll stdcall';

// The user of the interactive session Setup runs in. Setup is a 32-bit process, so pointers are Cardinal.
function GetCurrentProcessId: Cardinal;
external 'GetCurrentProcessId@kernel32.dll stdcall';

function ProcessIdToSessionId(dwProcessId: Cardinal; var pSessionId: Cardinal): Longint;
external 'ProcessIdToSessionId@kernel32.dll stdcall';

function WTSQuerySessionInformationW(hServer, SessionId, WTSInfoClass: Cardinal; var ppBuffer: Cardinal;
  var pBytesReturned: Cardinal): Longint;
external 'WTSQuerySessionInformationW@wtsapi32.dll stdcall';

procedure WTSFreeMemory(pMemory: Cardinal);
external 'WTSFreeMemory@wtsapi32.dll stdcall';

// Copies a NUL-terminated UTF-16 string from a raw pointer into a String buffer.
function lstrcpynW(lpString1: String; lpString2: Cardinal; iMaxLength: Longint): Cardinal;
external 'lstrcpynW@kernel32.dll stdcall';

// The DOMAIN\user name (NameSamCompatible = 2) of the account Setup itself runs as.
// It returns a one-byte BOOLEAN, so the result is declared as a Byte (the upper bytes of EAX are not defined).
function GetUserNameExW(NameFormat: Longint; lpNameBuffer: String; var nSize: Cardinal): Byte;
external 'GetUserNameExW@secur32.dll stdcall';

// The SID of the account Setup runs as, to tell LocalSystem apart by SID instead of by name.
function GetCurrentProcess: Cardinal;
external 'GetCurrentProcess@kernel32.dll stdcall';

function OpenProcessToken(ProcessHandle, DesiredAccess: Cardinal; var TokenHandle: Cardinal): Longint;
external 'OpenProcessToken@advapi32.dll stdcall';

function GetTokenInformation(TokenHandle, TokenInformationClass: Cardinal; TokenInformation: String;
  TokenInformationLength: Cardinal; var ReturnLength: Cardinal): Longint;
external 'GetTokenInformation@advapi32.dll stdcall';

function ConvertSidToStringSidW(Sid: Cardinal; var StringSid: Cardinal): Longint;
external 'ConvertSidToStringSidW@advapi32.dll stdcall';

function LocalFree(hMem: Cardinal): Cardinal;
external 'LocalFree@kernel32.dll stdcall';

function LogonUserW(lpszUsername, lpszDomain, lpszPassword: String; dwLogonType, dwLogonProvider: Cardinal;
  var phToken: Cardinal): Longint;
external 'LogonUserW@advapi32.dll stdcall';

// The same function with a NULL domain, for a user principal name (user@domain).
function LogonUserUpnW(lpszUsername: String; lpszDomainNull: Cardinal; lpszPassword: String;
  dwLogonType, dwLogonProvider: Cardinal; var phToken: Cardinal): Longint;
external 'LogonUserW@advapi32.dll stdcall';

function CloseHandle(hObject: Cardinal): Longint;
external 'CloseHandle@kernel32.dll stdcall';

function SetEnvironmentVariableW(lpName: String; lpValueNull: Cardinal): Longint;
external 'SetEnvironmentVariableW@kernel32.dll stdcall';

// Abort and exceptions raised in CurStepChanged do not change Setup's exit code (verified: it stays 0), so a
// failed post-install step ends the process with its own exit code instead.
procedure ExitProcess(uExitCode: Cardinal);
external 'ExitProcess@kernel32.dll stdcall';

var
  AccountPage: TWizardPage;
  VirtualRadio, SpecificRadio: TNewRadioButton;
  NameLabel, PasswordLabel, OperatorsNote: TNewStaticText;
  NameEdit: TNewEdit;
  PasswordEdit: TPasswordEdit;
  IsUpgrade: Boolean;
  UseSpecific: Boolean;
  SpecificAccount, SpecificPassword: String;
  OriginalUser: String;
  OriginalUserLoaded: Boolean;
  PostError: String;
  CommandLineRead, CommandLineHasAccount: Boolean;

function HasSwitch(const Name: String): Boolean;
var
  I: Integer;
begin
  Result := False;
  for I := 1 to ParamCount do
    if CompareText(ParamStr(I), Name) = 0 then
      Result := True;
end;

function SysDir(const FileName: String): String;
begin
  Result := ExpandConstant('{sys}\') + FileName;
end;

// Runs a program through cmd.exe so its output lands in a temp file, logs the output and returns the exit code
// (-1 when the program could not be started). Never pass a password here: the parameters are logged.
function RunCaptured(const Exe, Params: String; var Output: String): Integer;
var
  OutFile: String;
  Text: AnsiString;
  Code: Integer;
begin
  Output := '';
  OutFile := ExpandConstant('{tmp}\gapactivator-step.txt');
  DeleteFile(OutFile);
  Log('Running: ' + Exe + ' ' + Params);
  if Exec(ExpandConstant('{cmd}'), '/S /C ""' + Exe + '" ' + Params + ' > "' + OutFile + '" 2>&1"', '', SW_HIDE,
    ewWaitUntilTerminated, Code) then
    Result := Code
  else
    Result := -1;
  if LoadStringFromFile(OutFile, Text) then
    Output := Trim(String(Text));
  DeleteFile(OutFile);
  Log('Exit code ' + IntToStr(Result) + '. Output: ' + Output);
end;

function Sc(const Params: String; var Output: String): Integer;
begin
  Result := RunCaptured(SysDir('sc.exe'), Params, Output);
end;

function ServiceTool(const Params: String; var Output: String): Integer;
begin
  Result := RunCaptured(ExpandConstant('{app}\Service\GapActivator.Service.exe'), Params, Output);
end;

// A String from a pointer returned by a Windows API call.
function PointerToString(P: Cardinal): String;
var
  Buffer: String;
  N: Integer;
begin
  SetLength(Buffer, 512);
  lstrcpynW(Buffer, P, 512);
  N := Pos(#0, Buffer);
  if N > 0 then
    SetLength(Buffer, N - 1);
  Result := Buffer;
end;

// One string of the interactive session Setup runs in (WTSUserName = 5, WTSDomainName = 7).
function QuerySessionString(SessionId, InfoClass: Cardinal): String;
var
  Buffer, Bytes: Cardinal;
begin
  Result := '';
  Buffer := 0;
  Bytes := 0;
  // WTS_CURRENT_SERVER_HANDLE is 0.
  if WTSQuerySessionInformationW(0, SessionId, InfoClass, Buffer, Bytes) <> 0 then
  begin
    if Buffer <> 0 then
    begin
      Result := PointerToString(Buffer);
      WTSFreeMemory(Buffer);
    end;
  end;
end;

// DOMAIN\user of the account Setup itself runs as; the fallback when no interactive session has a user (for
// example a runner service in session 0).
function GetProcessUserName: String;
var
  Buffer: String;
  Size: Cardinal;
begin
  Result := '';
  Size := 256;
  SetLength(Buffer, 256);
  if GetUserNameExW(2, Buffer, Size) <> 0 then
    Result := Copy(Buffer, 1, Size);
end;

// The SID string of the account Setup runs as ('' on failure), read from the process token (TokenUser = 1,
// TOKEN_QUERY = 8). TOKEN_USER starts with the SID pointer; the buffer is a String used as raw memory.
function GetProcessSid: String;
var
  Token, Needed, SidPointer, SidString: Cardinal;
  Buffer: String;
begin
  Result := '';
  Token := 0;
  if OpenProcessToken(GetCurrentProcess, 8, Token) = 0 then
    exit;
  try
    SetLength(Buffer, 128);
    Needed := 0;
    if GetTokenInformation(Token, 1, Buffer, 256, Needed) <> 0 then
    begin
      // The first four bytes (two UTF-16 units) are the 32-bit pointer to the SID.
      SidPointer := Ord(Buffer[1]) + Ord(Buffer[2]) * 65536;
      SidString := 0;
      if ConvertSidToStringSidW(SidPointer, SidString) <> 0 then
      begin
        Result := PointerToString(SidString);
        LocalFree(SidString);
      end;
    end;
  finally
    CloseHandle(Token);
  end;
end;

// DOMAIN\user of the user who started Setup, from the WTS session of this process. This works when Setup was
// elevated with another administrator's credentials (the user's temp folder is not reachable then), and the
// name arrives as Unicode, so accented names survive.
function GetOriginalUser: String;
var
  SessionId: Cardinal;
  User, Domain: String;
begin
  if not OriginalUserLoaded then
  begin
    OriginalUserLoaded := True;
    OriginalUser := '';
    SessionId := 0;
    if ProcessIdToSessionId(GetCurrentProcessId, SessionId) <> 0 then
    begin
      User := QuerySessionString(SessionId, 5);
      Domain := QuerySessionString(SessionId, 7);
      if User <> '' then
      begin
        if Domain <> '' then
          OriginalUser := Domain + '\' + User
        else
          OriginalUser := User;
      end;
    end;
    if OriginalUser = '' then
    begin
      // No interactive user. A deployment tool runs Setup as LocalSystem (S-1-5-18, whose name reads as the
      // machine account PC$); neither is a person to add to the Operators group, so there is no installing user.
      if GetProcessSid = 'S-1-5-18' then
        Log('Setup runs as LocalSystem and no user is logged on in this session.')
      else
      begin
        OriginalUser := GetProcessUserName;
        if (OriginalUser <> '') and (Copy(OriginalUser, Length(OriginalUser), 1) = '$') then
        begin
          Log('Setup runs as the machine account ' + OriginalUser + '.');
          OriginalUser := '';
        end;
      end;
    end;
    Log('Installing user: ' + OriginalUser);
  end;
  Result := OriginalUser;
end;

// SERVICE_STOPPED when the service is stopped or does not exist; otherwise its current state.
function GetServiceState: Cardinal;
var
  Manager, Service: Cardinal;
  Status: TServiceStatus;
begin
  Result := SERVICE_STOPPED;
  Manager := OpenSCManagerW(0, 0, SC_MANAGER_CONNECT);
  if Manager = 0 then
    exit;
  try
    Service := OpenServiceW(Manager, ServiceName, SERVICE_QUERY_STATUS);
    if Service <> 0 then
    begin
      try
        if QueryServiceStatus(Service, Status) <> 0 then
          Result := Status.dwCurrentState;
      finally
        CloseServiceHandle(Service);
      end;
    end;
  finally
    CloseServiceHandle(Manager);
  end;
end;

// Asks the service to stop and waits up to 11 minutes. The state comes from the Service Control Manager API
// instead of the text of "sc query", so it does not depend on the display language.
function StopServiceAndWait: Boolean;
var
  I: Integer;
  Output: String;
begin
  Result := GetServiceState = SERVICE_STOPPED;
  if Result then
    exit;
  Log('Stopping the GapActivator service.');
  Sc('stop ' + ServiceName, Output);
  for I := 1 to StopPollCount do
  begin
    if GetServiceState = SERVICE_STOPPED then
    begin
      Result := True;
      break;
    end;
    // A stop request can be lost while the service is still starting; ask again every 30 seconds.
    if (I mod 60) = 0 then
      Sc('stop ' + ServiceName, Output);
    Sleep(StopPollMs);
  end;
  if Result then
    Sleep(1000)
  else
    Log('The GapActivator service did not stop within 11 minutes.');
end;

function IsBuiltinAccount(const Account: String): Boolean;
begin
  Result := (Pos('NT SERVICE\', Uppercase(Account)) = 1) or (Pos('NT AUTHORITY\', Uppercase(Account)) = 1)
    or (Pos('BUILTIN\', Uppercase(Account)) = 1) or (CompareText(Account, 'LocalSystem') = 0);
end;

// The account the installed service runs under, as the Service Control Manager stores it.
function GetInstalledAccount: String;
begin
  if not RegQueryStringValue(HKLM64, ServiceRegKey, 'ObjectName', Result) then
    Result := VirtualAccount;
  if CompareText(Result, 'LocalSystem') = 0 then
    Result := 'NT AUTHORITY\SYSTEM';
end;

// A bare name (no DOMAIN\ and no @) means an account of this machine. ChangeServiceConfigW needs the .\ form,
// so the name is normalized once, before the logon check and before it reaches the setup verbs.
function NormalizeAccount(const Account: String): String;
begin
  Result := Account;
  if (Result <> '') and (Pos('\', Result) = 0) and (Pos('@', Result) = 0) then
    Result := '.\' + Result;
end;

// Checks the name and password with a network logon. Err is the Win32 error code on failure.
function CheckLogon(const Account, Password: String; var Err: Longint): Boolean;
var
  Token: Cardinal;
  P: Integer;
  Ok: Longint;
begin
  Token := 0;
  P := Pos('\', Account);
  if P > 0 then
    Ok := LogonUserW(Copy(Account, P + 1, Length(Account)), Copy(Account, 1, P - 1), Password,
      LOGON32_LOGON_NETWORK, LOGON32_PROVIDER_DEFAULT, Token)
  else if Pos('@', Account) > 0 then
    Ok := LogonUserUpnW(Account, 0, Password, LOGON32_LOGON_NETWORK, LOGON32_PROVIDER_DEFAULT, Token)
  else
    Ok := LogonUserW(Account, '.', Password, LOGON32_LOGON_NETWORK, LOGON32_PROVIDER_DEFAULT, Token);
  Result := Ok <> 0;
  if Result then
  begin
    Err := 0;
    CloseHandle(Token);
  end
  else
    Err := DLLGetLastError;
end;

// Sets the account and password of the installed service without a command line.
function SetServiceAccount(const Account, Password: String; var Err: Longint): Boolean;
var
  Manager, Service: Cardinal;
begin
  Result := False;
  Err := 0;
  Manager := OpenSCManagerW(0, 0, SC_MANAGER_CONNECT);
  if Manager = 0 then
  begin
    Err := DLLGetLastError;
    exit;
  end;
  try
    Service := OpenServiceW(Manager, ServiceName, SERVICE_CHANGE_CONFIG);
    if Service = 0 then
    begin
      Err := DLLGetLastError;
      exit;
    end;
    try
      // -1 is SERVICE_NO_CHANGE (0xFFFFFFFF) for the type, start type and error control.
      if ChangeServiceConfigW(Service, -1, -1, -1, 0, 0, 0, 0, Account, Password, 0) = 0 then
        Err := DLLGetLastError
      else
        Result := True;
    finally
      CloseServiceHandle(Service);
    end;
  finally
    CloseServiceHandle(Manager);
  end;
end;

// Sets the (quoted) binary path of the installed service through the API; sc.exe would strip the quotes.
function SetServiceBinaryPath(const QuotedPath: String; var Err: Longint): Boolean;
var
  Manager, Service: Cardinal;
begin
  Result := False;
  Err := 0;
  Manager := OpenSCManagerW(0, 0, SC_MANAGER_CONNECT);
  if Manager = 0 then
  begin
    Err := DLLGetLastError;
    exit;
  end;
  try
    Service := OpenServiceW(Manager, ServiceName, SERVICE_CHANGE_CONFIG);
    if Service = 0 then
    begin
      Err := DLLGetLastError;
      exit;
    end;
    try
      if ChangeServiceBinaryPathW(Service, -1, -1, -1, QuotedPath, 0, 0, 0, 0, 0, 0) = 0 then
        Err := DLLGetLastError
      else
        Result := True;
    finally
      CloseServiceHandle(Service);
    end;
  finally
    CloseServiceHandle(Manager);
  end;
end;

function StepMessage(const StepKey: String; Code: Integer; const Output: String): String;
begin
  Result := FmtMessage(CustomMessage('StepFailed'), [CustomMessage(StepKey), IntToStr(Code), Output]);
end;

// Reads /ACCOUNT and the password once (the environment variable is cleared by the first read).
function ReadCommandLineAccount: Boolean;
begin
  if CommandLineRead then
  begin
    Result := CommandLineHasAccount;
    exit;
  end;
  CommandLineRead := True;
  SpecificAccount := NormalizeAccount(Trim(ExpandConstant('{param:ACCOUNT|}')));
  // Setup's own log records its command line, so /PASSWORD ends up in /LOG output. The environment variable is
  // the way to keep the password out of both; /PASSWORD wins when both are given.
  SpecificPassword := ExpandConstant('{param:PASSWORD|}');
  if SpecificPassword = '' then
    SpecificPassword := GetEnv('GAPACTIVATOR_SETUP_PASSWORD');
  // Do not hand the password to the child processes Setup starts, whichever way it was given.
  SetEnvironmentVariableW('GAPACTIVATOR_SETUP_PASSWORD', 0);
  CommandLineHasAccount := SpecificAccount <> '';
  Result := CommandLineHasAccount;
end;

procedure UpdateAccountPage;
begin
  NameLabel.Enabled := SpecificRadio.Checked;
  NameEdit.Enabled := SpecificRadio.Checked;
  PasswordLabel.Enabled := SpecificRadio.Checked;
  PasswordEdit.Enabled := SpecificRadio.Checked;
  if SpecificRadio.Checked then
    OperatorsNote.Caption := CustomMessage('OperatorsOff')
  else
    OperatorsNote.Caption := CustomMessage('OperatorsOn');
end;

procedure AccountRadioClick(Sender: TObject);
begin
  UpdateAccountPage;
end;

function InitializeSetup: Boolean;
begin
  // Read the command line first: this also clears the password variable before any child process starts.
  ReadCommandLineAccount;
  IsUpgrade := RegKeyExists(HKLM64, ServiceRegKey);
  Result := True;
end;

procedure InitializeWizard;
begin
  AccountPage := CreateCustomPage(wpSelectDir, CustomMessage('AccountPageCaption'),
    CustomMessage('AccountPageDescription'));

  VirtualRadio := TNewRadioButton.Create(AccountPage);
  VirtualRadio.Parent := AccountPage.Surface;
  VirtualRadio.Left := 0;
  VirtualRadio.Top := 0;
  VirtualRadio.Width := AccountPage.SurfaceWidth;
  VirtualRadio.Caption := CustomMessage('AccountVirtual');
  VirtualRadio.Checked := True;
  VirtualRadio.OnClick := @AccountRadioClick;

  SpecificRadio := TNewRadioButton.Create(AccountPage);
  SpecificRadio.Parent := AccountPage.Surface;
  SpecificRadio.Left := 0;
  SpecificRadio.Top := ScaleY(22);
  SpecificRadio.Width := AccountPage.SurfaceWidth;
  SpecificRadio.Caption := CustomMessage('AccountSpecific');
  SpecificRadio.OnClick := @AccountRadioClick;

  NameLabel := TNewStaticText.Create(AccountPage);
  NameLabel.Parent := AccountPage.Surface;
  NameLabel.Left := ScaleX(18);
  NameLabel.Top := ScaleY(48);
  NameLabel.Caption := CustomMessage('AccountNameLabel');

  NameEdit := TNewEdit.Create(AccountPage);
  NameEdit.Parent := AccountPage.Surface;
  NameEdit.Left := ScaleX(18);
  NameEdit.Top := ScaleY(64);
  NameEdit.Width := AccountPage.SurfaceWidth - ScaleX(18);
  NameEdit.Text := GetOriginalUser;

  PasswordLabel := TNewStaticText.Create(AccountPage);
  PasswordLabel.Parent := AccountPage.Surface;
  PasswordLabel.Left := ScaleX(18);
  PasswordLabel.Top := ScaleY(90);
  PasswordLabel.Caption := CustomMessage('AccountPasswordLabel');

  PasswordEdit := TPasswordEdit.Create(AccountPage);
  PasswordEdit.Parent := AccountPage.Surface;
  PasswordEdit.Left := ScaleX(18);
  PasswordEdit.Top := ScaleY(106);
  PasswordEdit.Width := AccountPage.SurfaceWidth - ScaleX(18);

  OperatorsNote := TNewStaticText.Create(AccountPage);
  OperatorsNote.Parent := AccountPage.Surface;
  OperatorsNote.Left := 0;
  OperatorsNote.Top := ScaleY(138);
  OperatorsNote.Width := AccountPage.SurfaceWidth;
  OperatorsNote.Height := ScaleY(64);
  OperatorsNote.AutoSize := False;
  OperatorsNote.WordWrap := True;

  UpdateAccountPage;

  // Silent installs use the virtual account unless /ACCOUNT=<name> and /PASSWORD=<pw> are given.
  if ReadCommandLineAccount then
  begin
    NameEdit.Text := SpecificAccount;
    PasswordEdit.Text := SpecificPassword;
    SpecificRadio.Checked := True;
    UpdateAccountPage;
  end;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := (AccountPage <> nil) and (PageID = AccountPage.ID) and IsUpgrade;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  Err: Longint;
begin
  Result := True;
  // A silent Setup simulates Next on every page. A message box here would block an unattended run
  // (/SUPPRESSMSGBOXES does not suppress MsgBox), so PrepareToInstall validates a silent run instead.
  if WizardSilent then
    exit;
  if (AccountPage <> nil) and (CurPageID = AccountPage.ID) and SpecificRadio.Checked then
  begin
    SpecificAccount := NormalizeAccount(Trim(NameEdit.Text));
    SpecificPassword := PasswordEdit.Text;
    if SpecificAccount = '' then
    begin
      MsgBox(CustomMessage('AccountNameRequired'), mbError, MB_OK);
      Result := False;
    end
    else if not CheckLogon(SpecificAccount, SpecificPassword, Err) then
    begin
      MsgBox(FmtMessage(CustomMessage('AccountLogonFailed'), [IntToStr(Err), SysErrorMessage(Err)]), mbError, MB_OK);
      Result := False;
    end;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Err: Longint;
begin
  Result := '';
  if IsUpgrade then
  begin
    if not StopServiceAndWait then
      Result := CustomMessage('StopTimeout');
    exit;
  end;

  // A new install: take the choice from the page, or from the command line when Setup is silent.
  if WizardSilent then
    UseSpecific := ReadCommandLineAccount
  else
    UseSpecific := SpecificRadio.Checked;
  if UseSpecific then
  begin
    SpecificAccount := NormalizeAccount(SpecificAccount);
    if SpecificAccount = '' then
      Result := CustomMessage('AccountNameRequired')
    else if WizardSilent and not CheckLogon(SpecificAccount, SpecificPassword, Err) then
      Result := FmtMessage(CustomMessage('AccountLogonFailed'), [IntToStr(Err), SysErrorMessage(Err)]);
  end;
end;

// Runs the post-install steps in order. On failure PostError holds the localized message.
procedure RunPostInstall;
var
  Output, Account, User: String;
  Code: Integer;
  Err: Longint;
  GrantRight: Boolean;
begin
  PostError := '';
  User := '';
  if IsUpgrade then
  begin
    Account := GetInstalledAccount;
    GrantRight := not IsBuiltinAccount(Account);
  end
  else if UseSpecific then
  begin
    Account := SpecificAccount;
    GrantRight := True;
  end
  else
  begin
    Account := VirtualAccount;
    GrantRight := False;
    User := GetOriginalUser;
  end;

  // 1. Create the service (new install), or re-apply the start type of an existing one (upgrade). sc.exe strips
  // the quotes from binPath= before it stores the path, and an unquoted path with a space can be hijacked, so
  // the quoted path is always set through the API afterwards (on upgrades too, which repairs older installs).
  if IsUpgrade then
  begin
    Code := Sc('config ' + ServiceName + ' start= delayed-auto', Output);
    if Code <> 0 then
    begin
      PostError := StepMessage('StepUpdateService', Code, Output);
      exit;
    end;
  end
  else
  begin
    Code := Sc('create ' + ServiceName + ' binPath= "' + ExpandConstant('{app}\Service\GapActivator.Service.exe')
      + '" start= delayed-auto obj= "' + VirtualAccount + '" DisplayName= ' + ServiceName, Output);
    if Code <> 0 then
    begin
      PostError := StepMessage('StepCreateService', Code, Output);
      exit;
    end;
  end;
  if not SetServiceBinaryPath('"' + ExpandConstant('{app}\Service\GapActivator.Service.exe') + '"', Err) then
  begin
    PostError := StepMessage('StepUpdateService', Err, SysErrorMessage(Err));
    exit;
  end;
  if not IsUpgrade then
  begin
    if UseSpecific then
    begin
      Log('Setting the service account to ' + SpecificAccount + ' with ChangeServiceConfigW.');
      if not SetServiceAccount(SpecificAccount, SpecificPassword, Err) then
      begin
        PostError := StepMessage('StepServiceAccount', Err, SysErrorMessage(Err));
        exit;
      end;
    end;
  end;

  // 2. Description and recovery settings. These also run on an upgrade, so they are always re-applied.
  Code := Sc('description ' + ServiceName + ' "Runs the GapActivator scheduled processes."', Output);
  if Code = 0 then
    Code := Sc('failure ' + ServiceName + ' reset= 86400 actions= restart/60000/restart/60000/restart/60000', Output);
  if Code = 0 then
    Code := Sc('failureflag ' + ServiceName + ' 1', Output);
  if Code <> 0 then
  begin
    PostError := StepMessage('StepConfigureService', Code, Output);
    exit;
  end;

  // 3. The Log on as a service right, for a specific account only.
  if GrantRight then
  begin
    Code := ServiceTool('setup grant-logon-right --account "' + Account + '"', Output);
    if Code <> 0 then
    begin
      PostError := StepMessage('StepLogonRight', Code, Output);
      exit;
    end;
  end;

  // 4. The data root.
  Code := ServiceTool('setup prepare-data-root --account "' + Account + '"', Output);
  if Code <> 0 then
  begin
    PostError := StepMessage('StepDataRoot', Code, Output);
    exit;
  end;

  // 5. The event source.
  Code := ServiceTool('setup register-event-source', Output);
  if Code <> 0 then
  begin
    PostError := StepMessage('StepEventSource', Code, Output);
    exit;
  end;

  // 6. The Operators group, in virtual-account mode on a new install. The verb creates the group when it is
  // missing and adds the user; an existing group or membership is success.
  if (not IsUpgrade) and (not UseSpecific) then
  begin
    if User = '' then
    begin
      Log('No interactive user to add: the GapActivator Operators group is created without members.');
      Code := ServiceTool('setup add-operator', Output);
    end
    else
      Code := ServiceTool('setup add-operator --account "' + User + '"', Output);
    if Code <> 0 then
    begin
      PostError := StepMessage('StepOperators', Code, Output);
      exit;
    end;
  end;

  // 7. Start the service.
  Code := Sc('start ' + ServiceName, Output);
  if Code <> 0 then
  begin
    PostError := StepMessage('StepStartService', Code, Output);
    exit;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Output: String;
begin
  if CurStep = ssPostInstall then
  begin
    RunPostInstall;
    if PostError <> '' then
    begin
      Log('Post-install failed: ' + PostError);
      // A failed new install must not leave a half-configured service behind.
      if not IsUpgrade then
      begin
        Sc('stop ' + ServiceName, Output);
        Sc('delete ' + ServiceName, Output);
      end
      else
      begin
        // The upgrade stopped the service; try to bring it back. The result is only logged.
        Log('Upgrade failed; starting the service again, sc start exit code: ' + IntToStr(Sc('start ' + ServiceName, Output)));
      end;
      if not WizardSilent then
        MsgBox(PostError, mbCriticalError, MB_OK);
      ExitProcess(ExitPostInstallFailed);
    end;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Output: String;
  Code: Integer;
  DataRoot: String;
begin
  if CurUninstallStep = usUninstall then
  begin
    // 1. Stop the service and wait for it.
    if not StopServiceAndWait then
    begin
      if not UninstallSilent then
        MsgBox(CustomMessage('StopTimeout'), mbCriticalError, MB_OK);
      ExitProcess(ExitPostInstallFailed);
    end;

    // 2. Delete the service. 1060 (does not exist) and 1072 (already marked for deletion) are fine.
    Code := Sc('delete ' + ServiceName, Output);
    if (Code <> 0) and (Code <> 1060) and (Code <> 1072) then
    begin
      Log('sc delete failed: ' + IntToStr(Code));
      if not UninstallSilent then
        MsgBox(StepMessage('StepDeleteService', Code, Output), mbError, MB_OK);
    end;

    // 3. Unregister the event source while the service executable still exists.
    Code := ServiceTool('setup unregister-event-source', Output);
    if Code <> 0 then
    begin
      Log('unregister-event-source failed: ' + IntToStr(Code));
      if not UninstallSilent then
        MsgBox(StepMessage('StepUnregisterEventSource', Code, Output), mbError, MB_OK);
    end;

    // 4. Delete the Operators group (a missing group is success).
    Code := ServiceTool('setup remove-operators-group', Output);
    if Code <> 0 then
    begin
      Log('remove-operators-group failed: ' + IntToStr(Code));
      if not UninstallSilent then
        MsgBox(StepMessage('StepRemoveOperators', Code, Output), mbError, MB_OK);
    end;

    // 5. The shortcuts are removed by the uninstaller itself. The Log on as a service right is never removed.
  end
  else if CurUninstallStep = usPostUninstall then
  begin
    // 6. The data stays unless the user opts in (or /DELETEDATA is given).
    DataRoot := ExpandConstant('{commonappdata}\GapActivator');
    if DirExists(DataRoot) then
    begin
      if HasSwitch('/DELETEDATA') or ((not UninstallSilent) and
        (SuppressibleMsgBox(CustomMessage('DeleteDataPrompt'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2, IDNO) = IDYES)) then
      begin
        Log('Deleting ' + DataRoot);
        if not DelTree(DataRoot, True, True, True) then
          Log('Could not delete ' + DataRoot);
      end;
    end;
  end;
end;
