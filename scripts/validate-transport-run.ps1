<#
.SYNOPSIS
    Run one conpty-mode smoke row end-to-end.

.DESCRIPTION
    Copies dev-configs/validate-transport/<Row>.conf to a temp file,
    launches the built Ghostty.exe with --config-file pointing at it,
    waits up to -TimeoutMs for exit, then invokes
    scripts/validate-transport-assert.ps1 -Row <Row> and exits with
    its exit code.

    Assumes the app is already built. Call from `just
    validate-transport-smoke <Row>` which chains the build.

.PARAMETER Row
    One of: pwsh-auto, pwsh-always, pwsh-never, cmd-auto.

.PARAMETER TimeoutMs
    Safety timeout. Defaults to 15000 (15 seconds).

.PARAMETER ExePath
    Path to the built Ghostty.exe. Defaults to the Debug x64 output.
#>
param(
    [Parameter(Mandatory)][string]$Row,
    [int]$TimeoutMs = 15000,
    [string]$ExePath = './windows/Ghostty/bin/x64/Debug/net10.0-windows10.0.19041.0/Ghostty.exe'
)
$ErrorActionPreference = 'Stop'

$fixturePath = "dev-configs/validate-transport/$Row.conf"
if (-not (Test-Path $fixturePath)) {
    Write-Host "ERROR: fixture not found: $fixturePath"
    exit 2
}
if (-not (Test-Path $ExePath)) {
    Write-Host "ERROR: exe not found: $ExePath (run ``just build-dll build-win`` first)"
    exit 2
}

$tmpConfig = Join-Path $env:TEMP "ghostty-validate-$Row.conf"
Copy-Item -LiteralPath $fixturePath -Destination $tmpConfig -Force

Write-Host "launching: $ExePath --config-file=$tmpConfig"
$proc = Start-Process -FilePath $ExePath -ArgumentList "--config-file=$tmpConfig" -PassThru
$exited = $proc.WaitForExit($TimeoutMs)
if (-not $exited) {
    Write-Host "WARN: app did not exit within ${TimeoutMs}ms, killing"
    try { Stop-Process -Id $proc.Id -Force } catch {}
    Write-Host "FAIL: $Row (timeout)"
    exit 2
}

Write-Host "app exited with code $($proc.ExitCode); running assertion"
pwsh -NoProfile -File scripts/validate-transport-assert.ps1 -Row $Row
exit $LASTEXITCODE
