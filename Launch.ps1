$ErrorActionPreference = 'Stop'
# Keep this script ASCII-compatible with Windows PowerShell 5.1.
try {
    $executable = Join-Path $PSScriptRoot 'ThermalrightColorStudio.exe'
    if (!(Test-Path -LiteralPath $executable)) {
        throw 'Application not found. Extract the complete portable ZIP before running Launch.cmd.'
    }
    $dataDirectory = Join-Path $PSScriptRoot 'data'
    Start-Process -FilePath $executable -WorkingDirectory $PSScriptRoot -ArgumentList @('--data-dir', ('"' + $dataDirectory + '"')) -Verb RunAs
}
catch {
    $message = $_.Exception.Message
    try { $message | Out-File -LiteralPath (Join-Path $PSScriptRoot 'launch-error.txt') -Encoding utf8 } catch { }
    Add-Type -AssemblyName System.Windows.Forms
    [System.Windows.Forms.MessageBox]::Show($message, 'Thermalright Color Studio - Launch failed') | Out-Null
    exit 1
}
