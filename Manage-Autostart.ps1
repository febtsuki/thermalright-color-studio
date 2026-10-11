param([switch]$Disable, [string]$ResultPath)
$ErrorActionPreference = 'Stop'
$taskName = 'Thermalright Color Studio'
$studioExe = Join-Path $PSScriptRoot 'ThermalrightColorStudio.exe'
try {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        $arguments = '-NoProfile -ExecutionPolicy Bypass -File "' + $PSCommandPath + '"'
        if ($Disable) { $arguments += ' -Disable' }
        if ($ResultPath) { $arguments += ' -ResultPath "' + $ResultPath + '"' }
        $child = Start-Process -FilePath "$env:SystemRoot/System32/WindowsPowerShell/v1.0/powershell.exe" -ArgumentList $arguments -Verb RunAs -WindowStyle Hidden -Wait -PassThru
        exit $child.ExitCode
    }
    $existing = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
    if ($existing -and $existing.Description -notlike 'Color Studio managed startup:*') { throw 'A different task already uses this name; it was not changed.' }
    if ($Disable) {
        if ($existing) { Unregister-ScheduledTask -TaskName $taskName -Confirm:$false }
        $result = @{Enabled=$false;TaskName=$taskName}
    } else {
        if (-not (Test-Path -LiteralPath $studioExe -PathType Leaf)) { throw 'ThermalrightColorStudio.exe must be beside this script.' }
        $action = New-ScheduledTaskAction -Execute $studioExe -WorkingDirectory $PSScriptRoot
        $trigger = New-ScheduledTaskTrigger -AtLogOn -User $identity.User.Value
        $trigger.Delay = 'PT15S'
        $taskPrincipal = New-ScheduledTaskPrincipal -UserId $identity.User.Value -LogonType Interactive -RunLevel Highest
        $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew
        Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Principal $taskPrincipal -Settings $settings -Description 'Color Studio managed startup: launch the portable app 15 seconds after this user signs in.' -Force | Out-Null
        $saved = Get-ScheduledTask -TaskName $taskName
        if ($saved.Actions.Execute -ne $studioExe -or $saved.Principal.RunLevel -ne 'Highest' -or $saved.Triggers.Delay -ne 'PT15S') { throw 'Registered task verification failed.' }
        $result = @{Enabled=$true;TaskName=$taskName;Executable=$saved.Actions.Execute;RunLevel=[string]$saved.Principal.RunLevel;LogonType=[string]$saved.Principal.LogonType;Delay=$saved.Triggers.Delay;ExecutionTimeLimit=$saved.Settings.ExecutionTimeLimit}
    }
    if ($ResultPath) { $result | ConvertTo-Json | Set-Content -LiteralPath $ResultPath -Encoding UTF8 }
    $result | ConvertTo-Json
} catch {
    if ($ResultPath) { @{Enabled=$null;Error=$_.Exception.Message} | ConvertTo-Json | Set-Content -LiteralPath $ResultPath -Encoding UTF8 }
    Write-Error $_
    exit 1
}
