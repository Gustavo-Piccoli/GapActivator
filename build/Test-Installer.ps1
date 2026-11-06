[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$InstallerPath,
    [int]$ServiceStartTimeoutSeconds = 60
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Installs, upgrades and uninstalls GapActivator for real: it creates a Windows service, a local group, an event
# source and a data folder. It runs in CI (elevated runner). Service state is read from the registry and from CIM
# wherever possible; the sc.exe text checks (qc, qfailure, qfailureflag) are in English and need an English OS.

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Test-Installer.ps1 must run elevated (as Administrator): it installs and removes a Windows service, a local group, an event source and the data folder.'
}
if (-not [Environment]::Is64BitProcess) { throw 'Run this script in a 64-bit PowerShell: it reads the 64-bit registry view.' }

if (-not (Test-Path -LiteralPath $InstallerPath -PathType Leaf)) { throw "$InstallerPath not found" }
$installer = (Resolve-Path -LiteralPath $InstallerPath).Path
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$logDir = Join-Path $repoRoot 'artifacts\installer-test'
if (Test-Path -LiteralPath $logDir) { Remove-Item -LiteralPath $logDir -Recurse -Force }
New-Item -ItemType Directory -Path $logDir | Out-Null

$serviceName = 'GapActivator'
$virtualAccount = 'NT SERVICE\GapActivator'
$groupName = 'GapActivator Operators'
$installDir = Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'GapActivator'
$dataRoot = Join-Path ([Environment]::GetFolderPath('CommonApplicationData')) 'GapActivator'
$serviceKey = "HKLM:\SYSTEM\CurrentControlSet\Services\$serviceName"
$eventSourceKey = "HKLM:\SYSTEM\CurrentControlSet\Services\EventLog\Application\$serviceName"
$uninstallKey = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{7B865882-D696-4BD1-ACFF-34BDE62B9FDD}_is1'
$startMenuShortcut = Join-Path ([Environment]::GetFolderPath('CommonPrograms')) 'GapActivator.lnk'
$desktopShortcut = Join-Path ([Environment]::GetFolderPath('CommonDesktopDirectory')) 'GapActivator.lnk'
$currentUser = [Security.Principal.WindowsIdentity]::GetCurrent().Name

function Assert([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw "Assertion failed: $Message" }
}

function Wait-Until([scriptblock]$Condition, [int]$TimeoutSeconds) {
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ($true) {
        if (& $Condition) { return $true }
        if ([DateTime]::UtcNow -ge $deadline) { return $false }
        Start-Sleep -Milliseconds 500
    }
}

function Test-GroupExists {
    try { Get-LocalGroup -Name $groupName -ErrorAction Stop | Out-Null; $true } catch { $false }
}

function Test-ServiceRegistered { Test-Path -LiteralPath $serviceKey }

function Get-ServiceStatus {
    $service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
    if ($null -eq $service) { return $null }
    [string]$service.Status
}

