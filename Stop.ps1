# Stop only the services tracked by the Moodify local runner.
# Usage: .\Stop.ps1
$ErrorActionPreference = "Stop"
$runner = Join-Path $PSScriptRoot "scripts\moodify-local.ps1"
if (-not (Test-Path -LiteralPath $runner)) {
    throw "Local service runner not found: $runner"
}

& $runner -Action Stop
