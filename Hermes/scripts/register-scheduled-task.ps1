# register-scheduled-task.ps1 — register the nightly Hermes run in Windows Task Scheduler.
#
# Same headless pattern as the Syncthing service: runs as dev.rob whether logged on or not.
# Only add this AFTER Hermes is proven with on-demand runs (Phase 1 build order, step 7).
#
# Usage (elevated PowerShell on devRyzen, from the Hermes project folder):
#   powershell -ExecutionPolicy Bypass -File scripts\register-scheduled-task.ps1
#
# Prompts once for the dev.rob password (needed to run when not logged on).
# Re-running updates the existing task.

$ErrorActionPreference = 'Stop'

$taskName   = 'Hermes Nightly'
$projectDir = Split-Path -Parent $PSScriptRoot          # ...\Hermes
$runScript  = Join-Path $projectDir 'scripts\run-hermes.cmd'
$user       = "$env:COMPUTERNAME\dev.rob"   # adjust if dev.rob is a domain account
$time       = '2:00AM'

if (-not (Test-Path $runScript)) {
    throw "run-hermes.cmd not found at $runScript"
}

# Launch the .cmd via cmd.exe with an explicit working directory — more reliable
# under Task Scheduler than executing the .cmd path directly.
$action   = New-ScheduledTaskAction -Execute 'cmd.exe' `
                -Argument "/c `"$runScript`"" -WorkingDirectory $projectDir
$trigger  = New-ScheduledTaskTrigger -Daily -At $time
$settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -DontStopOnIdleEnd `
                -ExecutionTimeLimit (New-TimeSpan -Hours 1)

$password = Read-Host "Password for $user" -AsSecureString
$bstr     = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($password)
try {
    $plain = [Runtime.InteropServices.Marshal]::PtrToStringAuto($bstr)

    Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger `
        -Settings $settings -User $user -Password $plain -RunLevel Limited -Force `
        -Description 'Nightly Hermes run: turn new intents into specs via local LLM.'
}
finally {
    # Zero + free the unmanaged plaintext password buffer and drop the managed copy.
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
    Remove-Variable plain -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "Registered '$taskName' to run daily at $time as $user (whether logged on or not)."
Write-Host "Test it now with:  Start-ScheduledTask -TaskName '$taskName'"
Write-Host "Remove it with:    Unregister-ScheduledTask -TaskName '$taskName' -Confirm:`$false"