function Test-PipeExists {
    # Listing the pipe directory can throw on Windows PowerShell 5.1 (odd pipe names); fall back to a direct probe.
    $pipe = "\\.\pipe\$serviceName"
    try { if ([IO.Directory]::GetFiles('\\.\pipe\') -contains $pipe) { return $true } } catch { }
    try { return [IO.File]::Exists($pipe) } catch { return $false }
}

function Get-ServiceProcessId {
    $cim = Get-CimInstance -ClassName Win32_Service -Filter "Name='$serviceName'"
    if ($null -eq $cim) { return 0 }
    [int]$cim.ProcessId
}

# The Inno logs contain no password in a passing run. When a run fails, the logs are printed with the test password masked.
$secretToMask = $null
function Show-Logs {
    foreach ($file in Get-ChildItem -LiteralPath $logDir -File | Sort-Object Name) {
        Write-Host "---- $($file.Name) ----"
        $text = Get-Content -LiteralPath $file.FullName -Raw
        if ($secretToMask) { $text = $text.Replace($secretToMask, '***') }
        Write-Host $text
        Write-Host "---- end of $($file.Name) ----"
    }
}

# Runs the installer silently and returns its exit code. The password, when given, travels in an environment
# variable: Setup's own log records its command line, so /PASSWORD= would put it in the log.
function Invoke-Install([string]$Name, [string[]]$ExtraArguments = @(), [string]$Password = $null, [int[]]$ExpectedExit = @(0)) {
    $log = Join-Path $logDir "$Name.log"
    $arguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/LOG=`"$log`"") + $ExtraArguments
    if ($Password) { $env:GAPACTIVATOR_SETUP_PASSWORD = $Password }
    try {
        $process = Start-Process -FilePath $installer -ArgumentList $arguments -Wait -PassThru
    }
    finally {
        Remove-Item -Path Env:\GAPACTIVATOR_SETUP_PASSWORD -ErrorAction SilentlyContinue
    }
    Assert ($ExpectedExit -contains $process.ExitCode) "installer '$Name' exits with $($ExpectedExit -join ' or ') (was $($process.ExitCode))"
    $log
}

# The uninstaller copies itself to temp and re-launches, so the parent's exit does not mean it finished: wait for
# the uninstall entry to disappear.
function Invoke-Uninstall([string]$Name, [switch]$DeleteData) {
    $unins = Join-Path $installDir 'unins000.exe'
    Assert (Test-Path -LiteralPath $unins -PathType Leaf) "$unins exists"
    $log = Join-Path $logDir "$Name.log"
    $arguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/LOG=`"$log`"")
    if ($DeleteData) { $arguments += '/DELETEDATA' }
    $process = Start-Process -FilePath $unins -ArgumentList $arguments -Wait -PassThru
    Assert ($process.ExitCode -eq 0) "the uninstaller '$Name' exits with 0 (was $($process.ExitCode))"
    # The first process re-launches a temp copy of itself, so wait until no uninstaller process remains.
    $finished = Wait-Until { @(Get-Process -Name 'unins*', '_iu*' -ErrorAction SilentlyContinue).Count -eq 0 } 180
    Assert $finished "the uninstaller '$Name' finishes (no unins*/_iu* process) within 180 s"
    Assert (Wait-Until { -not (Test-Path -LiteralPath $uninstallKey) } 180) 'the uninstall entry disappears within 180 s'
    $log
}

function Wait-ServiceRunning([string]$What) {
    $ok = Wait-Until { (Get-ServiceStatus) -eq 'Running' } $ServiceStartTimeoutSeconds
    Assert $ok "$What : the service is Running within $ServiceStartTimeoutSeconds s (status: $(Get-ServiceStatus))"
    $ok = Wait-Until { Test-PipeExists } $ServiceStartTimeoutSeconds
    Assert $ok "$What : \\.\pipe\$serviceName exists within $ServiceStartTimeoutSeconds s"
}

function ConvertFrom-FailureActions([byte[]]$Bytes) {
    # SERVICE_FAILURE_ACTIONS as stored in the registry: reset period, two string offsets, the action count,
    # the offset of the action array (20), then one (type, delay in ms) pair per action. Type 1 is restart.
    Assert ($Bytes.Length -ge 20) 'FailureActions has a header'
    $reset = [BitConverter]::ToUInt32($Bytes, 0)
    $count = [BitConverter]::ToUInt32($Bytes, 12)
    $offset = [BitConverter]::ToUInt32($Bytes, 16)
    $actions = @()
    for ($i = 0; $i -lt $count; $i++) {
        $at = $offset + 8 * $i
        $actions += [pscustomobject]@{
            Type  = [BitConverter]::ToUInt32($Bytes, $at)
            Delay = [BitConverter]::ToUInt32($Bytes, $at + 4)
        }
    }
    [pscustomobject]@{ Reset = $reset; Actions = $actions }
}

# The service configuration, read from the registry (language independent).
function Get-ServiceSnapshot {
    $item = Get-ItemProperty -LiteralPath $serviceKey
    [pscustomobject]@{
        Start         = [int]$item.Start
        Delayed       = [int]$item.DelayedAutostart
        ObjectName    = [string]$item.ObjectName
        ImagePath     = [string]$item.ImagePath
        DisplayName   = [string]$item.DisplayName
        Description   = [string]$item.Description
        NonCrashFlag  = [int]$item.FailureActionsOnNonCrashFailures
        FailureBytes  = (($item.FailureActions | ForEach-Object { '{0:X2}' -f $_ }) -join ' ')
    }
}

function Assert-ServiceConfiguration([string]$ExpectedAccount, [string]$ExpectedAccountPattern, [string]$What) {
    $snapshot = Get-ServiceSnapshot
    Assert ($snapshot.Start -eq 2) "$What : registry Start is 2 (automatic), was $($snapshot.Start)"
    Assert ($snapshot.Delayed -eq 1) "$What : registry DelayedAutostart is 1, was $($snapshot.Delayed)"
    Assert ($snapshot.ObjectName -ieq $ExpectedAccount) "$What : registry ObjectName is '$ExpectedAccount', was '$($snapshot.ObjectName)'"
    $expectedImage = Join-Path $installDir 'Service\GapActivator.Service.exe'
    # The stored path must be quoted: an unquoted path with a space can be hijacked (CWE-428).
    Assert ($snapshot.ImagePath -ceq ('"' + $expectedImage + '"')) "$What : ImagePath is the quoted path `"$expectedImage`", was '$($snapshot.ImagePath)'"
    Assert ($snapshot.ImagePath.StartsWith('"') -and $snapshot.ImagePath.EndsWith('GapActivator.Service.exe"')) "$What : ImagePath starts with a quote and ends with GapActivator.Service.exe and a quote"
    Assert ($snapshot.DisplayName -ceq 'GapActivator') "$What : DisplayName is GapActivator, was '$($snapshot.DisplayName)'"
    Assert ($snapshot.Description.Length -gt 0) "$What : the service has a description"
    Assert ($snapshot.NonCrashFlag -eq 1) "$What : FailureActionsOnNonCrashFailures is 1, was $($snapshot.NonCrashFlag)"
    $failure = ConvertFrom-FailureActions ([byte[]](Get-ItemProperty -LiteralPath $serviceKey).FailureActions)
    Assert ($failure.Reset -eq 86400) "$What : failure reset period is 86400 s, was $($failure.Reset)"
    Assert (@($failure.Actions).Count -eq 3) "$What : three failure actions, found $(@($failure.Actions).Count)"
    foreach ($action in $failure.Actions) {
        Assert ($action.Type -eq 1 -and $action.Delay -eq 60000) "$What : each failure action is restart after 60000 ms (type $($action.Type), delay $($action.Delay))"
    }

    # The brief's sc.exe views (English text).
    $qc = (& sc.exe qc $serviceName | Out-String)
    Assert ($LASTEXITCODE -eq 0) "$What : sc.exe qc succeeds"
    Assert ($qc -match 'AUTO_START\s+\(DELAYED\)') "$What : sc.exe qc shows AUTO_START (DELAYED). Output: $qc"
    Assert ($qc -match $ExpectedAccountPattern) "$What : sc.exe qc shows the account '$ExpectedAccount'. Output: $qc"
    $qfailure = (& sc.exe qfailure $serviceName | Out-String)
    Assert ($LASTEXITCODE -eq 0) "$What : sc.exe qfailure succeeds"
    Assert ($qfailure -match 'RESET_PERIOD \(in seconds\)\s*:\s*86400\b') "$What : sc.exe qfailure shows RESET_PERIOD 86400. Output: $qfailure"
    $restarts = [regex]::Matches($qfailure, 'RESTART -- Delay = 60000 milliseconds').Count
    Assert ($restarts -eq 3) "$What : sc.exe qfailure shows three 'RESTART -- Delay = 60000' actions (found $restarts). Output: $qfailure"
    $qflag = (& sc.exe qfailureflag $serviceName | Out-String)
    Assert ($LASTEXITCODE -eq 0) "$What : sc.exe qfailureflag succeeds"
    Assert ($qflag -match '\bTRUE\b') "$What : sc.exe qfailureflag shows TRUE. Output: $qflag"
}

function Assert-DataRootAcl([string]$ServiceAccount, [string]$What) {
    Assert (Test-Path -LiteralPath $dataRoot -PathType Container) "$What : the data root exists"
    $sidType = [Security.Principal.SecurityIdentifier]
    $acl = Get-Acl -LiteralPath $dataRoot
    $owner = $acl.GetOwner($sidType).Value
    Assert ($owner -eq 'S-1-5-32-544') "$What : the data root owner is Administrators (S-1-5-32-544), was $owner"
    Assert $acl.AreAccessRulesProtected "$What : the data root DACL is protected"
    $rules = @($acl.GetAccessRules($true, $true, $sidType))
    Assert ($rules.Count -eq 3) "$What : the data root has exactly three access entries (found $($rules.Count))"
    $serviceSid = (New-Object Security.Principal.NTAccount($ServiceAccount)).Translate($sidType).Value
    $full = [int][Security.AccessControl.FileSystemRights]::FullControl
    $modify = [int]([Security.AccessControl.FileSystemRights]::Modify -bor [Security.AccessControl.FileSystemRights]::Synchronize)
    $expected = @{ 'S-1-5-18' = $full; 'S-1-5-32-544' = $full }
    $expected[$serviceSid] = $modify
    $both = [Security.AccessControl.InheritanceFlags]'ContainerInherit, ObjectInherit'
    foreach ($rule in $rules) {
        $sid = $rule.IdentityReference.Value
        Assert ($expected.ContainsKey($sid)) "$What : unexpected data root entry for $sid"
        Assert ([int]$rule.FileSystemRights -eq $expected[$sid]) "$What : entry $sid has rights $($expected[$sid]), was $([int]$rule.FileSystemRights)"
        Assert ($rule.AccessControlType -eq 'Allow') "$What : entry $sid is an Allow entry"
        Assert (-not $rule.IsInherited) "$What : entry $sid is not inherited"
        Assert ($rule.InheritanceFlags -eq $both) "$What : entry $sid applies to subfolders and files (was $($rule.InheritanceFlags))"
        Assert ($rule.PropagationFlags -eq 'None') "$What : entry $sid has no propagation flags (was $($rule.PropagationFlags))"
        $expected.Remove($sid)
    }
    Assert ($expected.Count -eq 0) "$What : every expected data root entry is present (missing: $($expected.Keys -join ', '))"
}

function Assert-InstalledFiles([string]$What) {
    foreach ($relative in @('GapActivator.exe', 'LICENSE.txt', 'unins000.exe', 'Service\GapActivator.Service.exe', 'Service\e_sqlite3.dll')) {
        Assert (Test-Path -LiteralPath (Join-Path $installDir $relative) -PathType Leaf) "$What : $relative is installed"
    }
    $pdbs = @(Get-ChildItem -LiteralPath $installDir -Recurse -Force -Filter '*.pdb' -File)
    Assert ($pdbs.Count -eq 0) "$What : no .pdb under the install folder (found $($pdbs.Count): $(($pdbs | Select-Object -First 3 | ForEach-Object Name) -join ', '))"
    Assert (Test-Path -LiteralPath $startMenuShortcut) "$What : the Start Menu shortcut exists ($startMenuShortcut)"
    Assert (Test-Path -LiteralPath $desktopShortcut) "$What : the desktop shortcut exists ($desktopShortcut)"
    Assert (Test-Path -LiteralPath $uninstallKey) "$What : the uninstall entry exists under HKLM"
}

function Assert-EverythingRemoved([string]$What) {
    $gone = Wait-Until { -not (Test-ServiceRegistered) } 60
    Assert $gone "$What : the service registration disappears within 60 s"
    Assert ($null -eq (Get-ServiceStatus)) "$What : the service is gone"
    Assert (-not (Test-Path -LiteralPath $eventSourceKey)) "$What : the event source is gone"
    Assert (-not (Test-GroupExists)) "$What : the group '$groupName' is gone"
    $filesGone = Wait-Until { -not (Test-Path -LiteralPath $installDir) -or @(Get-ChildItem -LiteralPath $installDir -Recurse -Force -File).Count -eq 0 } 60
    $left = if (Test-Path -LiteralPath $installDir) { @(Get-ChildItem -LiteralPath $installDir -Recurse -Force -File | ForEach-Object FullName) } else { @() }
    Assert $filesGone "$What : the install folder holds no files (left: $($left -join ', '))"
    Assert (-not (Test-Path -LiteralPath $startMenuShortcut)) "$What : the Start Menu shortcut is removed"
    Assert (-not (Test-Path -LiteralPath $desktopShortcut)) "$What : the desktop shortcut is removed"
}

# Cleanup helpers: best effort, each reports a warning instead of throwing.
function Invoke-Safely([string]$What, [scriptblock]$Action) {
    try { & $Action } catch { Write-Warning "Cleanup '$What' failed: $($_.Exception.Message)" }
}

# ---- Preconditions: refuse to run over a real installation, because the test removes everything it finds afterwards.
Assert (-not (Test-Path -LiteralPath $uninstallKey)) 'a GapActivator installation is present (uninstall entry exists). Uninstall it first: this test would remove it.'
Assert (-not (Test-ServiceRegistered)) 'a service named GapActivator exists. Remove it first: this test would remove it.'
Assert (-not (Test-Path -LiteralPath $dataRoot)) "$dataRoot exists. Move it away first: this test deletes the data root."
Assert (-not (Test-Path -LiteralPath $installDir)) "$installDir exists. Remove it first: this test installs there."
Assert (-not (Test-Path -LiteralPath $eventSourceKey)) 'the GapActivator event source exists. Remove it first.'
Assert (-not (Test-GroupExists)) "the local group '$groupName' exists. Remove it first."
Assert (-not (Test-Path -LiteralPath $startMenuShortcut)) "$startMenuShortcut exists. Remove it first: this test would delete it."
Assert (-not (Test-Path -LiteralPath $desktopShortcut)) "$desktopShortcut exists. Remove it first: this test would delete it."
Assert (@(Get-Process -Name 'GapActivator', 'GapActivator.Service' -ErrorAction SilentlyContinue).Count -eq 0) 'a GapActivator process is running. Close it first: the installer would detect it.'

$testUser = $null
$passed = $false
try {
    # ---- Phase 1: fresh silent install with the virtual service account.
    Write-Host '== Phase 1: fresh silent install'
    Invoke-Install 'phase1-install' | Out-Null
    Assert-InstalledFiles 'phase 1'
    Assert-ServiceConfiguration $virtualAccount 'SERVICE_START_NAME\s*:\s*NT SERVICE\\GapActivator\s' 'phase 1'
    Wait-ServiceRunning 'phase 1'
    Assert (Test-Path -LiteralPath $eventSourceKey) 'phase 1 : the event source exists'
    Assert (Test-GroupExists) "phase 1 : the group '$groupName' exists"
    $members = @(Get-LocalGroupMember -Group $groupName | ForEach-Object { [string]$_.Name })
    # The installer adds the user of the interactive session, or the process user when no session has one.
    $sessionUser = [string](Get-CimInstance -ClassName Win32_ComputerSystem).UserName
    Assert (@($members | Where-Object { $_ -ieq $currentUser -or ($sessionUser -and $_ -ieq $sessionUser) }).Count -ge 1) "phase 1 : the group has the installing user '$currentUser' (session user '$sessionUser'; members: $($members -join ', '))"
    Assert-DataRootAcl $virtualAccount 'phase 1'

    # ---- Phase 2: upgrade by running the same installer again.
    Write-Host '== Phase 2: upgrade over the running install'
    $markerPath = Join-Path $dataRoot 'upgrade-marker.txt'
    Set-Content -LiteralPath $markerPath -Value 'marker' -Encoding ASCII
    $before = Get-ServiceSnapshot
    $pidBefore = Get-ServiceProcessId
    Assert ($pidBefore -gt 0) 'phase 2 : the service has a process id before the upgrade'
    Invoke-Install 'phase2-upgrade' | Out-Null
    Wait-ServiceRunning 'phase 2'
    $pidAfter = Get-ServiceProcessId
    Assert ($pidAfter -gt 0 -and $pidAfter -ne $pidBefore) "phase 2 : the service was restarted (process id $pidBefore before, $pidAfter after)"
    $after = Get-ServiceSnapshot
    foreach ($property in $before.PSObject.Properties.Name) {
        Assert ($before.$property -ceq $after.$property) "phase 2 : service setting '$property' is unchanged by the upgrade ('$($before.$property)' became '$($after.$property)')"
    }
    Assert-ServiceConfiguration $virtualAccount 'SERVICE_START_NAME\s*:\s*NT SERVICE\\GapActivator\s' 'phase 2'
    Assert-InstalledFiles 'phase 2'
    Assert (Test-Path -LiteralPath $markerPath -PathType Leaf) 'phase 2 : the marker file in the data root survived the upgrade'
    Assert (Test-Path -LiteralPath $eventSourceKey) 'phase 2 : the event source still exists'
    Assert (Test-GroupExists) "phase 2 : the group '$groupName' still exists"
    Assert-DataRootAcl $virtualAccount 'phase 2'

    # ---- Phase 3: silent uninstall without /DELETEDATA keeps the data.
    Write-Host '== Phase 3: silent uninstall keeps the data'
    Invoke-Uninstall 'phase3-uninstall' | Out-Null
    Assert-EverythingRemoved 'phase 3'
    Assert (Test-Path -LiteralPath $dataRoot -PathType Container) 'phase 3 : the data root remains'
    Assert (Test-Path -LiteralPath $markerPath -PathType Leaf) 'phase 3 : the marker file remains in the data root'

    # ---- Phase 4: install again over the kept data, then uninstall with /DELETEDATA.
    Write-Host '== Phase 4: install, then uninstall with /DELETEDATA'
    Invoke-Install 'phase4-install' | Out-Null
    Wait-ServiceRunning 'phase 4'
    Assert-DataRootAcl $virtualAccount 'phase 4'
    Invoke-Uninstall 'phase4-uninstall' -DeleteData | Out-Null
    Assert-EverythingRemoved 'phase 4'
    $dataGone = Wait-Until { -not (Test-Path -LiteralPath $dataRoot) } 60
    Assert $dataGone 'phase 4 : the data root is deleted by /DELETEDATA'

    # ---- Phase 5: a specific account (a throwaway local user with a random password).
    Write-Host '== Phase 5: specific account'
    $suffix = -join ((48..57) + (97..122) | Get-Random -Count 8 | ForEach-Object { [char]$_ })
    $userName = "gapact$suffix"
    $password = 'Aa1-' + (-join ((48..57) + (65..90) + (97..122) | Get-Random -Count 24 | ForEach-Object { [char]$_ }))
    $secretToMask = $password
    $secure = ConvertTo-SecureString -String $password -AsPlainText -Force
    New-LocalUser -Name $userName -Password $secure -AccountNeverExpires -PasswordNeverExpires -UserMayNotChangePassword -Description 'GapActivator installer test (temporary)' | Out-Null
    $testUser = $userName
    # A network logon needs the account to be in Users (the usual default for real accounts).
    Add-LocalGroupMember -SID 'S-1-5-32-545' -Member $userName
    $specificAccount = ".\$userName"
    $installLog = Invoke-Install 'phase5-install' @("/ACCOUNT=`"$specificAccount`"") $password
    Assert-ServiceConfiguration $specificAccount ('SERVICE_START_NAME\s*:\s*\.\\' + [regex]::Escape($userName) + '\s') 'phase 5'
    Wait-ServiceRunning 'phase 5'
    $installText = Get-Content -LiteralPath $installLog -Raw
    Assert ($installText.Contains('Setting the service account to')) 'phase 5 : the Inno log shows the account was set through the API'
    Assert (-not $installText.Contains($password)) 'phase 5 : the Inno install log does not contain the password'
    Assert (-not (Test-GroupExists)) "phase 5 : no '$groupName' group is created with a specific account"
    Assert (Test-Path -LiteralPath $eventSourceKey) 'phase 5 : the event source exists'
    Assert-DataRootAcl "$env:COMPUTERNAME\$userName" 'phase 5'
    $uninstallLog = Invoke-Uninstall 'phase5-uninstall' -DeleteData
    Assert (-not (Get-Content -LiteralPath $uninstallLog -Raw).Contains($password)) 'phase 5 : the Inno uninstall log does not contain the password'
    Assert-EverythingRemoved 'phase 5'
    $dataGone = Wait-Until { -not (Test-Path -LiteralPath $dataRoot) } 60
    Assert $dataGone 'phase 5 : the data root is deleted by /DELETEDATA'
    foreach ($file in Get-ChildItem -LiteralPath $logDir -File) {
        Assert (-not (Get-Content -LiteralPath $file.FullName -Raw).Contains($password)) "phase 5 : $($file.Name) does not contain the password"
    }

    $passed = $true
    Write-Host 'Installer OK'
}
catch {
    Write-Host "Installer test FAILED: $($_.Exception.Message)"
    Show-Logs
    throw
}
finally {
    Write-Host '== Cleanup'
    Invoke-Safely 'uninstall' {
        if (Test-Path -LiteralPath $uninstallKey) {
            $unins = Join-Path $installDir 'unins000.exe'
            if (Test-Path -LiteralPath $unins) {
                Start-Process -FilePath $unins -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/DELETEDATA') -Wait | Out-Null
                Wait-Until { -not (Test-Path -LiteralPath $uninstallKey) } 180 | Out-Null
            }
        }
    }
    Invoke-Safely 'service' {
        if (Test-ServiceRegistered) {
            & sc.exe stop $serviceName | Out-Null
            Wait-Until { (Get-ServiceStatus) -ne 'Running' -and (Get-ServiceStatus) -ne 'StopPending' } 60 | Out-Null
            & sc.exe delete $serviceName | Out-Null
        }
    }
    Invoke-Safely 'event source' {
        if (Test-Path -LiteralPath $eventSourceKey) { Remove-Item -LiteralPath $eventSourceKey -Recurse -Force }
    }
    Invoke-Safely 'group' {
        if (Test-GroupExists) { Remove-LocalGroup -Name $groupName }
    }
    Invoke-Safely 'test user' {
        if ($testUser -and (Get-LocalUser -Name $testUser -ErrorAction SilentlyContinue)) { Remove-LocalUser -Name $testUser }
    }
    Invoke-Safely 'data root' {
        if (Test-Path -LiteralPath $dataRoot) { Remove-Item -LiteralPath $dataRoot -Recurse -Force }
    }
    Invoke-Safely 'install folder' {
        if (Test-Path -LiteralPath $installDir) { Remove-Item -LiteralPath $installDir -Recurse -Force }
    }
    Invoke-Safely 'shortcuts' {
        foreach ($shortcut in @($startMenuShortcut, $desktopShortcut)) {
            if (Test-Path -LiteralPath $shortcut) { Remove-Item -LiteralPath $shortcut -Force }
        }
    }
    Remove-Item -Path Env:\GAPACTIVATOR_SETUP_PASSWORD -ErrorAction SilentlyContinue
    if (-not $passed) { Write-Host 'Installer test did not pass.' }
}
